using Source.Common.Bitbuffers;

using System.Buffers;
using System.Net;

namespace Source.Common.Networking;

using static Protocol;

public class SubChannel
{
	public const int MAX = 8;

	public int[] StartFragment = new int[NetChannel.MAX_STREAMS];
	public int[] NumFragments = new int[NetChannel.MAX_STREAMS];
	public int SendSeqNumber;
	public SubChannelState State;
	public int Index;

	public void Free() {
		State = SubChannelState.Free;
		SendSeqNumber = -1;
		for (int i = 0; i < NetChannel.MAX_STREAMS; i++) {
			StartFragment[i] = 0;
			NumFragments[i] = 0;
		}
	}
}

public class NetFrame
{
	/// <summary>
	/// Must be power of 2
	/// </summary>
	public const int BACKUP = 64;
	public const int MASK = BACKUP - 1;

	public TimeUnit_t Time;
	public int Size;
	public TimeUnit_t Latency;
	public TimeUnit_t AverageLatency;
	public bool IsValid;
	public int ChokedPackets;
	public int DroppedPackets;
	public TimeUnit_t InterpolationAmount;
	public ushort[] MessageGroups = new ushort[(uint)NetChannelGroup.Total];
}

public class NetFlow
{
	public const int MAX = 2;
	/// <summary>
	/// How fast to converge flow estimates
	/// </summary>
	public const double FLOW_AVG = 3d / 4d;
	/// <summary>
	/// Don't compute more often than this
	/// </summary>
	public const double FLOW_INTERVAL = .25d;

	public const int FLOW_OUTGOING = 0;
	public const int FLOW_INCOMING = 1;
	public const int MAX_FLOWS = 2;

	public TimeUnit_t NextCompute;
	public TimeUnit_t AverageBytesPerSec;
	public TimeUnit_t AveragePacketsPerSec;
	public TimeUnit_t AverageLoss;
	public TimeUnit_t AverageChoke;
	public TimeUnit_t AverageLatency;
	public TimeUnit_t Latency;
	public int TotalPackets;
	public int TotalBytes;
	public int CurrentIndex;
	public readonly NetFrame[] Frames = new NetFrame[NetFrame.BACKUP];
	public NetFrame? CurrentFrame;

	public NetFlow() {
		for (int i = 0; i < Frames.Length; i++)
			Frames[i] = new();
	}
}

public enum PacketFlag
{
	Invalid = -1,
	None = 0,
	Reliable = 1 << 0,     // 1 0 0 0 0 0 0 0
	Compressed = 1 << 1,   // 0 1 0 0 0 0 0 0
	Encrypted = 1 << 2,    // 0 0 1 0 0 0 0 0
	Split = 1 << 3,        // 0 0 0 1 0 0 0 0
	Choked = 1 << 4,       // 0 0 0 0 1 0 0 0
	Challenge = 1 << 5,    // 0 0 0 0 0 1 0 0
}
public class NetAddress
{
	public NetAddressType Type { get; set; }
	public IPEndPoint? Endpoint { get; private set; } = new IPEndPoint(IPAddress.Any, 0);

	public override string ToString() {
		if (Endpoint == null) return "<NULL NETADDR>";

		return $"<{Type} {Endpoint.ToString()}>";
	}

	public IPAddress IP {
		get => Endpoint?.Address ?? IPAddress.None;
		set => (Endpoint ?? throw new Exception()).Address = value;
	}

	public ushort Port {
		get => (ushort)(Endpoint?.Port ?? 0);
		set => (Endpoint ?? throw new Exception()).Port = value;
	}

	public NetAddress() {

	}

	public NetAddress(string hostname) {
		Endpoint = IPEndPoint.Parse(hostname);
	}

	public NetAddress(string hostname, ushort port) {
		Endpoint = IPEndPoint.Parse($"{hostname}:{port}");
	}

	public void SetFromEndPoint(EndPoint ep) {
		Endpoint = (IPEndPoint)ep; // please work please work please work please work please work please work please work please work please work please work please work 
	}

	public static implicit operator IPEndPoint(NetAddress addr) => addr.Endpoint ?? throw new Exception("Unset endpoint");

	public bool CompareAddress(NetAddress? other) => other != null && IP.Equals(other.IP) && Port == other.Port;

	public void Clear() {
		Endpoint = null;
		Type = NetAddressType.Null;
	}
}

public struct SPLITPACKET
{
	public int NetID;
	public int SequenceNumber;
	public short PacketID;
	public short SplitSize;
}

public class LONGPACKET
{
	public int CurrentSequence;
	public int SplitCount;
	public int TotalSize;
	public int ExpectedSplitSize;
	public byte[] Buffer;
	public bool RentedBuffer;
	public LONGPACKET() {
		Buffer = ArrayPool<byte>.Shared.Rent(MAX_MESSAGE);
		RentedBuffer = true;
	}
	~LONGPACKET() {
		if (RentedBuffer) {
			ArrayPool<byte>.Shared.Return(Buffer, true);
		}
	}
}

public class SplitPacketEntry
{
	public NetAddress? From;
	public readonly int[] SplitFlags = new int[MAX_SPLITPACKET_SPLITS];
	public LONGPACKET NetSplit = new();
	public double LastActiveTime;
}

public unsafe class NetScratchBuffer
{
	public readonly byte[] Data = ArrayPool<byte>.Shared.Rent(MAX_MESSAGE);
	public int Length => Data.Length;
}

public abstract class NetMessage : INetMessage
{
	private int type;
	protected bool reliable;
	private string typename;
	private NetChannel? netchan;

	public NetMessage(int type) {
		typename = GetType().Name;
		this.type = type;
		reliable = true;
	}

	// Implement interface

	public NetChannel? GetNetChannel() => netchan;
	public void SetNetChannel(NetChannel? netchan) => this.netchan = netchan;

	public bool IsReliable => reliable;
	public void SetReliable(bool state) => reliable = state;

	public int GetMessageType() => type;
	public string GetName() => typename;

	public virtual bool Process() {
		return netchan?.MessageHandler?.ProcessMessage(this) ?? false;
	}

	public virtual bool ReadFromBuffer(bf_read buffer) => false;
	public virtual bool WriteToBuffer(bf_write buffer) => false;

	public virtual NetChannelGroup GetGroup() => NetChannelGroup.Generic;

	public override string ToString() {
		return $"{GetType().Name} <no ToString() avail>";
	}
}

public unsafe class NetPacket
{
	public NetAddress From;
	public byte[]? Data;
	public int Size;
	public double Received;
	public NetSocketType Source;
	public bf_read Message;
	public int WireSize;
	public bool Stream;

	public NetPacket() {
		From = new();
		Size = 0;
		Received = 0;
		Source = NetSocketType.NotApplicable;
		Message = new();
		WireSize = 0;
		Stream = false;
	}
}

public interface IConnectionlessPacketHandler
{
	public bool ProcessConnectionlessPacket(ref NetPacket packet);
}
