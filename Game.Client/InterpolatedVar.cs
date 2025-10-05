﻿using SharpCompress.Common;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Hashing;
using Source.Common.Mathematics;

using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Client;

public enum LatchFlags
{
	LatchAnimationVar = 1 << 0,
	LatchSimulationVar = 1 << 1,
	ExcludeAutoLatch = 1 << 2,
	ExcludeAutoInterpolate = 1 << 3,
	InterpolateLinearOnly = 1 << 4,
	InteroplateOmitUpdateLastNetworked = 1 << 5,
}

public class InterpolationContext
{
	static bool AllowExtrapolation;
	static TimeUnit_t LastTimeStamp;
	internal static void EnableExtrapolation(bool value) => AllowExtrapolation = value;
	internal static bool IsExtrapolationAllowed() => AllowExtrapolation;
	internal static void SetLastTimeStamp(TimeUnit_t value) => LastTimeStamp = value;
	internal static TimeUnit_t GetLastTimeStamp() => LastTimeStamp;
}

public interface IInterpolatedVar
{
	public const double EXTRA_INTERPOLATION_HISTORY_STORED = 0.05;

	void Setup(object instance, DynamicAccessor accessor, LatchFlags type);
	void SetInterpolationAmount(TimeUnit_t seconds);
	void NoteLastNetworkedValue();
	bool NoteChanged(TimeUnit_t changeTime, bool updateLastNetworkedValue);
	void Reset();
	int Interpolate(TimeUnit_t seconds);
	LatchFlags GetVarType();
	void RestoreToLastNetworked();
	void Copy(IInterpolatedVar src);
	ReadOnlySpan<char> GetDebugName();
	void SetDebugName(ReadOnlySpan<char> name);
}

public class SimpleRingBuffer<T> where T : new()
{
	private T[]? elements;
	private ushort maxElement;
	private ushort firstElement;
	private ushort count;
	private ushort growSize;

	public SimpleRingBuffer(int startSize = 4) {
		elements = null;
		maxElement = 0;
		firstElement = 0;
		count = 0;
		growSize = 16;
		EnsureCapacity(startSize);
	}

	public int Count() => count;

	public int Head() => (count > 0) ? 0 : InvalidIndex();

	public bool IsIdxValid(int i) => (i >= 0 && i < count);

	public bool IsValidIndex(int i) => IsIdxValid(i);

	public int InvalidIndex() => -1;

	public ref T this[int i] {
		get {
			Assert(IsIdxValid(i));
			i += firstElement;
			i = WrapRange(i);
			return ref elements![i];
		}
	}

	public void EnsureCapacity(int capSize) {
		if (capSize > maxElement) {
			int newMax = maxElement + ((capSize + growSize - 1) / growSize) * growSize;
			T[] pNew = new T[newMax];

			for (int i = 0; i < count; i++) {
				pNew[i] = elements![WrapRange(i + firstElement)];
			}

			firstElement = 0;
			maxElement = (ushort)newMax;
			elements = pNew;
		}
	}

	public int AddToHead() {
		EnsureCapacity(count + 1);
		int i = firstElement + maxElement - 1;
		count++;
		i = WrapRange(i);
		firstElement = (ushort)i;
		elements![firstElement] = new T();
		return 0;
	}

	public int AddToHead(T elem) {
		AddToHead();
		elements![firstElement] = elem;
		return 0;
	}

	public int AddToTail() {
		EnsureCapacity(count + 1);
		count++;
		int index = WrapRange(firstElement + count - 1);
		elements![index] = new T();
		return index;
	}

	public void RemoveAll() {
		count = 0;
		firstElement = 0;
	}

	public void RemoveAtHead() {
		if (count > 0) {
			firstElement = (ushort)WrapRange(firstElement + 1);
			count--;
		}
	}

	public void Truncate(int newLength) {
		if (newLength < count) {
			System.Diagnostics.Debug.Assert(newLength >= 0);
			count = (ushort)newLength;
		}
	}

	private int WrapRange(int i) {
		return (i >= maxElement) ? (i - maxElement) : i;
	}
}

public struct InterpolatedVarEntryBase<T>
{
	public readonly bool IsArray;

	public TimeUnit_t ChangeTime;
	public T[]? Value;

	public InterpolatedVarEntryBase(bool isArray) {
		IsArray = isArray;
	}

	public T[]? GetValue() => Value;
	public void Init(int maxCount) {
		if (maxCount == 0) {
			DeleteEntry();
		}
		else {
			if (maxCount != Value?.Length)
				DeleteEntry();

			if (Value == null) {
				Value = new T[maxCount];
			}
		}
	}

	public Span<T> NewEntry(object instance, DynamicAccessor accessor, int maxCount, TimeUnit_t time) {
		ChangeTime = time;
		Init(accessor.Length);
		if (Value != null && maxCount != 0)
			accessor.CopyTo(instance, Value.AsSpan());
		return Value;
	}


	public Span<T> NewEntry(object instance, Span<T> data, TimeUnit_t time) {
		ChangeTime = time;
		Init(data.Length);
		if (Value != null && data.Length != 0)
			data.CopyTo(Value.AsSpan());
		return Value;
	}


	public void DeleteEntry() {
		Value = null;
	}

	internal void FastTransferFrom(ref InterpolatedVarEntryBase<T> src) {
		Value = src.Value;
		ChangeTime = src.ChangeTime;
		src.Value = null;
	}
}

public class InterpolatedVarArrayBase<T>(bool isArray) : IInterpolatedVar
{
	SimpleRingBuffer<InterpolatedVarEntryBase<T>> VarHistory = new();
	public void Copy(IInterpolatedVar inSrc) {
		InterpolatedVarArrayBase<T>? src = (InterpolatedVarArrayBase<T>?)inSrc;

		if (src == null || src.MaxCount != MaxCount) {
			if (src != null)
				AssertMsg(false, $"src.MaxCount ({src.MaxCount}) != MaxCount ({MaxCount}) for {GetDebugName()}.");
			else
				AssertMsg(false, "src was null in InterpolatedVarArrayBase<T>.Copy.");

			return;
		}

		Assert((Type & ~LatchFlags.ExcludeAutoInterpolate) == (src.Type & ~LatchFlags.ExcludeAutoInterpolate));
		for (int i = 0; i < MaxCount; i++) {
			LastNetworkedValue![i] = src.LastNetworkedValue![i];
			Looping![i] = src.Looping![i];
		}

		LastNetworkedTime = src.LastNetworkedTime;

		VarHistory.RemoveAll();

		for (nint i = 0; i < src.VarHistory.Count(); i++) {
			nint newslot = VarHistory.AddToTail();

			ref InterpolatedVarEntryBase<T> outDest = ref VarHistory[(int)newslot];
			ref InterpolatedVarEntryBase<T> refSrc = ref src.VarHistory[(int)i];
			outDest.NewEntry(Instance, refSrc.GetValue().AsSpan()[..MaxCount], refSrc.ChangeTime);
		}
	}

	public virtual ReadOnlySpan<char> GetDebugName() {
		return "NO NAME";
	}

	public LatchFlags GetVarType() => Type;

	public struct InterpolationInfo
	{
		public bool Hermite;
		public int Oldest;
		public int Older;
		public int Newer;
		public double Fraction;
	}
	public int Interpolate(TimeUnit_t currentTime) => Interpolate(currentTime, InterpolationAmount);
	public int Interpolate(TimeUnit_t currentTime, TimeUnit_t interpolationAmount) {
		int noMoreChanges = 0;
		if (GetInterpolationInfo(out InterpolationInfo info, currentTime, interpolationAmount, out noMoreChanges))
			return noMoreChanges;

		var history = VarHistory;

		if (info.Hermite)
			_Interpolate_Hermite(Instance, Accessor, info.Fraction, ref history[info.Oldest], ref history[info.Older], ref history[info.Newer]);
		else if (info.Newer == info.Older) {
			int realOlder = info.Newer + 1;
			if (InterpolationContext.IsExtrapolationAllowed() && IsValidIndex(realOlder) && history[realOlder].ChangeTime != 0.0 && interpolationAmount > 0.000001 && InterpolationContext.GetLastTimeStamp() <= LastNetworkedTime)
				_Extrapolate(Instance, Accessor, ref history[realOlder], ref history[info.Newer], currentTime - interpolationAmount, Interpolation.cl_extrapolate_amount.GetFloat());
			else
				_Interpolate(Instance, Accessor, info.Fraction, ref history[info.Older], ref history[info.Newer]);
		}
		else {
			_Interpolate(Instance, Accessor, info.Fraction, ref history[info.Older], ref history[info.Newer]);
		}

		RemoveEntriesPreviousTo(currentTime - interpolationAmount - IInterpolatedVar.EXTRA_INTERPOLATION_HISTORY_STORED);
		return noMoreChanges;
	}

	private void _Interpolate(object instance, DynamicAccessor accessor, double fraction, ref InterpolatedVarEntryBase<T> start, ref InterpolatedVarEntryBase<T> end) {
		if (Unsafe.AreSame(ref start, ref end)) {
			accessor.CopyFrom(instance, end.GetValue()!.AsSpan()[..MaxCount]);
			return;
		}

		Assert(fraction >= 0.0 && fraction <= 1.0);
		for (int i = 0; i < MaxCount; i++) {
			if (Looping![i])
				accessor.AtIndex(i)!.SetValue(instance, LerpFunctions.Lerp_Clamp(LerpFunctions.LoopingLerp(fraction, start.GetValue()![i], end.GetValue()![i])));
			else
				accessor.AtIndex(i)!.SetValue(instance, LerpFunctions.Lerp_Clamp(LerpFunctions.Lerp(fraction, start.GetValue()![i], end.GetValue()![i])));
		}
	}



	private void _Extrapolate(object instance, DynamicAccessor accessor, ref InterpolatedVarEntryBase<T> oldEntry, ref InterpolatedVarEntryBase<T> newEntry, double destinationTime, float maxExtrapolationAmount) {
		if (Math.Abs(oldEntry.ChangeTime - newEntry.ChangeTime) < 0.001 || destinationTime <= newEntry.ChangeTime) {
			for (int i = 0; i < MaxCount; i++)
				accessor.CopyFrom(instance, newEntry.GetValue()!.AsSpan()[..MaxCount]);
		}
		else {
			double extrapolationAmount = Math.Min(destinationTime - newEntry.ChangeTime, maxExtrapolationAmount);

			double divisor = 1.0 / (newEntry.ChangeTime - oldEntry.ChangeTime);
			for (int i = 0; i < MaxCount; i++) {
				accessor.AtIndex(i)!.SetValue(instance, ExtrapolateInterpolatedVarType(oldEntry.GetValue()![i], newEntry.GetValue()![i], divisor, extrapolationAmount));
			}
		}
	}

	private T ExtrapolateInterpolatedVarType(in T oldVal, in T newVal, double divisor, double extrapolationAmount) {
		// Refer to LerpFunctions comment - this all gets collapsed/boxing no-op'd by the JIT it seems
		if (typeof(T) == typeof(float)) {
			float from = (float)(object)oldVal;
			float to = (float)(object)newVal;
			return (T)(object)LerpFunctions.Lerp(1.0f + extrapolationAmount * divisor, in from, in to);
		}
		else if (typeof(T) == typeof(double)) {
			double from = (double)(object)oldVal;
			double to = (double)(object)newVal;
			return (T)(object)LerpFunctions.Lerp(1.0f + extrapolationAmount * divisor, in from, in to);
		}
		else if (typeof(T) == typeof(Vector3)) {
			Vector3 from = (Vector3)(object)oldVal;
			Vector3 to = (Vector3)(object)newVal;
			return (T)(object)LerpFunctions.Lerp(1.0f + extrapolationAmount * divisor, in from, in to);
		}
		else if (typeof(T) == typeof(QAngle)) {
			QAngle from = (QAngle)(object)oldVal;
			QAngle to = (QAngle)(object)newVal;
			return (T)(object)LerpFunctions.Lerp(1.0f + extrapolationAmount * divisor, in from, in to);
		}
		else
			return newVal;
	}

	private void _Interpolate_Hermite(object instance, DynamicAccessor accessor, double fraction, ref InterpolatedVarEntryBase<T> prev, ref InterpolatedVarEntryBase<T> start, ref InterpolatedVarEntryBase<T> end) {
		InterpolatedVarEntryBase<T> fixup = default;
		fixup.Init(MaxCount);

		TimeFixup_Hermite(ref fixup, ref prev, ref start, ref end);
		for (int i = 0; i < MaxCount; i++) {
			// Note that QAngle has a specialization that will do quaternion interpolation here...
			if (Looping![i]) {
				accessor.AtIndex(i)!.SetValue(instance, LerpFunctions.Lerp_Clamp(LerpFunctions.LoopingLerp_Hermite(fraction, prev.GetValue()![i], start.GetValue()![i], end.GetValue()![i])));
			}
			else {
				accessor.AtIndex(i)!.SetValue(instance, LerpFunctions.Lerp_Clamp(LerpFunctions.Lerp_Hermite(fraction, prev.GetValue()![i], start.GetValue()![i], end.GetValue()![i])));
			}
		}
	}

	private void TimeFixup_Hermite(ref InterpolatedVarEntryBase<T> fixup, ref InterpolatedVarEntryBase<T> prev, ref InterpolatedVarEntryBase<T> start, ref InterpolatedVarEntryBase<T> end)
		=> TimeFixup_Hermite2(ref fixup, ref prev, ref start, end.ChangeTime - start.ChangeTime);

	private void TimeFixup_Hermite2(ref InterpolatedVarEntryBase<T> fixup, ref InterpolatedVarEntryBase<T> prev, ref InterpolatedVarEntryBase<T> start, double dt1) {
		double dt2 = start.ChangeTime - prev.ChangeTime;

		if (Math.Abs(dt1 - dt2) > 0.0001 &&
			dt2 > 0.0001f) {
			double frac = dt1 / dt2;

			fixup.ChangeTime = start.ChangeTime - dt1;

			for (int i = 0; i < MaxCount; i++) {
				if (Looping![i])
					fixup.GetValue()![i] = LerpFunctions.LoopingLerp(1 - frac, prev.GetValue()![i], start.GetValue()![i]);
				else
					fixup.GetValue()![i] = LerpFunctions.Lerp(1 - frac, prev.GetValue()![i], start.GetValue()![i]);
			}

			prev = ref fixup;
		}
	}

	private bool IsValidIndex(int i) => VarHistory.IsValidIndex(i);

	public static bool COMPARE_HISTORY(T[]? _a, T[]? _b) {
		if (typeof(T) == typeof(float)) {
			float[]? a = (float[]?)(object?)_a;
			float[]? b = (float[]?)(object?)_b;
			for (int i = 0; i < a?.Length && i < b?.Length; i++)
				if (a[i] != b[i])
					return true;
			return false;
		}
		else if (typeof(T) == typeof(double)) {
			double[]? a = (double[]?)(object?)_a;
			double[]? b = (double[]?)(object?)_b;
			for (int i = 0; i < a?.Length && i < b?.Length; i++)
				if (a[i] != b[i])
					return true;
			return false;
		}
		else if (typeof(T) == typeof(Vector3)) {
			Vector3[]? a = (Vector3[]?)(object?)_a;
			Vector3[]? b = (Vector3[]?)(object?)_b;
			for (int i = 0; i < a?.Length && i < b?.Length; i++)
				if (a[i] != b[i])
					return true;
			return false;
		}
		else if (typeof(T) == typeof(QAngle)) {
			QAngle[]? a = (QAngle[]?)(object?)_a;
			QAngle[]? b = (QAngle[]?)(object?)_b;
			for (int i = 0; i < a?.Length && i < b?.Length; i++)
				if (a[i] != b[i])
					return true;
			return false;
		}

		throw new NotImplementedException();
	}

	private bool GetInterpolationInfo(out InterpolationInfo info, double currentTime, double interpolationAmount, out int noMoreChanges) {
		var varHistory = VarHistory;

		double targetTime = currentTime - interpolationAmount;
		info = default;
		noMoreChanges = default;

		info.Hermite = false;
		info.Fraction = 0;
		info.Oldest = info.Older = info.Newer = varHistory.InvalidIndex();
		for (int i = 0; i < varHistory.Count(); i++) {
			info.Older = i;

			double olderChangeTime = VarHistory[i].ChangeTime;
			if (olderChangeTime == 0.0f)
				break;

			if (targetTime < olderChangeTime) {
				info.Newer = info.Older;
				continue;
			}

			if (info.Newer == varHistory.InvalidIndex()) {
				info.Newer = info.Older;

				noMoreChanges = 1;
				return true;
			}

			double newerChangeTime = varHistory[info.Newer].ChangeTime;
			double dt = newerChangeTime - olderChangeTime;
			if (dt > 0.0001f) {
				info.Fraction = (targetTime - olderChangeTime) / (newerChangeTime - olderChangeTime);
				info.Fraction = Math.Min(info.Fraction, 2.0f);

				int oldestindex = i + 1;

				if ((Type & LatchFlags.InterpolateLinearOnly) == 0 && varHistory.IsIdxValid(oldestindex)) {
					info.Oldest = oldestindex;
					double oldestChangeTime = varHistory[oldestindex].ChangeTime;
					double dt2 = olderChangeTime - oldestChangeTime;
					if (dt2 > 0.0001f)
						info.Hermite = true;
				}

				if (info.Newer == VarHistory.Head()) {
					if (COMPARE_HISTORY(VarHistory[info.Newer].GetValue(), VarHistory[info.Oldest].GetValue())) {
						if (!info.Hermite || COMPARE_HISTORY(VarHistory[info.Newer].GetValue(), VarHistory[info.Oldest].GetValue()))
							noMoreChanges = 1;
					}
				}
			}

			return true;
		}

		if (info.Newer != varHistory.InvalidIndex()) {
			info.Older = info.Newer;
			return true;
		}


		info.Newer = info.Older;
		return (info.Older != varHistory.InvalidIndex());
	}

	public bool NoteChanged(TimeUnit_t changeTime, bool updateLastNetworkedValue) => NoteChanged(changeTime, InterpolationAmount, updateLastNetworkedValue);
	public bool NoteChanged(TimeUnit_t changeTime, TimeUnit_t interpolationAmount, bool updateLastNetworkedValue) {
		bool ret = true;

		if (VarHistory.Count() != 0) {
			// todo: optimize interpolation if no new value
		}

		AddToHead(changeTime, Accessor, true);

		if (updateLastNetworkedValue) {
			NoteLastNetworkedValue();
		}
		RemoveEntriesPreviousTo(gpGlobals.CurTime - interpolationAmount - IInterpolatedVar.EXTRA_INTERPOLATION_HISTORY_STORED);

		return ret;
	}

	private void RemoveEntriesPreviousTo(double time) {
		for (int i = 0; i < VarHistory.Count(); i++) {
			if (VarHistory[i].ChangeTime < time) {
				VarHistory.Truncate(i + 3);
				break;
			}
		}
	}

	private void AddToHead(double changeTime, DynamicAccessor values, bool flushNewer) {
		int newslot;

		if (flushNewer) {
			while (VarHistory.Count() != 0) {
				if ((VarHistory[0].ChangeTime + 0.0001) > changeTime)
					VarHistory.RemoveAtHead();
				else
					break;
			}

			newslot = VarHistory.AddToHead();
		}
		else {
			newslot = VarHistory.AddToHead();
			for (int i = 1; i < VarHistory.Count(); i++) {
				if (VarHistory[i].ChangeTime <= changeTime)
					break;
				VarHistory[newslot].FastTransferFrom(ref VarHistory[i]);
				newslot = i;
			}
		}

		ref InterpolatedVarEntryBase<T> e = ref VarHistory[newslot];
		e.NewEntry(Instance, values, MaxCount, changeTime);
	}

	public void NoteLastNetworkedValue() {
		LastNetworkedValue![0] = Accessor.GetValue<T>(Instance);
		LastNetworkedTime = Interpolation.LastPacketTimestamp;
	}

	public void Reset() {
		ClearHistory();

		if (Accessor != null) {
			AddToHead(gpGlobals.CurTime, Accessor, false);
			AddToHead(gpGlobals.CurTime, Accessor, false);
			AddToHead(gpGlobals.CurTime, Accessor, false);

			Accessor.CopyTo(Instance, LastNetworkedValue.AsSpan());
		}
	}

	public void SetMaxCount(int newmax) {
		bool changed = newmax != MaxCount ? true : false;
		newmax = Math.Max(1, newmax);
		if (changed) {
			Looping = null;
			LastNetworkedValue = null;
			Looping = new bool[newmax];
			LastNetworkedValue = new T[newmax];
			Reset();
		}
	}

	private void ClearHistory() {
		for (int i = 0; i < VarHistory.Count(); i++)
			VarHistory[i].DeleteEntry();

		VarHistory.RemoveAll();
	}

	public void RestoreToLastNetworked() {
		Accessor.CopyFrom(Instance, LastNetworkedValue.AsSpan());
	}

	public void SetDebugName(ReadOnlySpan<char> name) {

	}

	public void SetInterpolationAmount(TimeUnit_t seconds) {
		InterpolationAmount = seconds;
	}

	public void Setup(object instance, DynamicAccessor accessor, LatchFlags type) {
		this.Instance = instance;
		this.Accessor = accessor;
		this.Type = type;
	}
	object Instance;
	DynamicAccessor Accessor;
	LatchFlags Type;
	TimeUnit_t InterpolationAmount;
	TimeUnit_t LastNetworkedTime;
	T[]? LastNetworkedValue;
	int MaxCount => LastNetworkedValue?.Length ?? 0;
	bool[]? Looping;
}

public class InterpolatedVar<T> : InterpolatedVarArrayBase<T>
{
	string name;
	public InterpolatedVar(string name = "no debug name") : base(false) {
		this.name = name;
		SetMaxCount(1);
	}

	public override ReadOnlySpan<char> GetDebugName() {
		return name;
	}
}
public class InterpolatedVarArray<T> : InterpolatedVarArrayBase<T>
{
	string name;
	public InterpolatedVarArray(int count, string name = "no debug name") : base(true) {
		this.name = name;
		SetMaxCount(count);
	}
	public override ReadOnlySpan<char> GetDebugName() {
		return name;
	}
}


[EngineComponent]
public class Interpolation
{
	internal static ConVar cl_extrapolate_amount = new("0.25", FCvar.Cheat, "Set how many seconds the client will extrapolate entities for.");

	public static TimeUnit_t LastPacketTimestamp;
	internal void SetLastPacketTimeStamp(TimeUnit_t timestamp) {
		LastPacketTimestamp = timestamp;
	}



	public static Vector3 ExtrapolateInterpolatedVarType(in Vector3 oldVal, in Vector3 newVal, double divisor, double extrapolationAmount)
		=> Vector3.Lerp(oldVal, newVal, (float)(1.0 + extrapolationAmount * divisor));
	public static QAngle ExtrapolateInterpolatedVarType(in QAngle oldVal, in QAngle newVal, double divisor, double extrapolationAmount)
		=> QAngle.Lerp(oldVal, newVal, (float)(1.0 + extrapolationAmount * divisor));
	public static float ExtrapolateInterpolatedVarType(float oldVal, float newVal, double divisor, double extrapolationAmount)
		=> float.Lerp(oldVal, newVal, (float)(1.0 + extrapolationAmount * divisor));
	public static double ExtrapolateInterpolatedVarType(double oldVal, double newVal, double divisor, double extrapolationAmount)
		=> double.Lerp(oldVal, newVal, 1.0 + extrapolationAmount * divisor);
}