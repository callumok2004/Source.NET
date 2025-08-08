﻿using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;

using static Source.Common.Networking.Protocol;
using static Source.Dbg;
using Source.Common.Bitbuffers;
using Source.Common.Hashing;
using Source.Common.Commands;

namespace Source.Common.Networking;
public class NetChannel : INetChannelInfo, INetChannel
{
	public readonly Net Net;
	public NetChannel(Net Net) {
		this.Net = Net;

		SplitPacketSequence = 1;
		MaxRoutablePayloadSize = MAX_ROUTABLE_PAYLOAD;
		ProcessingMessages = false;
		ShouldDelete = false;
		ClearedDuringProcessing = false;
		streamContainsChallenge = false;
		Socket = NetSocketType.NotApplicable;
		RemoteAddress?.Clear();
		LastReceived = 0;
		ConnectTime = 0;
		ProtocolVersion = -1;

		StreamUnreliable.DebugName = "NetChan/UnreliableData";
		StreamReliable.DebugName = "NetChan/ReliableData";

		Rate = DEFAULT_RATE;
		Timeout = SIGNON_TIME_OUT;

		OutSequence = 1;
		InSequence = 0;
		OutSequenceAck = 0;
		OutReliableState = 0;
		InReliableState = 0;

		ChallengeNumber = 0;

		// Set up ReceiveList
		ReceiveList[FRAG_NORMAL_STREAM] = new();
		ReceiveList[FRAG_FILE_STREAM] = new();

		StreamSocket = null;
		StreamActive = false;

		ResetStreaming();
	}

	public const int DEFAULT_RATE = 80_000;
	public const float SIGNON_TIME_OUT = 300.0f;
	/// <summary>
	/// Socket type
	/// </summary>
	public NetSocketType Socket { get; set; }
	public Socket? StreamSocket { get; set; }

	public string Name { get; set; } = "";
	public INetChannelHandler MessageHandler { get; set; }
	public int ProtocolVersion { get; set; }

	public bf_write StreamReliable = new();
	public byte[] ReliableDataBuffer;

	public bf_write StreamUnreliable = new();
	public byte[] UnreliableDataBuffer;

	public bf_write StreamVoice = new();
	public byte[] VoiceDataBuffer;

	/// <summary>
	/// Address this netchannel is talking to
	/// </summary>
	public NetAddress? RemoteAddress { get; set; }

	public bool IsNull => RemoteAddress?.Type == NetAddressType.Null;


	public const int FRAG_NORMAL_STREAM = 0;
	public const int FRAG_FILE_STREAM = 1;
	public const int MAX_STREAMS = 2;

	public const int CONNECTION_PROBLEM_TIME = 4;

	public const int NET_FRAMES_BACKUP = 64;
	public const int NET_FRAMES_MASK = NET_FRAMES_BACKUP - 1;



	public void Clear() {
		int i;

		for (i = 0; i < MAX_STREAMS; i++) {
			while (WaitingList[i].Count > 0)
				WaitingList[i].RemoveAt(WaitingList[i].Count - 1);
		}

		for (i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.ToSend) {
				int bit = 1 << i;
				OutReliableState = FLIPBIT(OutReliableState, bit);

				SubChannels[i].Free();
			}
			else if (SubChannels[i].State == SubChannelState.Waiting) {
				SubChannels[i].State = SubChannelState.Dirty;
			}
		}

		if (ProcessingMessages)
			ClearedDuringProcessing = true;

		Reset();
	}

	public double LastReceived;
	public double ConnectTime;

	public int Rate { get; set; }
	public float Timeout { get; set; }

	public int OutSequence { get; private set; }
	public int InSequence { get; private set; }
	public int OutSequenceAck { get; private set; }
	public int OutReliableState { get; private set; }
	public int InReliableState { get; private set; }
	public int ChokedPackets { get; private set; }
	public double ClearTime { get; private set; }

	public uint ChallengeNumber { get; set; }

	public List<DataFragments>[] WaitingList = [new(), new()];
	public DataFragments[] ReceiveList = new DataFragments[MAX_STREAMS];
	public SubChannel[] SubChannels = new SubChannel[SubChannel.MAX];

	public void Setup(NetSocketType socketType, NetAddress address, string name, INetChannelHandler handler, int protocol) {
		Debug.Assert(name != null);
		Debug.Assert(handler != null);

		Socket = socketType;
		if (StreamSocket != null) {
			Net.CloseSocket(StreamSocket);
			StreamSocket = null;
		}

		if (address != null) {
			RemoteAddress = address;
		}
		else if (RemoteAddress != null) {
			RemoteAddress.Type = NetAddressType.Null;
		}

		LastReceived = Net.Time;
		ConnectTime = Net.Time;

		Name = name;
		MessageHandler = handler;
		ProtocolVersion = protocol;

		// Set up the unreliable buffer
		SetMaxBufferSize(
			reliable: false,
			bytes: MAX_DATAGRAM_PAYLOAD,
			voice: false);

		// Set up the voice buffer
		SetMaxBufferSize(
			reliable: false,
			bytes: MAX_DATAGRAM_PAYLOAD,
			voice: true);

		// Set up the reliable buffer
		SetMaxBufferSize(
			reliable: true,
			bytes: MAX_PAYLOAD,
			voice: false);

		Rate = DEFAULT_RATE;
		Timeout = SIGNON_TIME_OUT;

		OutSequenceAck = 1; // otherwise it looks like a connectionless header
		InSequence = 0;
		OutSequenceAck = 0;
		OutReliableState = 0;
		InReliableState = 0;

		ChallengeNumber = 0;

		StreamSocket = null;
		StreamActive = false;

		for (int i = 0; i < SubChannel.MAX; i++) {
			SubChannels[i] = new SubChannel();
			SubChannels[i].Index = i;
			SubChannels[i].Free();
		}

		ResetStreaming();

		MaxReliablePayloadSize = MAX_PAYLOAD;

		FileRequestCounter = 0;
		FileBackgroundTransmission = true;
		UseCompression = false;
		QueuedPackets = 0;

		RemoteFrameTime = 0;
		RemoteFrameTimeStdDeviation = 0;

		ResetFlow();

		MessageHandler.ConnectionStart(this);
	}

	public int MaxReliablePayloadSize { get; set; }
	public int MaxRoutablePayloadSize { get; set; }
	public uint FileRequestCounter { get; set; }
	public bool FileBackgroundTransmission { get; set; }
	public bool UseCompression { get; set; }
	public int QueuedPackets { get; set; }

	public float RemoteFrameTime { get; set; }
	public float RemoteFrameTimeStdDeviation { get; set; }

	// Packet history

	NetFlow[] DataFlow = new NetFlow[NetFlow.MAX];
	int[] MessageStats = new int[(int)NetChannelGroup.Total];

	// TCP stream state
	public bool StreamActive;
	public StreamCmd StreamType;
	public int StreamSeqNumber;
	public int StreamLength;
	public int StreamReceived;
	public string? StreamFile;
	List<byte> StreamData = [];


	public void ResetStreaming() {
		StreamType = StreamCmd.None;
		StreamLength = 0;
		StreamReceived = 0;
		StreamSeqNumber = 0;
		StreamFile = null;
	}

	public bool StartStreaming(uint challenge) {
		// Reset streaming
		ResetStreaming();

		ChallengeNumber = challenge;

		// Going to pretend like listen servers don't exist because for now they don't!
		if (Net.IsMultiplayer()) {
			StreamSocket = null;
			return true;
		}

		//StreamSocket = Net.ConnectSocket(Socket, RemoteAddress);
		StreamData.EnsureCapacity(MAX_PAYLOAD);

		return StreamSocket != null;
	}

	public void ResetFlow() {
		for (int i = 0; i < DataFlow.Length; i++)
			DataFlow[i] = new();

		for (int i = 0; i < MessageStats.Length; i++)
			MessageStats[i] = 0;
	}

	public void GetSequenceData(out int outSequence, out int inSequence, out int outSequenceAcknowledged) {
		outSequence = OutSequence;
		inSequence = InSequence;
		outSequenceAcknowledged = OutSequenceAck;
	}

	public void SetSequenceData(int outSequence, int inSequence, int outSequenceAcknowledged) {
		OutSequence = outSequence;
		InSequence = inSequence;
		OutSequenceAck = outSequenceAcknowledged;
	}

	public bool CanSendPacket() => ClearTime < Net.Time;

	public void SetChoked() {
		OutSequence++;
		ChokedPackets++;
	}

	public void Shutdown(string? reason) {
		if (Socket < 0)
			return;

		Clear();

		if (reason != null) {
			StreamUnreliable.WriteUBitLong(Net.Disconnect, NETMSG_TYPE_BITS);
			StreamUnreliable.WriteString(reason);
			Transmit();
		}

		if (StreamSocket != null) {
			Net.CloseSocket(StreamSocket);
			StreamSocket = null;
			StreamActive = false;
		}

		Socket = NetSocketType.NotApplicable;

		RemoteAddress?.Clear();

		if (MessageHandler != null) {
			MessageHandler.ConnectionClosing(reason);
			MessageHandler = null;
		}

		// GC can pick these up idrc
		NetMessages.Clear();

		if (ProcessingMessages) {
			Net.RemoveNetChannel(this, false);
			ShouldDelete = true;
		}
		else {
			Net.RemoveNetChannel(this, true);
		}
	}

	~NetChannel() {
		Shutdown("NetChannel removed.");
	}

	public void SetMaxBufferSize(bool reliable, int bytes, bool voice = false) {
		bytes = Math.Clamp(bytes, MAX_DATAGRAM_PAYLOAD, MAX_PAYLOAD);

		if (reliable)
			DoBufferThings(ref StreamReliable, ref ReliableDataBuffer, out ReliableDataBuffer, bytes);
		else if (voice)
			DoBufferThings(ref StreamVoice, ref VoiceDataBuffer, out VoiceDataBuffer, bytes);
		else
			DoBufferThings(ref StreamUnreliable, ref UnreliableDataBuffer, out UnreliableDataBuffer, bytes);
	}

	static int Bits2Bytes(int b) {
		return b + 7 >> 3;
	}

	static int PAD_NUMBER(int number, int boundary) => (number + (boundary - 1)) / boundary * boundary;
	static int BYTES2FRAGMENTS(int i) => (i + FRAGMENT_SIZE - 1) / FRAGMENT_SIZE;


	public int IncrementSplitPacketSequence() {
		return ++SplitPacketSequence;
	}

	private unsafe void DoBufferThings(ref bf_write stream, ref byte[]? bufferIn, out byte[] bufferOut, int bytes) {
		bufferOut = bufferIn;

		if (bufferIn != null && bufferIn.Length == bytes)
			return;

		byte[] copybuf = new byte[MAX_DATAGRAM_PAYLOAD];
		int copybits = stream.BitsWritten;
		int copybytes = Bits2Bytes(copybits);

		if (copybytes >= bytes) {
			Warning($"NetChannel.SetMaxBufferSize: can't preserve existing data, because {copybytes} >= {bytes}.\n");
			return;
		}

		if (copybits > 0 && stream.BaseArray != null) {
			Array.Copy(stream.BaseArray, copybuf, copybytes);
		}

		var newBuffer = new byte[bytes];

		if (bufferIn != null && bufferIn.Length > 0) {
			fixed (byte* dstPtr = newBuffer)
			fixed (byte* srcPtr = copybuf) {
				for (int i = 0; i < copybytes; i++) {
					dstPtr[i] = srcPtr[i];
				}
			}
		}
		else {

		}

		bufferOut = newBuffer;

		stream.StartWriting(bufferOut, bytes, copybits);
	}

	public bool ShouldChecksumPackets() {
		return Net.IsMultiplayer();
	}

	public static int FLIPBIT(int v, int b) => (v & b) > 0 ? v & ~b : v |= b;

	private static unsafe uint CRC32_ProcessSingleBuffer(void* data, nint length) {
		return CRC32.ProcessSingleBuffer((byte*)data, (int)length);
	}

	public static unsafe ushort BufferToShortChecksum(void* data, nint length) {
		uint crc = CRC32_ProcessSingleBuffer(data, length);


		ushort lowpart = (ushort)(crc & 0xFFFF);
		ushort highpart = (ushort)(crc >> 16 & 0xFFFF);

		return (ushort)(lowpart ^ highpart);
	}

	private bool streamContainsChallenge = false;
	private int packetDrop = 0;

	public unsafe PacketFlag ProcessPacketHeader(NetPacket packet) {
		int sequence = packet.Message.ReadLong();
		int sequenceAck = packet.Message.ReadLong();

		PacketFlag flags = (PacketFlag)packet.Message.ReadByte();

		//Debug.Assert(!flags.HasFlag(PacketFlag.Compressed));

		if (ShouldChecksumPackets()) {
			ushort checksum = (ushort)packet.Message.ReadUBitLong(16);
			Debug.Assert(packet.Message.BitsRead % 8 == 0);

			int offset = packet.Message.BitsRead >> 3;
			int checksumBytes = packet.Message.BytesAvailable - offset;

			fixed (byte* ptr = packet.Message.BaseArray) {
				ushort dataChecksum = BufferToShortChecksum(ptr + offset, checksumBytes);

				if (dataChecksum != checksum) {
					Warning($"{RemoteAddress}:corrupted packet {sequence} at {InSequence}\n");
					Debug.Assert(false);
					return PacketFlag.Invalid;
				}
			}
		}

		int relState = packet.Message.ReadByte();
		int choked = 0;
		int i, j;

		if (flags.HasFlag(PacketFlag.Choked))
			choked = packet.Message.ReadByte();

		if (flags.HasFlag(PacketFlag.Challenge)) {
			uint challenge = (uint)packet.Message.ReadLong();
			if (challenge != ChallengeNumber)
				return PacketFlag.Invalid;
			streamContainsChallenge = true;
		}
		else if (streamContainsChallenge)
			return PacketFlag.Invalid;

		// Stale/duplicated packets
		if (sequence <= InSequence) {
			if (sequence == InSequence)
				Warning($"{RemoteAddress}: duplicate packet {sequence} at {InSequence}\n");
			else
				Warning($"{RemoteAddress}: out-of-order packet {sequence} at {InSequence}\n");

			return PacketFlag.Invalid;
		}

		packetDrop = sequence - (InSequence + choked + 1);
		if (packetDrop > 0)
			Warning($"{RemoteAddress}: dropped {packetDrop} packets at {sequence}\n");

		// todo: net_maxpacketdrop

		for (i = 0; i < SubChannel.MAX; i++) {
			int bitmask = 1 << i;
			SubChannel subchan = SubChannels[i];
			Debug.Assert(subchan.Index == i);

			if ((OutReliableState & bitmask) == (relState & bitmask)) {
				if (subchan.State == SubChannelState.Dirty)
					subchan.Free();
				else if (subchan.SendSeqNumber > sequenceAck) {
					Warning($"{RemoteAddress}: reliable state invalid ({i}).\n");
					Debug.Assert(false);
				}
				else if (subchan.State == SubChannelState.Waiting) {
					for (j = 0; j < MAX_STREAMS; j++) {
						if (subchan.NumFragments[j] == 0)
							continue;

						Debug.Assert(WaitingList[j].Count > 0);

						DataFragments data = WaitingList[j][0];

						data.AckedFragments += subchan.NumFragments[j];
						data.PendingFragments -= subchan.NumFragments[j];
					}

					subchan.Free();
				}
			}
			else {
				if (subchan.SendSeqNumber <= sequenceAck) {
					Debug.Assert(subchan.State != SubChannelState.Free);

					if (subchan.State == SubChannelState.Waiting) {
						if (Net.net_showfragments.GetBool())
							Msg($"Resending subchan {subchan.Index}: start {subchan.StartFragment[0]}, num {subchan.NumFragments[0]}");

						subchan.State = SubChannelState.ToSend;
					}
					else if (subchan.State == SubChannelState.Dirty) {
						int bit = 1 << subchan.Index;
						OutReliableState = FLIPBIT(OutReliableState, bit);
						subchan.Free();
					}
				}
			}
		}

		InSequence = sequence;
		OutSequenceAck = sequenceAck;

		for (i = 0; i < MAX_STREAMS; i++)
			CheckWaitingList(i);

		FlowNewPacket(NetFlow.FLOW_INCOMING, InSequence, OutSequenceAck, choked, packetDrop, packet.WireSize + UDP_HEADER_SIZE);

		return flags;
	}

	public SubChannel? GetFreeSubchannel() {
		for (int i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.Free)
				return SubChannels[i];
		}

		return null;
	}

	public static unsafe void WritePacketToConsole(byte* ptr, int length) {
		int x = 0;
		Console.WriteLine("Packet at " + new nint(ptr) + " (length " + length + ")");
		for (int i = 0; i < length; i++) {
			if (x == 0)
				Console.Write($"{i:X}".PadLeft(4, '0') + "    ");

			Console.Write($"{ptr[i]:X}".PadLeft(2, '0') + " ");

			x++;
			if (x == 8)
				Console.Write("  ");

			if (x >= 16) {
				Console.WriteLine();
				x = 0;
			}
		}
		if (x > 0)
			Console.WriteLine();
	}

	public unsafe void RemoveHeadInWaitingList(int list) {
		DataFragments data = WaitingList[list][0];
		data.Return();

		// File freeing later...

		WaitingList[list].Remove(data);
	}

	public unsafe void CheckWaitingList(int list) {
		if (WaitingList[list].Count == 0 || OutSequenceAck <= 0)
			return;

		DataFragments data = WaitingList[list][0];

		if (data.AckedFragments == data.NumFragments) {
			if (Net.net_showfragments.GetBool())
				Msg($"Sending complete: {data.NumFragments} fragments, {data.Bytes} bytes\n");
			RemoveHeadInWaitingList(list);
			return;
		}
	}

	public void FlowNewPacket(int flow, int seq, int outSeqAck, int choked, int packetDrop, int wiresize) {
		// todo
	}

	public void ProcessPacket(NetPacket packet, bool hasHeader) {
		Debug.Assert(packet != null);

		bf_read msg = packet.Message;

		if (RemoteAddress != null && !packet.From.CompareAddress(RemoteAddress))
			return;

		FlowUpdate(NetFlow.FLOW_INCOMING, packet.WireSize + UDP_HEADER_SIZE);

		PacketFlag flags = PacketFlag.None;
		if (hasHeader)
			flags = ProcessPacketHeader(packet);

		if (flags == PacketFlag.Invalid) {
			Debug.Assert(false);
			return;
		}

		Debug.Assert(!flags.HasFlag(PacketFlag.Compressed));

		LastReceived = Net.Time;

		MessageHandler.PacketStart(InSequence, OutSequenceAck);

		if (flags.HasFlag(PacketFlag.Reliable)) {
			int i = 0;
			int bit = 1 << (int)msg.ReadUBitLong(3);

			for (i = 0; i < MAX_STREAMS; i++) {
				if (msg.ReadOneBit() != 0) {
					if (!ReadSubChannelData(msg, i))
						return; // Error reading fragments; drop whole packet
				}
			}

			InReliableState = FLIPBIT(InReliableState, bit);

			for (i = 0; i < MAX_STREAMS; i++) {
				if (!CheckReceivingList(i))
					return;
			}
		}

		if (msg.BitsLeft > 0) {
			if (!ProcessMessages(msg))
				return;
		}

		MessageHandler.PacketEnd();
	}

	public unsafe bool CheckReceivingList(int list) {
		DataFragments data = ReceiveList[list];

		if (data.Buffer == null)
			return true;

		if (data.AckedFragments < data.NumFragments)
			return true;

		if (data.AckedFragments > data.NumFragments) {
			Warning($"receiving failed: too many fragments {data.AckedFragments}/{data.NumFragments} from {RemoteAddress}\n");
			return false;
		}

		// Got all fragments
		if (Net.net_showfragments.GetBool())
			Msg($"Receiving complete: {data.NumFragments} fragments, {data.Bytes} bytes\n");

		if (data.Compressed)
			UncompressFragments(data);

		if (data.Filename == null) {
			bf_read buffer = new bf_read(data.Buffer, data.Bytes);
			if (!ProcessMessages(buffer))
				return false;
		}
		else {
			HandleUpload(data, MessageHandler);
		}

		if (data.Buffer != null) {
			data.Return();
		}

		return true;
	}

	public void HandleUpload(DataFragments buffer, INetChannelHandler handler) {
		// todo
	}

	public unsafe void UncompressFragments(DataFragments data) {
		if (!data.Compressed || data.Buffer == null)
			return;

		uint uncompressedSize = data.UncompressedSize;

		if (uncompressedSize == 0)
			return;

		if (data.Bytes > 100_000_000)
			return;

		byte[] newBuffer = ArrayPool<byte>.Shared.Rent((int)(uncompressedSize * 3u));

		fixed (byte* bPtr = newBuffer)
		fixed (byte* dBfr = data.Buffer) {
			Net.BufferToBufferDecompress(bPtr, ref uncompressedSize, dBfr, data.Bytes);
		}

		data.Return();
		data.Buffer = newBuffer;
		data.Bytes = uncompressedSize;
		data.Compressed = false;
	}

	public bool ProcessControlMessage(uint cmd, bf_read buf) {
		string? str = null;
		if (cmd == Net.NOP)
			return true;

		if (cmd == Net.Disconnect) {
			buf.ReadString(out str, 1024);
			MessageHandler?.ConnectionClosing(str ?? "Forced disconnect");
			return false;
		}

		if (cmd == Net.File) {
			uint transferID = buf.ReadUBitLong(32);
			buf.ReadString(out str, 1024);
			if (buf.ReadOneBit() != 0 && false) {
				MessageHandler.FileRequested(str, transferID);
			}
			else {
				MessageHandler.FileDenied(str, transferID);
			}

			return true;
		}

		Warning($"NetChannel: received bad control cmd {cmd} from {RemoteAddress}.\n");
		return false;
	}

	public bool ProcessMessages(bf_read buf) {
		string showmsgname = Net.net_showmsg.GetString();
		string blockmsgname = Net.net_blockmsg.GetString();

		int startbit = buf.BitsRead;
		while (true) {
			if (buf.Overflowed) {
				MessageHandler.ConnectionCrashed("Buffer overflow in net message");
				return false;
			}

			if (buf.BitsLeft < NETMSG_TYPE_BITS)
				break;

			uint cmd = buf.ReadUBitLong(NETMSG_TYPE_BITS);

			if (cmd <= Net.File) {
				if (!ProcessControlMessage(cmd, buf))
					return false;

				continue;
			}

			// Find net message handler
			INetMessage? netMsg = FindMessage((int)cmd);
			if (netMsg != null) {
				string msgName = netMsg.GetName();

				int msgStartBit = buf.BitsRead;
				if (!netMsg.ReadFromBuffer(buf)) {
					Error($"NetChannel: failed reading message {msgName} from {RemoteAddress}\n");
					Debug.Assert(false);
					return false;
				}

				UpdateMessageStats(netMsg.GetGroup(), buf.BitsRead - msgStartBit);


				if (showmsgname != "0" && (showmsgname == "1" || showmsgname.Equals(netMsg.GetName(), StringComparison.OrdinalIgnoreCase))) {
					Msg($"Msg from {RemoteAddress}: {netMsg.ToString()?.Trim('\n')}\n");
				}

				if (blockmsgname != "0" && (blockmsgname == "1" || blockmsgname.Equals(netMsg.GetName(), StringComparison.OrdinalIgnoreCase))) {
					Msg($"Blocking message {netMsg.ToString()?.Trim('\n')}\n");
					continue;
				}

				// todo: block

				ProcessingMessages = true;
				bool ret = netMsg.Process();
				ProcessingMessages = false;

				if (ShouldDelete) {
					return false;
				}

				if (ClearedDuringProcessing) {
					ClearedDuringProcessing = false;
					return false;
				}

				if (!ret) {
					Warning($"NetChannel: no handler processed message '{msgName}'\n");
					return false;
				}

				if (IsOverflowed)
					return false;
			}
			else {
				Warning($"NetChannel: unknown net message ({cmd}) from {RemoteAddress}.\n");
				//Debug.Assert(false);
				return false;
			}
		}

		return true;
	}

	public bool ShouldDelete { get; private set; }
	public bool ProcessingMessages { get; private set; }
	public bool ClearedDuringProcessing { get; private set; }

	public void UpdateMessageStats(NetChannelGroup group, int bits) {
		// TODO
	}


	public List<INetMessage> NetMessages = [];

	public INetMessage? FindMessage(int type) {
		foreach (var netmsg in NetMessages)
			if (netmsg.GetMessageType() == type)
				return netmsg;

		return null;
	}

	public bool RegisterMessage(INetMessage msg) {
		if (FindMessage(msg.GetMessageType()) != null)
			return false;

		NetMessages.Add(msg);
		msg.SetNetChannel(this);

		return true;
	}

	public void RegisterMessage<T>() where T : INetMessage, new() => RegisterMessage(new T());


	public bool SendNetMsg(INetMessage msg, bool forceReliable = false, bool voice = false) {
		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null)
			return true;

		bf_write stream = StreamUnreliable;
		if (msg.IsReliable || forceReliable)
			stream = StreamReliable;

		if (voice)
			stream = StreamVoice;

		if (msg.IsReliable)
			Msg("writing " + msg + "\n");
		return msg.WriteToBuffer(stream);
	}

	public bool SendData(bf_write msg, bool reliable) {
		// No remote address on the NetChannel
		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null)
			return true;

		// Empty (or somehow, negative-length) packet
		if (msg.BitsWritten <= 0)
			return true;

		// The write-from overflowed, unreliable message, drop packet
		if (msg.Overflowed && !reliable)
			return true;

		bf_write buf = reliable ? StreamReliable : StreamUnreliable;

		// Writing the write-from (msg) to the write-to (buf) would result in the write-to overflowing
		if (msg.BitsWritten > buf.BitsLeft) {
			if (reliable)
				Warning($"Error: SendData reliable data too big ({msg.BytesWritten} bytes)\n");

			return false;
		}

		// Copy write-from -> write-to buffers
		unsafe {
			return buf.WriteBits(msg.BaseArray, msg.BitsWritten);
		}
	}

	public static int ENCODE_PAD_BITS(int x) => x << 5 & 0xff;
	public static byte ENCODE_PAD_BITS(byte x) => (byte)(x << 5 & 0xff);
	public static int DECODE_PAD_BITS(int x) => x >> 5 & 0xff;
	public static byte DECODE_PAD_BITS(byte x) => (byte)(x >> 5 & 0xff);

	public SubChannel? GetFreeSubChannel() {
		for (int i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.Free)
				return SubChannels[i];
		}

		return null;
	}

	public unsafe void UpdateSubchannels() {
		// first check if there is a free subchannel
		SubChannel? freeSubChan = GetFreeSubChannel();

		if (freeSubChan == null)
			return; //all subchannels in use right now

		int i, nSendMaxFragments = MaxReliablePayloadSize / FRAGMENT_SIZE;

		bool bSendData = false;

		for (i = 0; i < MAX_STREAMS; i++) {
			if (WaitingList[i].Count <= 0)
				continue;

			DataFragments data = WaitingList[i][0]; // get head

			if (data.AsTCP)
				continue;

			int nSentFragments = data.AckedFragments + data.PendingFragments;

			Debug.Assert(nSentFragments <= data.NumFragments);

			if (nSentFragments == data.NumFragments)
				continue; // all fragments already send

			// how many fragments can we send ?

			int numFragments = Math.Min(nSendMaxFragments, data.NumFragments - nSentFragments);

			// if we are in file background transmission mode, just send one fragment per packet
			//if (i == FRAG_FILE_STREAM && FileBackgroundTranmission)
			//numFragments = min(1, numFragments);

			// copy fragment data into subchannel

			freeSubChan.StartFragment[i] = nSentFragments;
			freeSubChan.NumFragments[i] = numFragments;

			data.PendingFragments += numFragments;

			bSendData = true;

			nSendMaxFragments -= numFragments;

			if (nSendMaxFragments <= 0)
				break;
		}

		if (bSendData) {
			// flip channel bit 
			int bit = 1 << freeSubChan.Index;

			OutReliableState = FLIPBIT(OutReliableState, bit);

			freeSubChan.State = SubChannelState.ToSend;
			freeSubChan.SendSeqNumber = 0;
		}
	}
	public unsafe bool SendSubChannelData(bf_write buf) {
		SubChannel? subChan = null;
		int i;
		// compress fragments
		// send tcp data
		UpdateSubchannels();

		for (i = 0; i < SubChannel.MAX; i++) {
			subChan = SubChannels[i];

			if (subChan.State == SubChannelState.ToSend)
				break;
		}

		if (i == SubChannel.MAX || subChan == null)
			return false; // no data to send in any subchannel

		buf.WriteUBitLong((uint)i, 3);

		for (i = 0; i < MAX_STREAMS; i++) {
			if (subChan.NumFragments[i] == 0) {
				buf.WriteOneBit(0); // no data for this stream
				continue;
			}

			DataFragments data = WaitingList[i][0];

			buf.WriteOneBit(1); // data follows:

			uint offset = (uint)(subChan.StartFragment[i] * FRAGMENT_SIZE);
			uint length = (uint)(subChan.NumFragments[i] * FRAGMENT_SIZE);

			if (subChan.StartFragment[i] + subChan.NumFragments[i] == data.NumFragments) {
				// we are sending the last fragment, adjust length
				int rest = (int)(FRAGMENT_SIZE - data.Bytes % FRAGMENT_SIZE);
				if (rest < FRAGMENT_SIZE)
					length -= (uint)rest;
			}

			// if all fragments can be send within a single packet, avoid overhead (if not a file)
			bool bSingleBlock = subChan.NumFragments[i] == data.NumFragments &&
								 data.Filename == null;

			if (bSingleBlock) {
				Debug.Assert(length == data.Bytes);
				Debug.Assert(length < MAX_PAYLOAD);
				Debug.Assert(offset == 0);

				buf.WriteOneBit(0); // single block bit

				// data compressed ?
				if (data.Compressed) {
					buf.WriteOneBit(1);
					buf.WriteUBitLong(data.UncompressedSize, MAX_FILE_SIZE_BITS);
				}
				else {
					buf.WriteOneBit(0);
				}

				buf.WriteVarInt32(data.Bytes);
			}
			else {
				buf.WriteOneBit(1); // uses fragments with start fragment offset byte
				buf.WriteUBitLong((uint)subChan.StartFragment[i], MAX_FILE_SIZE_BITS - FRAGMENT_BITS);
				buf.WriteUBitLong((uint)subChan.NumFragments[i], 3);

				if (offset == 0) {
					// this is the first fragment, write header info

					if (data.Filename != null) {
						buf.WriteOneBit(1); // file transmission net message stream
						buf.WriteUBitLong(data.TransferID, 32);
						buf.WriteString(data.Filename);
					}
					else {
						buf.WriteOneBit(0); // normal net message stream
					}

					// data compressed ?
					if (data.Compressed) {
						buf.WriteOneBit(1);
						buf.WriteUBitLong(data.UncompressedSize, MAX_FILE_SIZE_BITS);
					}
					else {
						buf.WriteOneBit(0);
					}

					buf.WriteUBitLong(data.Bytes, MAX_FILE_SIZE_BITS); // 4MB max for files
				}
			}

			// write fragments to buffer
			if (data.Buffer != null) {
				Debug.Assert(data.Filename == null);
				// send from memory block
				fixed (byte* ptr = data.Buffer)
					buf.WriteBytes(ptr + offset, (int)length);
			}
			else // if ( data->file != FILESYSTEM_INVALID_HANDLE )
			{
				// send from file
				throw new Exception("Cannot upload file syet!!!");
			}

			if (Net.net_showfragments.GetBool())
				ConMsg($"Sending subchan {subChan.Index}: start {subChan.StartFragment[i]}, num {subChan.NumFragments[i]}");

			subChan.SendSeqNumber = OutSequence;
			subChan.State = SubChannelState.Waiting;
		}

		return true;
	}

	private unsafe byte[] sendbuf = new byte[MAX_MESSAGE];
	private bf_write send = new();
	public unsafe int SendDatagram(bf_write? datagram) {
		if (Socket == NetSocketType.Client) {
			// todo: maxroutable?

		}

		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null) {
			// demo channels, ignoring
			OutSequence++;
			return OutSequence - 1;
		}

		if (StreamReliable.Overflowed) {
			Warning($"{RemoteAddress}: send reliable stream overflow\n");
			return 0;
		}
		else if (StreamReliable.BitsWritten > 0) {
			CreateFragmentsFromBuffer(StreamReliable, FRAG_NORMAL_STREAM);
			StreamReliable.Reset();
		}

		send.StartWriting(sendbuf, MAX_MESSAGE, 0);

		byte flags = (byte)PacketFlag.None;

		send.WriteLong(OutSequence);
		send.WriteLong(InSequence);

		bf_write flagsPos = send.Copy();

		send.WriteByte(0);

		if (ShouldChecksumPackets()) {
			send.WriteShort(0);
			Debug.Assert(send.BitsWritten % 8 == 0);
		}

		int checksumStart = send.BytesWritten;
		send.WriteByte(InReliableState);
		if (ChokedPackets > 0) {
			flags |= (byte)PacketFlag.Choked;
			send.WriteByte(ChokedPackets & 0xFF);
		}

		flags |= (byte)PacketFlag.Challenge;
		send.WriteLong((int)ChallengeNumber);

		if (SendSubChannelData(send))
			flags |= (byte)PacketFlag.Reliable;

		if (datagram != null) {
			if (datagram.BitsWritten < send.BitsLeft)
				send.WriteBits(datagram.BaseArray, datagram.BitsWritten);
			else
				Warning("NetChannel.SendDatagram: writing datagram would overflow buffer, ignoring\n");
		}

		if (StreamUnreliable.BitsWritten < send.BitsLeft)
			send.WriteBits(StreamUnreliable.BaseArray, StreamUnreliable.BitsWritten);
		else
			Warning("NetChannel.SendDatagram: writing unreliable would overflow buffer, ignoring\n");

		StreamUnreliable.Reset();

		if (StreamVoice.BitsWritten > 0 && StreamVoice.BitsWritten < send.BitsLeft) {
			send.WriteBits(StreamVoice.BaseArray, StreamVoice.BitsWritten);
			StreamVoice.Reset();
		}

		int minRoutable = MIN_ROUTABLE_PAYLOAD;

		if (Socket == NetSocketType.Server)
			minRoutable = minRoutable; // todo: net_minroutable convar

		while (send.BytesWritten < minRoutable)
			send.WriteUBitLong(Net.NOP, NETMSG_TYPE_BITS);

		int remainingBits = send.BitsWritten % 8;
		if (remainingBits > 0 && remainingBits <= 8 - NETMSG_TYPE_BITS)
			send.WriteUBitLong(Net.NOP, NETMSG_TYPE_BITS);

		{
			remainingBits = send.BitsWritten % 8;
			if (remainingBits > 0) {
				int padBits = 8 - remainingBits;
				flags |= ENCODE_PAD_BITS((byte)padBits);

				if (padBits > 0) {
					uint unOnes = BitHelpers.GetBitForBitnum(padBits) - 1;
					send.WriteUBitLong(unOnes, padBits);
				}
			}
		}

		bool sendVoice = false;

		bool compress = false;
		// net compress?

		flagsPos.WriteByte(flags);

		if (ShouldChecksumPackets()) {
			fixed (byte* ptr = send.BaseArray) {
				void* pvData = ptr + checksumStart;
				Debug.Assert(send.BitsWritten % 8 == 0);
				int nCheckSumBytes = send.BytesWritten - checksumStart;
				ushort usCheckSum = BufferToShortChecksum(pvData, nCheckSumBytes);
				flagsPos.WriteUBitLong(usCheckSum, 16);
			}
		}

		int bytesSent = Net.SendPacket(this, Socket, RemoteAddress, send.BaseArray, send.BytesWritten, sendVoice ? StreamVoice : null, compress);

		if (sendVoice)
			StreamVoice.Reset();

		int totalSize = bytesSent + UDP_HEADER_SIZE;

		FlowNewPacket(NetFlow.FLOW_OUTGOING, OutSequence, InSequence, ChokedPackets, 0, totalSize);
		FlowUpdate(NetFlow.FLOW_OUTGOING, totalSize);

		if (ClearTime < Net.Time)
			ClearTime = Net.Time;

		double addTime = totalSize / (double)Rate;
		ClearTime += addTime;
		if (Net.net_maxcleartime.GetDouble() > 0) {
			double latestClearTime = Net.Time + Net.net_maxcleartime.GetDouble();
			if (ClearTime > latestClearTime)
				ClearTime = latestClearTime;
		}

		// convar...
		ChokedPackets = 0;
		OutSequence++;

		return OutSequence - 1;
	}

	public unsafe bool CreateFragmentsFromBuffer(bf_write buffer, int stream) {
		bf_write bfwrite = new();
		DataFragments? data = null;

		// if we have more than one item in the waiting list, try to add the 
		// reliable data to the last item. that doesn't work with the first item
		// since it may have been already send and is waiting for acknowledge

		int count = WaitingList[stream].Count;

		if (count > 1) {
			// get last item in waiting list
			data = WaitingList[stream][count - 1];

			int totalBytes = Bits2Bytes((int)(data.Bits + (uint)buffer.BitsWritten));

			totalBytes = PAD_NUMBER(totalBytes, 4); // align to 4 bytes boundary

			if (totalBytes < MAX_PAYLOAD && data.Buffer != null) {
				// we have enough space for it, create new larger mem buffer
				byte[] newBuf = ArrayPool<byte>.Shared.Rent(totalBytes);

				Array.Copy(data.Buffer, newBuf, data.Bytes);
				ArrayPool<byte>.Shared.Return(data.Buffer, true);

				data.Buffer = newBuf; // set new buffer

				bfwrite.StartWriting(newBuf, totalBytes, (int)data.Bits);
			}
			else {
				data = null; // reset to NULL
			}
		}

		// if not added to existing item, create a new reliable data waiting buffer
		if (data == null) {
			int totalBytes = Bits2Bytes(buffer.BitsWritten);

			totalBytes = PAD_NUMBER(totalBytes, 4); // align to 4 bytes boundary

			data = new DataFragments();
			data.Bytes = 0;    // not filled yet
			data.Bits = 0;
			data.Buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
			data.Compressed = false;
			data.UncompressedSize = 0;
			data.Filename = null;

			bfwrite.StartWriting(data.Buffer, totalBytes, 0);
			WaitingList[stream].Add(data);  // that's it for now
		}

		// update bit length
		data.Bits += (uint)buffer.BitsWritten;
		data.Bytes = (uint)Bits2Bytes((int)data.Bits);

		// write new reliable data to buffer
		bfwrite.WriteBits(buffer.BaseArray, buffer.BitsWritten);

		// fill last bits in last byte with NOP if necessary
		int nRemainingBits = bfwrite.BitsWritten % 8;
		if (nRemainingBits > 0 && nRemainingBits <= 8 - NETMSG_TYPE_BITS) {
			bfwrite.WriteUBitLong(Net.NOP, NETMSG_TYPE_BITS);
		}

		// check if send as stream or with snapshot
		data.AsTCP = StreamActive && data.Bytes > MaxReliablePayloadSize;

		// calc number of fragments needed
		data.NumFragments = BYTES2FRAGMENTS((int)data.Bytes);
		data.AckedFragments = 0;
		data.PendingFragments = 0;

		return true;
	}

	public bool Transmit(bool onlyReliable = false) {
		if (onlyReliable)
			StreamUnreliable.Reset();

		return SendDatagram(null) != 0;
	}

	public int SplitPacketSequence;

	public bool HasPendingReliableData => StreamReliable.BitsWritten > 0
									   || WaitingList[FRAG_NORMAL_STREAM].Count > 0
									   || WaitingList[FRAG_FILE_STREAM].Count > 0;

	public double TimeConnected => Math.Max(0, Net.Time - ConnectTime);
	public bool IsTimedOut => Timeout == -1 ? false : LastReceived + Timeout < Net.Time;
	public bool IsTimingOut => Timeout == -1 ? false : LastReceived + CONNECTION_PROBLEM_TIME < Net.Time;
	public double TimeSinceLastReceived => Math.Max(Net.Time - LastReceived, 0);
	public bool IsOverflowed => StreamReliable.Overflowed;

	public void Reset() {
		StreamUnreliable.Reset();
		StreamReliable.Reset();
		ClearTime = 0;
		ChokedPackets = 0;
		SplitPacketSequence = 1;
	}

	public double GetAvgData(int flow) => DataFlow[flow].AverageBytesPerSec;
	public double GetAvgPackets(int flow) => DataFlow[flow].AveragePacketsPerSec;
	public int GetTotalData(int flow) => DataFlow[flow].TotalBytes;
	public int GetSequenceNr(int flow) {
		if (flow == NetFlow.FLOW_OUTGOING)
			return OutSequence;

		else if (flow == NetFlow.FLOW_INCOMING)
			return InSequence;

		return 0;
	}

	public bool IsValidPacket(int flow, int frame) => DataFlow[flow].Frames[frame & NET_FRAMES_MASK].IsValid;
	public double GetPacketTime(int flow, int frame) => DataFlow[flow].Frames[frame & NET_FRAMES_MASK].Time;
	public double GetLatency(int flow) => DataFlow[flow].Latency;

	public unsafe bool ReadSubChannelData(bf_read buf, int stream) {
		DataFragments data = ReceiveList[stream]; // get list
		int startFragment = 0;
		int numFragments = 0;
		uint offset = 0;
		uint length = 0;


		bool bSingleBlock = buf.ReadOneBit() == 0; // is single block ?

		if (!bSingleBlock) {
			startFragment = (int)buf.ReadUBitLong(MAX_FILE_SIZE_BITS - FRAGMENT_BITS); // 16 MiB max
			numFragments = (int)buf.ReadUBitLong(3);  // 8 fragments per packet max
			offset = (uint)(startFragment * FRAGMENT_SIZE);
			length = (uint)(numFragments * FRAGMENT_SIZE);
		}

		if (offset == 0) // first fragment, read header info
		{
			data.Filename = null;
			data.Compressed = false;
			data.TransferID = 0;

			if (bSingleBlock) {
				// data compressed ?
				if (buf.ReadOneBit() == 1) {
					data.Compressed = true;
					data.UncompressedSize = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
				}
				else {
					data.Compressed = false;
				}

				data.Bytes = buf.ReadVarInt32();
			}
			else {

				if (buf.ReadOneBit() == 1) // is it a file ?
				{
					data.TransferID = buf.ReadUBitLong(32);
					data.Filename = buf.ReadString(260);
				}

				// data compressed ?
				if (buf.ReadOneBit() == 1) {
					data.Compressed = true;
					data.UncompressedSize = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
				}
				else {
					data.Compressed = false;
				}

				data.Bytes = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
			}

			if (data.Buffer != null) {
				// last transmission was aborted, free data
				ArrayPool<byte>.Shared.Return(data.Buffer, true);
				data.Buffer = null;
				Warning($"Fragment transmission aborted at {data.AckedFragments}/{data.NumFragments} from {RemoteAddress}.\n");
			}

			data.Bits = data.Bytes * 8;
			data.Rent(PAD_NUMBER((int)data.Bytes, 4));
			data.AsTCP = false;
			data.NumFragments = BYTES2FRAGMENTS((int)data.Bytes);
			data.AckedFragments = 0;
			//data.file = FILESYSTEM_INVALID_HANDLE;

			if (bSingleBlock) {
				numFragments = data.NumFragments;
				length = (uint)(numFragments * FRAGMENT_SIZE);
			}

			if (data.Bytes > MAX_FILE_SIZE) {
				// This can happen with the compressed path above, which uses VarInt32 rather than MAX_FILE_SIZE_BITS
				Warning($"Net message exceeds max size ({MAX_FILE_SIZE} / {data.Bytes})\n");
				// Subsequent packets for this transfer will treated as invalid since we never setup a buffer.
				return false;
			}

		}
		else {
			if (data.Buffer == null) {
				// This can occur if the packet containing the "header" (offset == 0) is dropped.  Since we need the header to arrive we'll just wait
				//  for a retry
				// ConDMsg("Received fragment out of order: %i/%i\n", startFragment, numFragments );
				return false;
			}
		}

		if (startFragment + numFragments == data.NumFragments) {
			// we are receiving the last fragment, adjust length
			int rest = FRAGMENT_SIZE - (int)(data.Bytes % FRAGMENT_SIZE);
			if (rest < FRAGMENT_SIZE)
				length -= (uint)rest;
		}
		else if (startFragment + numFragments > data.NumFragments) {
			// a malicious client can send a fragment beyond what was arranged in fragment#0 header
			// old code will overrun the allocated buffer and likely cause a server crash
			// it could also cause a client memory overrun because the offset can be anywhere from 0 to 16MB range
			// drop the packet and wait for client to retry
			Warning($"Received fragment chunk out of bounds: {startFragment}+{numFragments}>{data.NumFragments} from {RemoteAddress}\n");
			return false;
		}

		Debug.Assert(offset + length <= data.Bytes);
		if (length == 0 || offset + length > data.Bytes) {
			data.Return();
			Warning($"Malformed fragment offset {offset} len {length} buffer size {PAD_NUMBER((int)data.Bytes, 4)} from {RemoteAddress}\n");
			return false;
		}

		fixed (byte* ptr = data.Buffer)
			buf.ReadBytes(new Span<byte>(ptr + offset, (int)length)); // read data

		data.AckedFragments += numFragments;

		//if (net_showfragments.GetBool())
		Msg($"Received fragments: start {startFragment}, num {numFragments}, end {data.NumFragments}\n");

		return true;
	}

	private void FlowUpdate(int flow, int size) {

	}

	public void SetRemoteFramerate(float hostFrameTime, float hostFrameDeviation) {
		RemoteFrameTime = hostFrameTime;
		RemoteFrameTimeStdDeviation = hostFrameDeviation;
	}
}
