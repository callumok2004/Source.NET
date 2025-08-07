﻿using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using static Source.Dbg;
using static Source.Common.Networking.Protocol;
using Snappier;
using Microsoft.Extensions.DependencyInjection;
using Source.Common.Bitbuffers;
using Source.Common.Compression;
using Source.Common.Commands;

namespace Source.Common.Networking;

public class Net
{
	/// <summary>
	/// NOP command used for padding.
	/// </summary>
	public const int NOP = 0;
	/// <summary>
	/// Disconnect, last message in connection.
	/// </summary>
	public const int Disconnect = 1;
	/// <summary>
	/// File transmission message request/denial.
	/// </summary>
	public const int File = 2;
	/// <summary>
	/// Send the last world tick.
	/// </summary>
	public const int Tick = 3;
	/// <summary>
	/// A string command
	/// </summary>
	public const int StringCmd = 4;
	/// <summary>
	/// Sends one or more convar settings
	/// </summary>
	public const int SetConVar = 5;
	/// <summary>
	/// Signals current signon state.
	/// </summary>
	public const int SignOnState = 6;

	public List<VecSplitPacketEntries> SplitPackets = [];

	public void DiscardStaleSplitPackets(NetSocketType sock) {
		VecSplitPacketEntries splitPacketEntries = SplitPackets[(int)sock];
		int i;
		for (i = splitPacketEntries.Count - 1; i >= 0; i--) {
			SplitPacketEntry entry = splitPacketEntries[i];
			Debug.Assert(entry != null);

			if (Time < entry.LastActiveTime + SPLIT_PACKET_STALE_TIME)
				continue;

			SplitPackets.RemoveAt(i);
		}

		if (splitPacketEntries.Count > SPLIT_PACKET_TRACKING_MAX) {
			while (splitPacketEntries.Count > SPLIT_PACKET_TRACKING_MAX) {
				SplitPacketEntry entry = splitPacketEntries[i];
				if (Time != entry.LastActiveTime) {
					splitPacketEntries.RemoveAt(0);
				}
			}
		}
	}

	public SplitPacketEntry? FindOrCreateSplitPacketEntry(NetSocketType sock, NetAddress from) {
		VecSplitPacketEntries splitPacketEntries = SplitPackets[(int)sock];
		int i = 0;
		int count = splitPacketEntries.Count;
		SplitPacketEntry? entry = null;

		for (i = 0; i < count; i++) {
			entry = splitPacketEntries[i];
			Debug.Assert(entry != null);

			if (from.CompareAddress(entry.From))
				break;
		}

		if (i >= count) {
			SplitPacketEntry newEntry = new();
			newEntry.From = from;

			splitPacketEntries.Add(newEntry);
			// C++ does it like this for pointer reasons; we could just throw newEntry into here instead though
			entry = splitPacketEntries[splitPacketEntries.Count - 1];
		}

		return entry;
	}

	public string DescribeSocket(int sock) {
		switch (sock) {
			case 0: return "cl ";
			case 1: return "sv ";
			default: return "?? ";
		}
	}
	public string DescribeSocket(NetSocketType sock) => DescribeSocket((int)sock);

	public static int Bits2Bytes(int b) {
		return b + 7 >> 3;
	}
	public double Time { get; private set; }

	public bool IsMultiplayer() => Multiplayer; // its always going to be true; listen servers don't exist yet (if ever)


	public readonly List<NetSocket> NetSockets = [];
	public readonly List<NetPacket> NetPackets = [];
	public readonly ConcurrentBag<NetChannel> NetChannels = [];
	public readonly Stack<NetScratchBuffer> NetScratchBuffers = [];

	public NetScratchBuffer ObtainScratchBuffer() {
		if (NetScratchBuffers.TryPop(out var res))
			return res;

		res = new NetScratchBuffer();
		NetScratchBuffers.Push(res);
		return res;
	}

	public bool StringToAdr(string host, int port, [NotNullWhen(true)] out IPEndPoint? ep) {
		ep = null;
		if (host == null) return false;

		if (host == "localhost") {
			ep = new IPEndPoint(IPAddress.Loopback, port);
			return true;
		}
		if (IPAddress.TryParse(host, out IPAddress? ip)) {
			ep = new IPEndPoint(ip, port < 0 ? 0 : port);
			return true;
		}

		return false;
	}

	public bool StringToAdr(string host, [NotNullWhen(true)] out IPEndPoint? ep) {
		ep = null;
		if (host == null) return false;

		return IPEndPoint.TryParse(host, out ep);
	}

	public NetChannel? CreateNetChannel(
		NetSocketType socket, NetAddress address, string name, INetChannelHandler handler,
		bool forceNewChannel = false, int protocolVersion = VERSION
	) {

		NetChannel? channel = null;

		if (!forceNewChannel && address != null && TryFindNetChannel(socket, address, out channel))
			channel.Clear();

		if (channel == null) {
			channel = new NetChannel(this);
			lock (NetChannels) {
				NetChannels.Add(channel);
			}
		}

		// ClearLagData?

		channel.Setup(socket, address, name, handler, protocolVersion);

		return channel;
	}

	public void RemoveNetChannel(NetChannel netchan, bool delete) {
		if (netchan == null) return;

		lock (NetChannels) {
			if (NetChannels.FirstOrDefault(x => x == netchan) == null) {
				Msg($"Net.CloseNetChannel: unknown net channel.\n");
				return;
			}

			// todo: should use ConcurrentDictionary instead to avoid this mess (probably?)
			NetChannel[] copychannels = new NetChannel[NetChannels.Count - 1];
			int i = 0;
			foreach (var item in NetChannels) {
				if (item == netchan)
					continue;

				copychannels[i] = item;
				i++;
			}

			NetChannels.Clear();

			foreach (var channel in copychannels) {
				NetChannels.Add(channel);
			}

			// todo; multithreading?
		}
	}

	public NetChannel? FindNetChannel(NetSocketType socket, NetAddress address) {
		lock (NetChannels) {
			foreach (var chan in NetChannels) {
				if (chan.Socket != chan.Socket)
					continue;

				if (address.CompareAddress(chan.RemoteAddress)) {
					return chan;
				}
			}

			return null;
		}
	}

	public bool TryFindNetChannel(NetSocketType socket, NetAddress address, [NotNullWhen(true)] out NetChannel? netchan) {
		netchan = FindNetChannel(socket, address);
		return netchan != null;
	}

	public unsafe void ProcessSocket(NetSocketType sock, IConnectionlessPacketHandler handler) {
		NetPacket? packet;
		lock (NetChannels) {
			int numChannels = NetChannels.Count;

			foreach (var netchan in NetChannels) {
				if (netchan.Socket != sock)
					continue;

				// process TCP stream later
			}
		}

		// datagrams from socket

		NetScratchBuffer scratch = ObtainScratchBuffer();
		while (true) {
			packet = GetPacket(sock, scratch.Data);
			if (packet == null) {
				break;
			}

			unsafe {
				int header = packet.Message.ReadLong();

				switch (header) {
					case CONNECTIONLESS_HEADER:
						// Hand off out-of-band packet
						handler.ProcessConnectionlessPacket(ref packet);
						continue;
				}

				packet.Message.Reset();

				NetChannel? channel = FindNetChannel(sock, packet.From);
				if (channel != null) {
					channel.ProcessPacket(packet, true);
				}
			}
		}
		NetScratchBuffers.Push(scratch);
	}

	public bool GetLoopPacket(NetPacket packet) {
		return false; // not doing this rn
	}

	private int net_error;
	private void ClearLastError() => net_error = 0;
	private int GetLastError() => net_error;
	private bool Errored => net_error > 0;

	public unsafe bool GetLong(NetSocketType sock, NetPacket packet) {
		int packetNumber, packetCount, sequenceNumber, offset;
		short packetID;

		SPLITPACKET* pHeader;

		if (packet.Size < sizeof(SPLITPACKET)) {
			Warning($"Invalid split packet length {packet.Size}\n");
			return false;
		}

		fixed (byte* ptr = packet.Data) {
			pHeader = (SPLITPACKET*)ptr;
			sequenceNumber = pHeader->SequenceNumber;
			packetID = pHeader->PacketID;
			packetNumber = packetID >> 8;
			packetCount = packetID & 0xff;

			int splitSizeMinusHeader = pHeader->SplitSize;
			if (splitSizeMinusHeader < MIN_SPLIT_SIZE || splitSizeMinusHeader > MAX_SPLIT_SIZE) {
				Warning($"Net.GetLong: Split packet from {packet.From} with invalid split size (number {packetNumber}/ count {packetCount}) where size {splitSizeMinusHeader} is out of valid range [{MIN_SPLIT_SIZE} - {MAX_SPLIT_SIZE}]\n");
				return false;
			}

			if (packetNumber >= MAX_SPLITPACKET_SPLITS || packetCount > MAX_SPLITPACKET_SPLITS) {
				Warning($"Net.GetLong: Split packet from {packet.From} with too many split parts (number {packetNumber}/ count {packetCount}), where {MAX_SPLITPACKET_SPLITS} is max count allowed\n");
				return false;
			}

			SplitPacketEntry? entry = FindOrCreateSplitPacketEntry(sock, packet.From);
			Debug.Assert(entry != null);
			if (entry == null)
				return false;

			entry.LastActiveTime = Time;
			Debug.Assert(packet.From.CompareAddress(entry.From));

			if (entry.NetSplit.CurrentSequence == -1 || sequenceNumber != entry.NetSplit.CurrentSequence) {
				entry.NetSplit.CurrentSequence = sequenceNumber;
				entry.NetSplit.SplitCount = packetCount;
				entry.NetSplit.ExpectedSplitSize = splitSizeMinusHeader;
			}

			if (entry.NetSplit.ExpectedSplitSize != splitSizeMinusHeader) {
				Warning($"Net.GetLong: Split packet from {packet.From} with inconsistent split size (number {packetNumber}/ count {packetCount}) where size {splitSizeMinusHeader} not equal to initial size of {entry.NetSplit.ExpectedSplitSize}\n");
				entry.LastActiveTime = Time + SPLIT_PACKET_STALE_TIME;
				return false;
			}

			int size = packet.Size - sizeof(SPLITPACKET);
			if (entry.SplitFlags[packetNumber] != sequenceNumber) {
				if (packetNumber == packetCount - 1)
					entry.NetSplit.TotalSize = (packetCount - 1) * splitSizeMinusHeader + size;

				entry.NetSplit.SplitCount--;
				entry.SplitFlags[packetNumber] = sequenceNumber;

				if (true) {
					Msg($"<-- [{DescribeSocket(sock)}] Split packet {packetNumber + 1}/{packetCount} seq {sequenceNumber} size {size} mtu {splitSizeMinusHeader + sizeof(SPLITPACKET)} from {packet.From}");
				}
			}
			else {
				Warning($"Net.GetLong: Ignoring duplicated split packet {packetNumber + 1} of {packetCount} ({size} bytes) from {packet.From}\n");
			}

			// Copy incoming data
			offset = packetNumber * splitSizeMinusHeader;
			fixed (byte* src = entry.NetSplit.Buffer) {
				byte* entryNSBuf = src + offset;
				NativeMemory.Copy(ptr + sizeof(SPLITPACKET), entryNSBuf, (nuint)size);
			}

			if (entry.NetSplit.SplitCount <= 0) {
				entry.NetSplit.CurrentSequence = -1;
				if (entry.NetSplit.TotalSize > MAX_MESSAGE) {
					Warning($"Split packet too large! {entry.NetSplit.TotalSize} bytes from {packet.From}\n");
					return false;
				}

				fixed (byte* dst = packet.Data)
				fixed (byte* src = entry.NetSplit.Buffer) {
					NativeMemory.Copy(src, dst, (nuint)entry.NetSplit.TotalSize);
				}

				packet.Size = entry.NetSplit.TotalSize;
				packet.WireSize = entry.NetSplit.TotalSize;

				return true;
			}
		}

		return false;
	}
	public static uint LZSS_ID = 'S' << 24 | 'S' << 16 | 'Z' << 8 | 'L';
	public static uint SNAPPY_ID = 'P' << 24 | 'A' << 16 | 'N' << 8 | 'S';
	public static unsafe int GetUncompressedSize(byte* compressedData, uint compressedLen) {
		lzss_header* pHeader = (lzss_header*)compressedData;

		if (compressedLen >= sizeof(lzss_header) && pHeader->id == LZSS_ID) {
			return (int)pHeader->actualSize; // LZSS size
		}

		if (compressedLen > 4 && pHeader->id == SNAPPY_ID) {
			Span<byte> d = new Span<byte>(compressedData, (int)(compressedLen - 4));
			int snappySize = Snappy.GetUncompressedLength(d);
			if (snappySize > 0)
				return snappySize;
		}

		return -1;
	}
	public const int LZSS_LOOKSHIFT = 4;
	public const int LZSS_LOOKAHEAD = 1 << LZSS_LOOKSHIFT;
	public unsafe uint GetActualSize(byte* pInput) {
		lzss_header* asU = (lzss_header*)pInput;

		if (asU != null && asU->id == LZSS_ID)
			return asU->actualSize;

		return 0;
	}
	public unsafe uint SafeUncompress(byte* pInput, byte* pOutput) {
		uint totalBytes = 0;
		int cmdByte = 0;
		int getCmdByte = 0;

		uint actualSize = GetActualSize(pInput);
		if (actualSize == 0) {
			// unrecognized
			return 0;
		}

		pInput += sizeof(lzss_header_t);

		for (; ; )
		{
			if (getCmdByte <= 0) {
				cmdByte = *pInput++;
			}
			getCmdByte = getCmdByte + 1 & 0x07;

			if ((cmdByte & 0x01) == 0x01) {
				int position = *pInput++ << LZSS_LOOKSHIFT;
				position |= *pInput >> LZSS_LOOKSHIFT;
				int count = (*pInput++ & 0x0F) + 1;
				if (count == 1) {
					break;
				}
				byte* pSource = pOutput - position - 1;
				for (int i = 0; i < count; i++) {
					*pOutput++ = *pSource++;
				}
				totalBytes += (uint)count;
			}
			else {
				*pOutput++ = *pInput++;
				totalBytes++;
			}
			cmdByte = cmdByte >> 1;
		}

		if (totalBytes != actualSize) {
			return 0;
		}

		return totalBytes;
	}
	public unsafe uint LZSS_GetActualSize(byte* pInput) {
		lzss_header* pHeader = (lzss_header*)pInput;
		if (pHeader != null && pHeader->id == LZSS_ID)
			return pHeader->actualSize;

		return 0;
	}
	public unsafe bool BufferToBufferDecompress(void* dest, ref uint destLen, void* source, uint sourceLen) {
		Span<byte> src = new Span<byte>(source, (int)sourceLen);
		Span<byte> dst = new Span<byte>(dest, (int)destLen);

		if (CLZSS.IsCompressed((byte*)source)) {
			uint uDecompressedLen = CLZSS.GetActualSize(src);
			if (uDecompressedLen > destLen) {
				Warning($"NET_BufferToBufferDecompress with improperly sized dest buffer ({destLen} in, {uDecompressedLen} needed)\n");
				return false;
			}
			else {

				destLen = CLZSS.Uncompress(src, dst);
			}
		}
		else {
			NativeMemory.Copy(source, dest, sourceLen);
			destLen = sourceLen;
		}

		return true;
	}

	private ArrayPool<byte> DecompressionPool = ArrayPool<byte>.Create();
	public unsafe uint GetDecompressedBufferSize(byte* compressedbuf) {
		if (compressedbuf == null)
			return 0;

		if (!CLZSS.IsCompressed(compressedbuf))
			return 0;

		return CLZSS.GetActualSize(compressedbuf);
	}

	public unsafe bool ReceiveDatagram(NetSocketType sock, NetPacket packet) {
		NetSocket net_socket = NetSockets[(int)packet.Source];
		Socket socket = net_socket.UDP ?? throw new Exception("No UDP socket.");

		EndPoint recv = new IPEndPoint(IPAddress.Any, 0);
		int ret = socket.Available > 0 ? socket.ReceiveFrom(packet.Data, SocketFlags.None, ref recv) : -1;
		/*if (socket.Poll(10, SelectMode.SelectRead)) {
			ret = socket.ReceiveFrom(packet.Data, SocketFlags.None, ref recv);
		}*/

		if (ret >= MIN_MESSAGE) {
			packet.WireSize = ret;
			packet.From.SetFromEndPoint(recv);
			packet.Size = ret;

			if (ret < MAX_MESSAGE) {
				// Check for split messages
				int netHeader;
				fixed (byte* b = packet.Data) {
					netHeader = *(int*)b;
				}
				// Check for split packet
				//Console.WriteLine($"Header: {netHeader}");
				if (netHeader == (int)NetHeaderFlag.SplitPacket) {
					if (!GetLong(sock, packet))
						return false;
				}

				// Check for compressed packet
				if (netHeader == (int)NetHeaderFlag.CompressedPacket) {
					fixed (byte* packetData = packet.Data) {
						byte* lzssStart = packetData + sizeof(uint);
						uint uncompressedSize = GetDecompressedBufferSize(lzssStart);

						if (uncompressedSize <= 0 || uncompressedSize > MAX_PAYLOAD)
							return false;

						byte[] uncompressedArray = DecompressionPool.Rent((int)(uncompressedSize * 2 + 1024));

						fixed (byte* uncompressedBuffer = uncompressedArray) {
							BufferToBufferDecompress(uncompressedBuffer, ref uncompressedSize, lzssStart, (uint)packet.WireSize);

							if (uncompressedSize == 0) {
								DecompressionPool.Return(uncompressedArray);
								Warning($"UDP: discarding {ret} bytes due to decompression error.\n");
								return false;
							}

							NativeMemory.Copy(uncompressedBuffer, packetData, uncompressedSize);
							DecompressionPool.Return(uncompressedArray, true);
							packet.Size = (int)uncompressedSize;
						}
					}
				}

				return LagPacket(true, packet);
				// Check for compressed messages
			}
			else {
				Warning("Net.ReceiveDatagram: Oversize packet\n");
			}
		}
		else if (ret == -1) {
			net_error = 10035;
		}

		return false;
	}

	public bool ReceiveValidDatagram(NetSocketType sock, NetPacket packet) {
		for (int i = 0; i < 1000; i++) {
			ClearLastError();
			if (ReceiveDatagram(sock, packet))
				return true;

			if (Errored)
				break;
		}

		return false;
	}
	public bool LagPacket(bool newdata, NetPacket packet) {
		if ((int)packet.Source >= NetSocket.MAX)
			return newdata; // Fake lag not supported for extra sockets

		// ignore droppackets for now

		// ignore fakeloss for now...

		// ignore fakelag for now.......

		// man

		return newdata;
	}

	public unsafe NetPacket? GetPacket(NetSocketType sock, byte[] scratch) {
		// AdjustLag, DiscardStaleSplitpackets?

		NetPacket packet = NetPackets[(int)sock];

		packet.From.Type = NetAddressType.IP;
		//packet.From.Clear();
		packet.Received = Time;
		packet.Source = sock;
		packet.Data = scratch;
		packet.Size = 0;
		packet.WireSize = 0;

		// Check loopback 
		if (!GetLoopPacket(packet)) {
			if (!IsMultiplayer())
				return null;

			// Then check UDP
			if (!ReceiveValidDatagram(sock, packet)) {
				// Check lag packet
				if (!LagPacket(false, packet))
					return null;
			}
		}

		// Prepare the packet message-buffer-reader for reading from the network packet
		packet.Message.StartReading(packet.Data, packet.Size);
		return packet;
	}

	public bool NoIP { get; set; }

	public void SetMultiplayer(bool multiplayer) {
		if (NoIP && multiplayer) {
			Warning("Warning: Cannot use multiplayer networking with no Internet Protocol\n");
			return;
		}

		if (Dedicated && !Multiplayer) {
			Warning("Warning: Cannot use singleplayer mode on a dedicated server\n");
			return;
		}

		if (Multiplayer != multiplayer) {
			Multiplayer = multiplayer;
			Config();
		}

		if (!multiplayer)
			ClearLoopbackBuffers();
	}

	public void Config() {
		CloseAllSockets();
		Time = 0;
		if (Multiplayer) {
			ConfigLoopbackBuffers(false);
			GetLocalAddress();
			OpenSockets();
		}
		else {
			ConfigLoopbackBuffers(true);
		}

		Msg("Net.Config: ready!\n");
	}

	public NetSocket GetSocket(NetSocketType type) {
		return NetSockets[(int)type];
	}

	public void CloseSocket(Socket socket, NetSocketType socketType = NetSocketType.NotApplicable) {
		socket.Close();

		if (socketType != NetSocketType.NotApplicable) {
			var netsock = NetSockets[(int)socketType];
			if (netsock.TCP == socket) {
				netsock.TCP = null;
				netsock.Listening = false;
			}
		}
	}

	public Socket? OpenSocket(string? netInterface, ref int port, ProtocolType protocol) {
		AddressFamily addressFamily = AddressFamily.InterNetwork;
		SocketType socketType = (protocol == ProtocolType.Tcp) ? SocketType.Stream : SocketType.Dgram;

		Socket? socket;
		try {
			socket = new Socket(addressFamily, socketType, protocol);
		}
		catch (SocketException ex) {
			Warning($"WARNING: OpenSocket: socket failed: {ex.Message}\n");
			return null;
		}

		try {
			socket.Blocking = false;

			if (protocol == ProtocolType.Tcp) {
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(false, 0));
				socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, Protocol.MAX_MESSAGE);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, Protocol.MAX_MESSAGE);

				// TCP sockets do not bind here
				return socket;
			}

			// UDP only
			int currentRecvBuf = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer);
			//Msg($"UDP socket SO_RCVBUF size {currentRecvBuf} bytes, changing to {Protocol.MAX_MESSAGE}\n");
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, Protocol.MAX_MESSAGE);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, Protocol.MAX_MESSAGE);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

			// Determine IP address to bind to
			IPAddress ipAddress = IPAddress.Any;
			if (!string.IsNullOrEmpty(netInterface) && netInterface != "localhost") {
				if (!IPAddress.TryParse(netInterface, out ipAddress)) {
					Warning($"WARNING: OpenSocket: failed to parse address: {netInterface}\n");
					socket.Close();
					return null;
				}
			}

			IPEndPoint endPoint = new IPEndPoint(ipAddress, 0);
			const int PORT_TRY_MAX = 32;
			for (int portOffset = 0; portOffset < PORT_TRY_MAX; portOffset++) {
				int tryPort = port == PORT_ANY ? 0 : port + portOffset;
				endPoint.Port = tryPort;

				try {
					socket.Bind(endPoint);

					if (port != PORT_ANY && portOffset != 0) {
						port = tryPort;
						//Warning($"Socket bound to non-default port {port} because original port was already in use.\n");
					}

					return socket;
				}
				catch (SocketException bindEx) {
					if (port == PORT_ANY) {
						Warning($"WARNING: OpenSocket: bind failed: {bindEx.Message} (code {bindEx.SocketErrorCode})\n");
						socket.Close();
						return null;
					}

					// Try next port
				}
			}

			Warning("WARNING: OpenSocket: unable to bind socket after multiple attempts\n");
			socket.Close();
			return null;
		}
		catch (SocketException ex) {
			Warning($"Socket error: {ex.Message}\n");
			socket?.Close();
			return null;
		}
		catch (Exception ex) {
			Warning($"Unhandled exception: {ex}\n");
			socket?.Close();
			return null;
		}
	}

	public bool Multiplayer { get; private set; }
	public bool Dedicated { get; private set; }

	public void SetDedicated() {
		Dedicated = true;
	}

	public void ClearLoopbackBuffers() {
		// unimplemented
	}
	public void CloseAllSockets() {
		for (int i = 0; i < NetSockets.Count; i++) {
			if (NetSockets[i] != null && NetSockets[i].Port > 0) {
				if (NetSockets[i].UDP != null) CloseSocket(NetSockets[i].UDP!);
				if (NetSockets[i].TCP != null) CloseSocket(NetSockets[i].TCP!);

				NetSockets[i].Port = 0;
				NetSockets[i].Listening = false;
				NetSockets[i].UDP = null;
				NetSockets[i].TCP = null;
			}
		}

		Msg("Closed all sockets.\n");
	}
	public void ConfigLoopbackBuffers(bool alloc) {
		ClearLoopbackBuffers();
	}

	// todo
	public void GetLocalAddress() {

	}

	public const int PORT_ANY = -1;
	public const int PORT_RCON = 27015;
	public const int PORT_MASTER = 2701!;
	public const int PORT_CLIENT = 27005;
	public const int PORT_SERVER = 27015;
	public const int PORT_HLTV = 27020;

	public ushort GetHostPort() => PORT_SERVER;
	public ushort GetClientPort() => PORT_CLIENT;
	public ConVar ipname = new("ip", "localhost", 0, "Overrides IP for multihomed hosts");

	public void OpenSockets() {
		OpenSocketInternal(NetSocketType.Server, GetHostPort(), PORT_SERVER, "server", ProtocolType.Udp, false);
		OpenSocketInternal(NetSocketType.Client, GetClientPort(), PORT_SERVER, "client", ProtocolType.Udp, true);
	}

	bool OpenSocketInternal(NetSocketType module, int setPort, int defaultPort, string name, ProtocolType protocol, bool tryAny) {
		int port = setPort > 0 ? setPort : defaultPort;
		var netSock = NetSockets[(int)module];
		Socket? socket;

		if (netSock.Port <= 0) {
			socket = OpenSocket(ipname.GetString(), ref port, protocol);
			if (socket == null && tryAny) {
				port = PORT_ANY;
				socket = OpenSocket(ipname.GetString(), ref port, protocol);
			}

			if (socket == null) {
				throw new Exception();
			}

			netSock.Port = port;
			Msg($"Net.OpenSocketInternal: {name} successfully opened, local ep {socket.LocalEndPoint}\n");
		}
		else {
			Warning($"Warning: Net.OpenSockets: {name} port {netSock.Port} already open.\n");
			return false;
		}


		if (protocol == ProtocolType.Tcp) {
			netSock.TCP = socket;
		}
		else if (protocol == ProtocolType.Udp) {
			netSock.UDP = socket;
		}
		else {
			throw new Exception($"Unrecognized protocol '{protocol}'");
		}

		return netSock.Port != 0;
	}

	public void Init(bool isDedicated) {
		Time = 0;

		NetSockets.EnsureCount(NetSocket.MAX);
		NetPackets.EnsureCount(NetSocket.MAX);
		SplitPackets.EnsureCount(NetSocket.MAX);
		//SplitPackets.EnsureCount(NetSocket.MAX); // wip

		// todo: setup lagdata
		if (isDedicated)
			SetDedicated();
		else
			ConfigLoopbackBuffers(true);
	}

	private double lastRealTime = 0;
	public void SetTime(double realTime) {
		double frametime = realTime - lastRealTime;
		lastRealTime = realTime;

		if (frametime > 1.0) {
			frametime = 1.0;
		}
		else if (frametime < 0)
			frametime = 0;

		Time += frametime * 1.0; // host timescale... later
	}

	public bool NoTCP { get; set; }

	public void RunFrame(double realTime) {
		SetTime(realTime);

		if (!IsMultiplayer() || NoTCP)
			return;

		// process TCP sockets? todo
	}

	public void Shutdown() {
		Multiplayer = false;
		Dedicated = false;
		CloseAllSockets();
		ConfigLoopbackBuffers(false);
	}

	public unsafe int SendPacket(NetChannel chan, NetSocketType sock, NetAddress? to, byte[] data, int length, bf_write? voicePayload = null, bool useCompression = false) {
		int ret;
		Socket? netSocket;

		if (!IsMultiplayer() || to.Type == NetAddressType.Loopback) {
			Debug.Assert(voicePayload == null);
			SendLoopPacket(sock, length, data, to);
			return length;
		}

		if (to.Type == NetAddressType.Broadcast) {
			netSocket = NetSockets[(int)sock].UDP;
			if (netSocket == null)
				return length;
		}
		else if (to.Type == NetAddressType.IP) {
			netSocket = NetSockets[(int)sock].UDP;
			if (netSocket == null)
				return length;
		}
		else {
			Warning($"Net.SendPacket: bad address type {to.Type}\n");
			return length;
		}

		// Drop packets...
		// Fake loss...

		int gameDataLength = length;

		// Payloads....
		// Voice stuff...
		// Compression...
		// Write voice...


		int maxRoutable = MAX_ROUTABLE_PAYLOAD;
		if (chan != null) {
			maxRoutable = Math.Clamp(chan.MaxRoutablePayloadSize, MIN_USER_MAXROUTABLE_SIZE, MAX_USER_MAXROUTABLE_SIZE); // todo: sv_maxroutable?
		}

		var addr = to;
		//Console.WriteLine($"writing packet len {length}");

		//NetChannel.WritePacketToConsole((byte*)data, length);


		if (length <= maxRoutable && !(false && chan != null)) {
			// simple case, small packet, just send it
			ret = SendTo(true, netSocket, data, length, addr, gameDataLength);
		}
		else {
			// split packet into smaller pieces
			ret = SendLong(chan, sock, netSocket, data, length, addr, maxRoutable);
		}

		if (ret == -1) {
			Warning("Net.SendPacket went wrong!!!\n");
			ret = length;
		}

		return ret;
	}

	public unsafe int SendTo(bool verbose, Socket s, byte[] buf, int len, NetAddress to, int gameDataLength) {
		int send = SendToImpl(s, buf, len, to, gameDataLength);
		return send;
	}

	private unsafe int SendToImpl(Socket s, Span<byte> buf, int len, NetAddress to, int gameDataLength) {
		return s.SendTo(buf.Slice(0, len), 0, to);
	}

	private readonly int net_splitrate = 1;

	public unsafe int SendLong(NetChannel netchan, NetSocketType sock, Socket s, byte[] buf, int len, NetAddress to, int maxRoutable) {
		short splitSizeMinusHeader = (short)(maxRoutable - sizeof(SPLITPACKET));

		int sequenceNumber = -1;
		if (netchan != null) {
			sequenceNumber = netchan.IncrementSplitPacketSequence();
		}
		else {
			Debug.Assert(false, "FINISH THIS");
		}

		int packetNumber = 0;
		int totalBytesSent = 0;
		int fragmentsSent = 0;

		fixed (byte* sendbuf_u = buf) {
			sbyte* sendbuf = (sbyte*)sendbuf_u;
			int sendlen = len;

			sbyte* packetptr = stackalloc sbyte[MAX_ROUTABLE_PAYLOAD];
			sbyte* packet = packetptr;

			SPLITPACKET* pPacket = (SPLITPACKET*)packetptr;
			pPacket->NetID = SPLITPACKET_HEADER;
			pPacket->SequenceNumber = sequenceNumber;
			pPacket->SplitSize = splitSizeMinusHeader;

			int packetCount = (sendlen + splitSizeMinusHeader - 1) / splitSizeMinusHeader;

			int bytesLeft = sendlen;

			while (bytesLeft > 0) {
				int size = Math.Min(splitSizeMinusHeader, bytesLeft);
				pPacket->PacketID = (short)((packetNumber << 8) + packetCount);
				NativeMemory.Copy(sendbuf + packetNumber * splitSizeMinusHeader, packet + sizeof(SPLITPACKET), (nuint)size);
				int ret = 0;

				int len2 = size + sizeof(SPLITPACKET);
				ret = SendToImpl(s, new(packet, len2), len2, to, -1);

				++fragmentsSent;
				if (ret < 0)
					return ret;

				if (ret >= size)
					totalBytesSent += size;

				bytesLeft -= size;
				++packetNumber;

				Msg($"--> [{DescribeSocket(sock)}] Split packet {packetNumber}/{packetCount} seq {sequenceNumber} size {size} mtu {maxRoutable} to {to} [ total {sendlen} ]\n");
			}
		}

		return totalBytesSent;
	}

	public static unsafe void SendLoopPacket(NetSocketType sock, int length, byte[] data, NetAddress? to) {
		throw new NotImplementedException();
	}

	public void SendQueuedPackets() {

	}
}