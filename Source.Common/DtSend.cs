﻿using CommunityToolkit.HighPerformance;

using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

using static Source.Common.Networking.svc_ClassInfo;

namespace Source.Common;

public delegate int ArrayLengthSendProxyFn(object instance, int objectID);
public static class SendPropHelpers
{
	public static void SendProxy_AngleToFloat(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) {
		float angle = prop.GetValue<float>(instance);
		outData.Float = MathLib.AngleMod(angle);
	}
	public static void SendProxy_FloatToFloat(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) {
		outData.Float = prop.GetValue<float>(instance);
	}
	public static void SendProxy_QAngles(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) {
		var v = prop.GetValue<QAngle>(instance);
		outData.Vector[0] = MathLib.AngleMod(v.X);
		outData.Vector[1] = MathLib.AngleMod(v.Y);
		outData.Vector[2] = MathLib.AngleMod(v.Z);
	}
	public static void SendProxy_VectorToVector(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) {
		var v = prop.GetValue<Vector3>(instance);
		outData.Vector[0] = MathLib.AngleMod(v.X);
		outData.Vector[1] = MathLib.AngleMod(v.Y);
		outData.Vector[2] = MathLib.AngleMod(v.Z);
	}
	public static void SendProxy_VectorXYToVectorXY(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) {
		var v = prop.GetValue<Vector3>(instance);
		outData.Vector[0] = MathLib.AngleMod(v.X);
		outData.Vector[1] = MathLib.AngleMod(v.Y);
	}
	public static void SendProxy_Int8ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = prop.GetValue<sbyte>(instance);
	public static void SendProxy_Int16ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = prop.GetValue<short>(instance);
	public static void SendProxy_Int32ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = prop.GetValue<int>(instance);
	public static void SendProxy_UInt8ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = prop.GetValue<byte>(instance);
	public static void SendProxy_UInt16ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = prop.GetValue<ushort>(instance);
	public static void SendProxy_UInt32ToInt32(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.Int = (int)prop.GetValue<uint>(instance);
	public static void SendProxy_StringToString(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID)
		=> outData.String = prop.GetValue<string>(instance);
	public static object SendProxy_DataTableToDataTable(SendProp prop, object instance, FieldInfo data, SendProxyRecipients recipients, int objectID)
		=> prop.GetValue<object>(instance);
	public static void SendProxy_Empty(SendProp prop, object instance, FieldInfo data, ref DVariant outData, int element, int objectID) { }


	static float AssignRangeMultiplier(int bits, double range) {
		uint highValue;
		if (bits == 32)
			highValue = 0xFFFFFFFE;
		else
			highValue = (uint)((1 << bits) - 1);

		float fHighLowMul = (float)(Math.Abs(range) <= double.Epsilon ? highValue : highValue / range);

		if ((uint)(fHighLowMul * range) > highValue ||
			 (fHighLowMul * range) > (double)highValue) {
			Span<float> multipliers = [0.9999f, 0.99f, 0.9f, 0.8f, 0.7f];
			int i;
			for (i = 0; i < multipliers.Length; i++) {
				fHighLowMul = (float)(highValue / range) * multipliers[i];
				if ((uint)(fHighLowMul * range) > highValue || (fHighLowMul * range) > (double)highValue) {
				}
				else
					break;
			}

			if (i == multipliers.Length) {
				Assert(false);
				return 0;
			}
		}

		return fHighLowMul;
	}

	static readonly string[] ElementNames = GeneratePaddedStrings(Constants.MAX_ARRAY_ELEMENTS);

	/// <summary>
	/// Requires a variable template directly above the call!
	/// </summary>
	public static SendProp SendPropArray2(ArrayLengthSendProxyFn proxyFn, int elementCount, ReadOnlySpan<char> arrayName) {
		return InternalSendPropArray(elementCount, arrayName, proxyFn);
	}
	public static SendProp SendPropArray3(ArrayFieldInfo field, SendProp arrayProp, SendTableProxyFn? proxyFn = null) {
		proxyFn ??= SendProxy_DataTableToDataTable;

		SendProp ret = new();
		int elements = field.Length;

		Assert(elements != -1);
		Assert(elements <= Constants.MAX_ARRAY_ELEMENTS);

		ret.FieldInfo = field;
		ret.Type = SendPropType.DataTable;
		ret.SetDataTableProxyFn(proxyFn);
		ret.SetArrayProp(arrayProp);

		if (proxyFn == SendProxy_DataTableToDataTable)
			ret.SetFlags(PropFlags.ProxyAlwaysYes);

		SendProp[] props = new SendProp[elements];

		for (int i = 0; i < elements; i++) {
			props[i] = arrayProp.Copy();
			props[i].FieldInfo = new ArrayFieldIndexInfo(field, i);
			props[i].NameOverride = ElementNames[i];
			props[i].SetParentArrayPropName(field.Name);
		}

		SendTable table = new SendTable(field.Name, props);
		ret.SetDataTable(table);

		return ret;
	}
	
	public static SendProp InternalSendPropArray(int elementCount, ReadOnlySpan<char> name, ArrayLengthSendProxyFn? arrayLengthFn = null) {
		SendProp ret = new();

		ret.Type = SendPropType.Array;
		ret.Elements = elementCount;
		ret.NameOverride = new(name);
		ret.SetProxyFn(SendProxy_Empty);
		ret.ArrayProp = null;
		ret.SetArrayLengthProxy(arrayLengthFn);

		return ret;
	}

	public static SendProp SendPropFloat(FieldInfo field, int bits = 32, PropFlags flags = 0, float lowValue = 0, float highValue = Constants.HIGH_DEFAULT, SendVarProxyFn? proxyFn = null) {
		proxyFn ??= SendProxy_FloatToFloat;

		SendProp ret = new();

		if (bits <= 0 || bits == 32) {
			flags |= PropFlags.NoScale;
			lowValue = 0f;
			highValue = 0f;
		}
		else {
			if (highValue == Constants.HIGH_DEFAULT)
				highValue = (1 << bits);

			if ((flags & PropFlags.RoundDown) != 0)
				highValue = highValue - ((highValue - lowValue) / (1 << bits));
			else if ((flags & PropFlags.RoundUp) != 0)
				lowValue = lowValue + ((highValue - lowValue) / (1 << bits));
		}

		ret.Type = SendPropType.Float;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);
		ret.LowValue = lowValue;
		ret.HighValue = highValue;
		ret.HighLowMul = AssignRangeMultiplier(ret.Bits, ret.HighValue - ret.LowValue);
		ret.SetProxyFn(proxyFn);
		if ((ret.GetFlags() & (PropFlags.Coord | PropFlags.NoScale | PropFlags.Normal | PropFlags.CoordMP | PropFlags.CoordMPLowPrecision | PropFlags.CoordMPIntegral)) != 0)
			ret.Bits = 0;

		return ret;
	}
	public static SendProp SendPropVector(FieldInfo field, int bits = 32, PropFlags flags = 0, float lowValue = 0, float highValue = Constants.HIGH_DEFAULT, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_VectorToVector;

		if (bits == 32)
			flags |= PropFlags.NoScale;

		ret.Type = SendPropType.Vector;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);
		ret.LowValue = lowValue;
		ret.HighValue = highValue;
		ret.HighLowMul = AssignRangeMultiplier(ret.Bits, ret.HighValue - ret.LowValue);
		ret.SetProxyFn(proxyFn);
		if ((ret.GetFlags() & (PropFlags.Coord | PropFlags.NoScale | PropFlags.Normal | PropFlags.CoordMP | PropFlags.CoordMPLowPrecision | PropFlags.CoordMPIntegral)) != 0)
			ret.Bits = 0;

		return ret;
	}
	public static SendProp SendPropVectorXY(FieldInfo field, int bits = 32, PropFlags flags = 0, float lowValue = 0, float highValue = Constants.HIGH_DEFAULT, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_VectorXYToVectorXY;

		if (bits == 32)
			flags |= PropFlags.NoScale;

		ret.Type = SendPropType.VectorXY;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);
		ret.LowValue = lowValue;
		ret.HighValue = highValue;
		ret.HighLowMul = AssignRangeMultiplier(ret.Bits, ret.HighValue - ret.LowValue);
		ret.SetProxyFn(proxyFn);
		if ((ret.GetFlags() & (PropFlags.Coord | PropFlags.NoScale | PropFlags.Normal | PropFlags.CoordMP | PropFlags.CoordMPLowPrecision | PropFlags.CoordMPIntegral)) != 0)
			ret.Bits = 0;

		return ret;
	}
	public static SendProp SendPropAngle(FieldInfo field, int bits = 32, PropFlags flags = 0, float lowValue = 0, float highValue = Constants.HIGH_DEFAULT, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_AngleToFloat;

		if (bits == 32)
			flags |= PropFlags.NoScale;

		ret.Type = SendPropType.Float;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);
		ret.LowValue = 0.0f;
		ret.HighValue = 360.0f;
		ret.HighLowMul = AssignRangeMultiplier(ret.Bits, ret.HighValue - ret.LowValue);
		ret.SetProxyFn(proxyFn);

		return ret;
	}
	public static SendProp SendPropQAngles(FieldInfo field, int bits = 32, PropFlags flags = 0, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_QAngles;

		if (bits == 32)
			flags |= PropFlags.NoScale;

		ret.Type = SendPropType.Vector;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);
		ret.LowValue = 0.0f;
		ret.HighValue = 360.0f;
		ret.HighLowMul = AssignRangeMultiplier(ret.Bits, ret.HighValue - ret.LowValue);
		ret.SetProxyFn(proxyFn);

		return ret;
	}

	public static SendProp SendPropInt(FieldInfo field, int bits = -1, PropFlags flags = 0, SendVarProxyFn? proxyFn = null, int sizeOfVar = -1)
		=> SendPropInt(null, field, bits, flags, proxyFn, sizeOfVar);
	public static SendProp SendPropInt(string name, int bits = -1, PropFlags flags = 0, SendVarProxyFn? proxyFn = null, int sizeOfVar = -1)
		=> SendPropInt(name, null, bits, flags, proxyFn, sizeOfVar);

	public static SendProp SendPropInt(string? nameOverride, FieldInfo? field, int bits = -1, PropFlags flags = 0, SendVarProxyFn? proxyFn = null, int sizeOfVar = -1) {
		SendProp ret = new();
		sizeOfVar = sizeOfVar == -1 
						? field == null 
							? -1 : DataTableHelpers.FieldSizes.TryGetValue(field.FieldType, out int v) 
							? v : -1 
						: sizeOfVar;
		if (proxyFn == null) {
			if (sizeOfVar == 1)
				proxyFn = SendProxy_Int8ToInt32;
			else if (sizeOfVar == 2)
				proxyFn = SendProxy_Int16ToInt32;
			else if (sizeOfVar == 4)
				proxyFn = SendProxy_Int32ToInt32;
			else {
				AssertMsg(false, $"SendPropInt var has invalid size ({(sizeOfVar == -1 ? "UNDEFINED" : sizeOfVar)})");
				proxyFn = SendProxy_Int8ToInt32;
			}
		}

		if (bits <= 0) {
			Assert(sizeOfVar == 1 || sizeOfVar == 2 || sizeOfVar == 4);
			bits = sizeOfVar * 8;
		}

		ret.Type = SendPropType.Int;
		ret.NameOverride = nameOverride;
		ret.FieldInfo = field;
		ret.Bits = bits;
		ret.SetFlags(flags);

		ret.SetProxyFn(proxyFn);
		if ((ret.GetFlags() & PropFlags.Unsigned) != 0) {
			if (proxyFn == SendProxy_Int8ToInt32)
				ret.SetProxyFn(SendProxy_UInt8ToInt32);
			else if (proxyFn == SendProxy_Int16ToInt32)
				ret.SetProxyFn(SendProxy_UInt16ToInt32);
			else if (proxyFn == SendProxy_Int32ToInt32)
				ret.SetProxyFn(SendProxy_UInt32ToInt32);
		}

		return ret;
	}
	public static SendProp SendPropModelIndex(FieldInfo field)
		=> SendPropInt(field, Constants.SP_MODEL_INDEX_BITS);
	public static SendProp SendPropString(FieldInfo field, int bufferSize = -1, PropFlags flags = 0, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		if (bufferSize == -1) {
			// Try to see if this is inline. If it is then we can set the explicit size ourselves
			var inlineArrayAttr = field.FieldType.GetCustomAttribute<InlineArrayAttribute>();
			if (inlineArrayAttr != null)
				bufferSize = inlineArrayAttr.Length;
		}
		ret.Type = SendPropType.String;
		ret.FieldInfo = field;
		ret.SetFlags(flags);
		ret.SetProxyFn(proxyFn ?? SendProxy_StringToString);

		return ret;
	}
	public static SendProp SendPropStringT(FieldInfo field, PropFlags flags = 0, SendVarProxyFn? proxyFn = null) {
		SendProp ret = new();
		ret.Type = SendPropType.String;
		ret.FieldInfo = field;
		ret.SetFlags(flags);
		ret.SetProxyFn(proxyFn ?? SendProxy_StringToString);
		return ret;
	}
	public static SendProp SendPropDataTable(string name, SendTable sendTable, SendTableProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_DataTableToDataTable;

		ret.Type = SendPropType.DataTable;
		ret.NameOverride = name;
		ret.SetDataTable(sendTable);
		ret.SetDataTableProxyFn(proxyFn);

		if (proxyFn == SendProxy_DataTableToDataTable)
			ret.SetFlags(PropFlags.ProxyAlwaysYes);

		if(proxyFn == SendProxy_DataTableToDataTable && name == "baseclass") {
			ret.SetFlags(PropFlags.Collapsible);
		}

		return ret;
	}
	delegate void EnsureCapacityBasicFn(int length);
	public static SendProp SendPropList(FieldInfo field, int maxElements, SendProp arrayProp, SendTableProxyFn? proxyFn = null) {
		proxyFn ??= SendProxy_DataTableToDataTable;

		SendProp ret = new();
		ret.Type = SendPropType.DataTable;
		ret.FieldInfo = field;
		ret.SetDataTableProxyFn(proxyFn);

		if (proxyFn == SendProxy_DataTableToDataTable)
			ret.SetFlags(PropFlags.ProxyAlwaysYes);

		// Hack to get this to work. This is also rather slow. I'm just lazy and need it to work
		MethodInfo ensureCapacity = field.FieldType.GetMethod("EnsureCapacity")!;
		SendPropExtra_UtlVector extraData = new() {
			MaxElements = maxElements,
			EnsureCapacityFn = (instance, list, size) => {
				ensureCapacity.CreateDelegate<EnsureCapacityBasicFn>(list)(size);
			}
		};

		if (arrayProp.Type == SendPropType.DataTable)
			extraData.DataTableProxyFn = arrayProp.GetDataTableProxyFn();
		else
			extraData.ProxyFn = arrayProp.GetProxyFn();

		SendProp[] props = new SendProp[maxElements + 1]; 
		SendProp lengthProp = SendPropInt($"lengthprop{maxElements}", DtCommon.NumBitsForCount(maxElements), PropFlags.Unsigned, SendProxy_UtlVectorLength);
		lengthProp.SetExtraData(extraData);

		string lengthProxyTableName = DtUtlVectorCommon.AllocateUniqueDataTableName(true, $"_LPT_{field.Name}_{maxElements}");
		SendTable lengthTable = new SendTable(lengthProxyTableName, [lengthProp]);
		props[0] = SendPropDataTable("lengthproxy", lengthTable, SendProxy_LengthTable);
		props[0].SetExtraData(extraData);

		for (int i = 1; i < maxElements + 1; i++) {
			props[i] = arrayProp;
			props[i].SetOffset(0); 
			props[i].NameOverride = ElementNames[i - 1]; 
			props[i].ParentArrayPropName = field.Name;
			props[i].SetExtraData(extraData);

			if (arrayProp.Type == SendPropType.DataTable) {
				props[i].SetDataTableProxyFn(SendProxy_UtlVectorElement_DataTable);
				props[i].SetFlags(PropFlags.ProxyAlwaysYes);
			}
			else {
				props[i].SetProxyFn(SendProxy_UtlVectorElement);
			}
		}

		SendTable table = new SendTable(DtUtlVectorCommon.AllocateUniqueDataTableName(true, $"_ST_{field.Name}_{maxElements}"), props);
		ret.SetDataTable(table);
		return ret;
	}

	private static void SendProxy_UtlVectorLength(SendProp prop, object instance, FieldInfo field, ref DVariant outData, int element, int objectID) {
		throw new NotImplementedException();
	}

	private static object? SendProxy_UtlVectorElement_DataTable(SendProp prop, object instance, FieldInfo data, SendProxyRecipients recipients, int objectID) {
		throw new NotImplementedException();
	}

	private static void SendProxy_UtlVectorElement(SendProp prop, object instance, FieldInfo field, ref DVariant outData, int element, int objectID) {
		throw new NotImplementedException();
	}

	private static object? SendProxy_LengthTable(SendProp prop, object instance, FieldInfo data, SendProxyRecipients recipients, int objectID) {
		throw new NotImplementedException();
	}

	public static SendProp SendPropDataTable(string name, FieldInfo field, SendTable sendTable, SendTableProxyFn? proxyFn = null) {
		SendProp ret = new();
		proxyFn ??= SendProxy_DataTableToDataTable;

		ret.Type = SendPropType.DataTable;
		ret.NameOverride = name;
		ret.FieldInfo = field;
		ret.SetDataTable(sendTable);
		ret.SetDataTableProxyFn(proxyFn);

		if (proxyFn == SendProxy_DataTableToDataTable)
			ret.SetFlags(PropFlags.ProxyAlwaysYes);

		// TODO: Collapsible...

		return ret;
	}

	public static SendProp SendPropGModTable(FieldInfo field) {
		SendProp ret = new();

		ret.Type = SendPropType.GModTable;
		ret.FieldInfo = field;

		return ret;
	}
}

[DebuggerDisplay("SendProp<{Type}> {NameOverride ?? FieldInfo.Name} [{Flags,ac}]")]
public class SendProp : IDataTableProp
{
	public RecvProp? MatchingRecvProp;
	public SendPropType Type;
	public int Bits;
	public float LowValue;
	public float HighValue;
	public SendProp? ArrayProp;
	public int Elements = 1;
	public int Offset;
	public string? ExcludeDTName;
	public string? ParentArrayPropName;
	public FieldInfo FieldInfo;
	public float HighLowMul;
	public object? ExtraData;

	public int GetOffset() => Offset;
	public void SetOffset(int value) => Offset = value;

	public SendProp() {

	}
	public SendProp(FieldInfo field, SendPropType type, int bits = 32, PropFlags flags = 0, float lowValue = 0f, float highValue = -121121.125f) {
		Type = type;
		FieldInfo = field;
		Bits = bits;
		Flags = flags;
		LowValue = lowValue;
		HighValue = highValue;
	}
	public SendProp(string? name, FieldInfo field, SendPropType type, int bits = 32, PropFlags flags = 0, float lowValue = 0f, float highValue = -121121.125f) {
		NameOverride = name;
		Type = type;
		FieldInfo = field;
		Bits = bits;
		Flags = flags;
		LowValue = lowValue;
		HighValue = highValue;
	}

	public string? NameOverride;
	PropFlags Flags;
	SendTable? DataTable;

	public T GetValue<T>(object instance) => FieldAccess<T>.Getter(FieldInfo)(instance);
	public void SetValue<T>(object instance, in T value) => FieldAccess<T>.Setter(FieldInfo)(instance, value);

	public SendProp? GetArrayProp() {
		return ArrayProp;
	}

	public PropType? GetArrayProp<PropType>() where PropType : IDataTableProp {
		return (PropType?)(object?)ArrayProp;
	}

	public SendTable? GetDataTable() => DataTable;

	IDataTableBase<PropType>? IDataTableProp.GetDataTable<PropType>() {
		return (IDataTableBase<PropType>?)DataTable;
	}

	public PropFlags GetFlags() => Flags;

	public ReadOnlySpan<char> GetName() => NameOverride ?? FieldInfo.Name;

	public int GetNumElements() => Elements;

	public SendPropType GetPropType() => Type;

	public bool IsExcludeProp() => (Flags & PropFlags.Exclude) != 0;
	public bool IsInsideArray() => (Flags & PropFlags.InsideArray) != 0;
	public bool IsSigned() => (Flags & PropFlags.Unsigned) == 0;
	public bool SetArrayProp<PropType>(PropType propType) where PropType : IDataTableProp {
		return (ArrayProp = propType is SendProp sp ? sp : null) != null;
	}

	public void SetFlags(PropFlags flags) => Flags = flags;
	public void SetInsideArray() => Flags |= PropFlags.InsideArray;
	public int SetNumElements(int elements) => Elements = elements;
	public ReadOnlySpan<char> GetExcludeDTName() => ExcludeDTName;

	public void SetDataTable<PropType>(IDataTableBase<PropType>? dt) where PropType : IDataTableProp
		=> DataTable = dt is SendTable st ? st : throw new InvalidCastException();

	public ReadOnlySpan<char> GetParentArrayPropName() {
		return ParentArrayPropName;
	}
	public void SetParentArrayPropName(ReadOnlySpan<char> str) {
		ParentArrayPropName = new(str);
	}

	SendVarProxyFn Fn;
	SendTableProxyFn DtFn;

	public SendVarProxyFn GetProxyFn() {
		return Fn;
	}

	public void SetProxyFn(SendVarProxyFn fn) {
		Fn = fn;
	}

	public void SetDataTableProxyFn(SendTableProxyFn value) {
		DtFn = value;
	}

	public SendTableProxyFn GetDataTableProxyFn() {
		return DtFn;
	}

	public SendProp Copy() => new() {
		MatchingRecvProp = MatchingRecvProp,
		Type = Type,
		Bits = Bits,
		LowValue = LowValue,
		HighValue = HighValue,
		ArrayProp = ArrayProp,
		Elements = Elements,
		Offset = Offset,
		ExcludeDTName = ExcludeDTName,
		ParentArrayPropName = ParentArrayPropName,
		FieldInfo = FieldInfo,
		HighLowMul = HighLowMul,
		NameOverride = NameOverride,
		Flags = Flags,
		DataTable = DataTable,
		Fn = Fn,
		DtFn = DtFn,
		arrayLengthProxyFn = arrayLengthProxyFn,
		ExtraData = ExtraData
	};

	ArrayLengthSendProxyFn? arrayLengthProxyFn;

	public ArrayLengthSendProxyFn? GetArrayLengthProxy() => arrayLengthProxyFn;
	public void SetArrayLengthProxy(ArrayLengthSendProxyFn? arrayLengthFn) {
		arrayLengthProxyFn = arrayLengthFn;
	}

	public int GetNumArrayLengthBits() => (int)Math.Floor(Math.Log2(GetNumElements()) + 1); // $todo am i sure?

	public object? GetExtraData() => ExtraData;
	public void SetExtraData(object? data) => ExtraData = data;
}

public class SendTable : IEnumerable<SendProp>, IDataTableBase<SendProp>
{
	public SendTable() { }
	public SendTable(SendProp[] props) {
		Props = props;
	}
	public SendTable(SendTable parent, SendProp[] props) {
		Props = new SendProp[props.Length + 1];
		Props[0] = SendPropDataTable("baseclass", parent, SendProxy_DataTableToDataTable);
		int i = 1;
		foreach (var prop in props)
			Props[i++] = prop;
	}
	public SendTable(string name, SendProp[] props) {
		NetTableName = name;
		Props = props;
	}

	public string? NetTableName;
	public SendTablePrecalc? Precalc;

	public SendProp[]? Props;

	protected bool Initialized;
	protected bool HasBeenWritten;
	protected bool HasPropsEncodedAgainstCurrentTickCount;

	public ReadOnlySpan<char> GetName() => NetTableName;

	public static void BuildHierarchy(SendNode node, SendTable? table, ref BuildHierarchyStruct bhs) {
		node.Table = table;
		node.FirstRecursiveProp = (ushort)bhs.NumProps;

		Assert(bhs.PropProxies < 255);
		byte curPropProxy = bhs.PropProxies;
		++bhs.PropProxies;

		SendProp[] nonDatatableProps = ArrayPool<SendProp>.Shared.Rent(Constants.MAX_TOTAL_SENDTABLE_PROPS);
		int numNonDatatableProps = 0;

		BuildHierarchy_IterateProps(node, table, ref bhs, nonDatatableProps, ref numNonDatatableProps);

		ErrorIfNot(bhs.NumProps + numNonDatatableProps < ((Span<SendProp>)bhs.Props).Length, "SendTable_BuildHierarchy: overflowed prop buffer.");

		for (int i = 0; i < numNonDatatableProps; i++) {
			bhs.Props[bhs.NumProps] = nonDatatableProps[i];
			bhs.PropProxyIndices[bhs.NumProps] = curPropProxy;
			++bhs.NumProps;
		}

		node.RecursiveProps = (ushort)(bhs.NumProps - node.FirstRecursiveProp);
		ArrayPool<SendProp>.Shared.Return(nonDatatableProps, true);
	}

	private static void BuildHierarchy_IterateProps(SendNode node, SendTable? table, ref BuildHierarchyStruct bhs, SendProp[] nonDatatableProps, ref int numNonDatatableProps) {
		int i;
		for (i = 0; i < (table?.Props?.Length ?? 0); i++) {
			SendProp prop = table.Props![i];

			if (prop.IsExcludeProp() || prop.IsInsideArray() || FindExcludeProp(table.GetName(), prop.GetName(), bhs.ExcludeProps!)) {
				continue;
			}

			if (prop.GetPropType() == SendPropType.DataTable) {
				if ((prop.GetFlags() & PropFlags.Collapsible) != 0) {
					BuildHierarchy_IterateProps(
						node,
						prop.GetDataTable(),
						ref bhs,
						nonDatatableProps,
						ref numNonDatatableProps);
				}
				else {
					SendNode child = new();

					if (bhs.NumDataTableProps >= ((Span<SendProp>)bhs.DataTableProps).Length)
						Error($"Overflowed datatable prop list in SendTable '{table.GetName()}'.");

					bhs.DataTableProps[bhs.NumDataTableProps] = prop;
					child.DataTableProp = (short)bhs.NumDataTableProps;
					++bhs.NumDataTableProps;

					node.Children.Add(child);

					BuildHierarchy(child, prop.GetDataTable(), ref bhs);
				}
			}
			else {
				if (numNonDatatableProps >= Constants.MAX_TOTAL_SENDTABLE_PROPS)
					Error($"SendTable_BuildHierarchy: overflowed non-datatable props with '{prop.GetName()}'.");

				nonDatatableProps[numNonDatatableProps++] = prop;
			}
		}
	}

	private static bool FindExcludeProp(ReadOnlySpan<char> tableName, ReadOnlySpan<char> propName, ExcludeProp[]? excludeProps) {
		for (int i = 0; i < excludeProps?.Length; i++) {
			if (tableName.Equals(excludeProps[i].TableName!, StringComparison.OrdinalIgnoreCase) && propName.Equals(excludeProps[i].PropName!, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}

	public static void SortByPriority(ref BuildHierarchyStruct bhs) {
		int i, start = 0;

		while (true) {
			for (i = start; i < bhs.NumProps; i++) {
				SendProp p = bhs.Props[i];
				byte c = bhs.PropProxyIndices[i];

				if ((p.GetFlags() & PropFlags.ChangesOften) != 0) {
					bhs.Props[i] = bhs.Props[start];
					bhs.PropProxyIndices[i] = bhs.PropProxyIndices[start];
					bhs.Props[start] = p;
					bhs.PropProxyIndices[start] = c;
					start++;
					break;
				}
			}

			if (i == bhs.NumProps)
				return;
		}
	}

	public bool IsInitialized() => Initialized;
	public void SetInitialized(bool initialized) => Initialized = initialized;
	public int GetNumProps() => Props == null ? 0 : Props.Length;

	public static bool GetPropsExcluded(SendTable table, Span<ExcludeProp> excludeProps, ref int numExcludeProps, int maxExcludeProps) {
		for (int i = 0; i < table.Props?.Length; i++) {
			SendProp prop = table.Props[i];

			if (prop.IsExcludeProp()) {
				ReadOnlySpan<char> pName = prop.GetExcludeDTName();

				ErrorIfNot(pName != null, "Found an exclude prop missing a name.");
				ErrorIfNot(numExcludeProps < maxExcludeProps, $"SendTable_GetPropsExcluded: Overflowed max exclude props with {pName}.");

				excludeProps[numExcludeProps].TableName = new(pName);
				excludeProps[numExcludeProps].PropName = new(prop.GetName());
				numExcludeProps++;
			}
			else if (prop.GetDataTable() != null) {
				if (!GetPropsExcluded(prop.GetDataTable()!, excludeProps, ref numExcludeProps, maxExcludeProps))
					return false;
			}
		}

		return true;
	}

	public SendProp GetProp(int index) {
		return Props![index];
	}

	public static void GenerateProxyPaths(SendTablePrecalc precalc, int proxyIndices) {
		precalc.ProxyPaths.SetSize(proxyIndices);

		Span<SendTablePrecalc.ProxyPath> precalcs = precalc.ProxyPaths.AsSpan();
		for (int i = 0; i < proxyIndices; i++)
			precalcs[i].FirstEntry = precalcs[i].Entries = 0xFFFF;

		int totalPathLengths = 0;
		List<int> pathLengths = [];
		pathLengths.SetSize(proxyIndices);
		CalcPathLengths_R(precalc.GetRootNode(), pathLengths, 0, ref totalPathLengths);

		int curEntry = 0;
		precalc.ProxyPathEntries.SetSize(totalPathLengths);
		FillPathEntries_R(precalc, precalc.GetRootNode(), null, ref curEntry);
	}

	private static void CalcPathLengths_R(SendNode node, List<int> pathLengths, int curPathLength, ref int totalPathLengths) {
		pathLengths[node.GetRecursiveProxyIndex()] = curPathLength;
		totalPathLengths += curPathLength;

		for (int i = 0; i < node.GetNumChildren(); i++) {
			CalcPathLengths_R(node.GetChild(i), pathLengths, curPathLength + 1, ref totalPathLengths);
		}
	}

	private static void FillPathEntries_R(SendTablePrecalc precalc, SendNode node, SendNode? parent, ref int curEntry) {
		ref SendTablePrecalc.ProxyPath outProxyPath = ref precalc.ProxyPaths.AsSpan()[node.GetRecursiveProxyIndex()];
		outProxyPath.FirstEntry = (ushort)curEntry;

		if (parent != null) {
			ref SendTablePrecalc.ProxyPath parentProxyPath = ref precalc.ProxyPaths.AsSpan()[parent.GetRecursiveProxyIndex()];
			outProxyPath.Entries = (ushort)(parentProxyPath.Entries + 1);

			for (int i = 0; i < parentProxyPath.Entries; i++)
				precalc.ProxyPathEntries[curEntry++] = precalc.ProxyPathEntries[parentProxyPath.FirstEntry + i];

			precalc.ProxyPathEntries.AsSpan()[curEntry].Proxy = node.GetRecursiveProxyIndex();
			precalc.ProxyPathEntries.AsSpan()[curEntry].DataTableProp = (ushort)node.DataTableProp;
			++curEntry;
		}
		else
			outProxyPath.Entries = 0;

		for (int i = 0; i < node.GetNumChildren(); i++)
			FillPathEntries_R(precalc, node.GetChild(i), node, ref curEntry);
	}

	public IEnumerator<SendProp> GetEnumerator() {
		return ((IEnumerable<SendProp>)Props).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() {
		return Props.GetEnumerator();
	}

	public void SetHasPropsEncodedAgainstTickcount(bool state) {
		HasPropsEncodedAgainstCurrentTickCount = state;
	}
}

public delegate void SendVarProxyFn(SendProp prop, object instance, FieldInfo field, ref DVariant outData, int element, int objectID);
public delegate object? SendTableProxyFn(SendProp prop, object instance, FieldInfo data, SendProxyRecipients recipients, int objectID);