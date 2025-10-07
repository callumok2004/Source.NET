using Source.Common;
using Source.Engine.Client;
using System.Buffers;

using static Source.Constants;
using static Source.Common.Networking.Protocol;
using Source.Common.Networking;
using Source.Common.Client;
using Source.Common.Commands;
using Microsoft.Extensions.DependencyInjection;
using Source.Common.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Source.Common.Bitbuffers;

namespace Source.Engine;

/// <summary>
/// Various clientside methods. In Source, these would mostly be represented by
/// CL_MethodName's in the static global namespace
/// </summary>
public class CL(IServiceProvider services, Net Net,
	ClientGlobalVariables clientGlobalVariables, ServerGlobalVariables serverGlobalVariables,
	CommonHostState host_state, Host Host, Cbuf Cbuf, IEngineVGuiInternal? EngineVGui, Scr Scr, Shader Shader, ClientDLL ClientDLL, EngineRecvTable RecvTable)
{
	public IPrediction ClientSidePrediction => ClientDLL.ClientSidePrediction;
	public IClientEntityList EntityList => ClientDLL.EntityList;
	public ICenterPrint CenterPrint => ClientDLL.CenterPrint;
	public IClientLeafSystemEngine ClientLeafSystem => ClientDLL.ClientLeafSystem;


	public ClientState cl;
	public IBaseClientDLL clientDLL;
	public IEngineClient engineClient;
	public void ApplyAddAngle() {

	}

	public void CheckClientState() {

	}

	public void ExtraMouseUpdate(double frameTime) {
		if (!cl.IsActive())
			return;

		if (!Host.ShouldRun())
			return;

		clientDLL.ExtraMouseSample(frameTime, !cl.Paused);
	}

	public void Init() {
		cl = services.GetRequiredService<ClientState>();
		cl.Clear();

		clientDLL = services.GetRequiredService<IBaseClientDLL>();
		engineClient = services.GetRequiredService<IEngineClient>();
	}

	public void ReadPackets(bool finalTick) {
		//if(!Host.ShouldRun) return;

		cl.OldTickCount = cl.GetServerTickCount();
		if (!cl.IsPaused()) {
			cl.SetClientTickCount(cl.GetClientTickCount() + 1);

			if (!ClockDriftMgr.Enabled)
				cl.SetServerTickCount(cl.GetClientTickCount());

			clientGlobalVariables.TickCount = cl.GetClientTickCount();
			clientGlobalVariables.CurTime = cl.GetTime();
		}
		clientGlobalVariables.FrameTime = cl.GetFrameTime();

		Net.ProcessSocket(NetSocketType.Client, cl);
	}

	public void RunPrediction(PredictionReason reason) {

	}

	public void SendMove() {
		byte[] data = ArrayPool<byte>.Shared.Rent(MAX_CMD_BUFFER);
		{
			int nextcommandnr = cl.LastOutgoingCommand + cl.ChokedCommands + 1;
			CLC_Move move = new();

			move.DataOut.StartWriting(data, data.Length, 0);
			int cl_cmdbackup = 2;
			move.BackupCommands = Math.Clamp(cl_cmdbackup, 0, MAX_BACKUP_COMMANDS);
			move.NewCommands = 1 + cl.ChokedCommands;
			move.NewCommands = Math.Clamp(move.NewCommands, 0, MAX_NEW_COMMANDS);
			int numcmds = move.NewCommands + move.BackupCommands;
			int from = -1;
			bool ok = true;
			for (int to = nextcommandnr - numcmds + 1; to <= nextcommandnr; to++) {
				bool isnewcmd = to >= (nextcommandnr - move.NewCommands + 1);
				ok = ok && clientDLL.WriteUsercmdDeltaToBuffer(move.DataOut, from, to, isnewcmd);
				from = to;
			}

			if (ok)
				cl.NetChannel?.SendNetMsg(move);
		}
		ArrayPool<byte>.Shared.Return(data, true);
	}

	public void Move(double accumulatedExtraSamples, bool finalTick) {
		if (!cl.IsConnected())
			return;

		bool sendPacket = true;

		if (Net.Time < cl.NextCmdTime || !cl.NetChannel.CanSendPacket())
			sendPacket = false;

		if (cl.IsActive()) {
			int nextcmdnr = cl.LastOutgoingCommand + cl.ChokedCommands + 1;
			clientDLL.CreateMove(nextcmdnr, host_state.IntervalPerTick - accumulatedExtraSamples, !cl.IsPaused());

			if (sendPacket)
				SendMove();
			else {
				cl.NetChannel?.SetChoked();
				cl.ChokedCommands++;
			}
		}

		if (!sendPacket)
			return;

		bool hasProblem = cl.NetChannel.IsTimingOut && cl.IsActive();

		if (cl.IsActive()) {
			NET_Tick mymsg = new NET_Tick(cl.DeltaTick, (float)Host.FrameTimeUnbounded, (float)Host.FrameTimeStandardDeviation);
			cl.NetChannel?.SendNetMsg(mymsg);
		}

		cl.LastOutgoingCommand = cl.NetChannel.SendDatagram(null);
		cl.ChokedCommands = 0;

		if (cl.IsActive()) {
			const int cl_cmdrate = 66;
			float commandInterval = 1.0f / cl_cmdrate;
			float maxDelta = Math.Min((float)host_state.IntervalPerTick, commandInterval);
			float delta = Math.Clamp((float)(Net.Time - cl.NextCmdTime), 0, maxDelta);
			cl.NextCmdTime = Net.Time + commandInterval - delta;
		}
		else {
			cl.NextCmdTime = Net.Time + (1f / 5f);
		}
	}

	public void FullyConnected() {
		EngineVGui?.UpdateProgressBar(LevelLoadingProgress.FullyConnected);
		// Static prop manager level init client
		// Flush dynamic models
		// Purge unused models
		// Shutdown preload data
		// Pending pure file reloads
		// Level init post entity

		// Start notifying dependencies
		int ip = 0;
		short port = 0;
		int queryPort = 0;
		// TODO ^^^^^^^ requires some changes in netchannels.
		EngineVGui!.NotifyOfServerConnect(Common.Gamedir, ip, port, queryPort);
		EngineVGui!.UpdateProgressBar(LevelLoadingProgress.ReadyToPlay);
		// MDL cache end map load

		Scr.EndLoadingPlaque();
		// EndLoadingUpdates();
	}

	public void Connect(string address, string sourceTag) {
		if (!address.Equals("localhost", StringComparison.OrdinalIgnoreCase)) {
			Host.Disconnect(false);
			Net.SetMultiplayer(true);
			EngineVGui?.EnabledProgressBarForNextLoad();
			Scr.BeginLoadingPlaque();
			EngineVGui?.UpdateProgressBar(LevelLoadingProgress.BeginConnect);
		}
		else {
			cl.Disconnect("Connecting to local host", false);
		}

		cl.Connect(address, sourceTag);
	}

	[ConCommand]
	public void connect(in TokenizedCommand cmd) {
		var splits = cmd.ArgS().Split(' ');
		if (splits.Length == 1) {
			Connect(splits[0], "");
		}
		else if (splits.Length == 2) {
			Connect(splits[0], splits[1]);
		}
		else {
			Dbg.ConMsg("Usage:  connect <server>\n");
		}
	}

	[ConCommand(helpText: "Retry connection to last server.")]
	void retry() {
		Retry();
	}

	public void Retry() {
		if (cl == null) return;

		if (cl.RetryAddress == null) {
			Dbg.ConMsg("Can't retry, no previous connection\n");
			return;
		}

		bool canAddExecutionMarkers = Cbuf.HasRoomForExecutionMarkers(2);
		Dbg.ConMsg($"Commencing connection retry to {cl.RetryAddress}\n");

		ReadOnlySpan<char> command = $"connect {cl.RetryAddress} {cl.RetryChallenge}\n";
		if (cl.RestrictServerCommands && canAddExecutionMarkers)
			Cbuf.AddTextWithMarkers(CmdExecutionMarker.DisableServerCanExecute, command, CmdExecutionMarker.EnableServerCanExecute);
		else
			Cbuf.AddText(command);
	}

	internal void ProcessVoiceData() {

	}

	internal void TakeSnapshotAndSwap() {
		Shader.SwapBuffers();
	}

	public void DumpStringTables() => cl.StringTableContainer?.Dump();

	internal bool CheckCRCs(ReadOnlySpan<char> levelFileName) {
		return true;
	}

	internal void RegisterResources() {
		host_state.SetWorldModel(cl.GetModel(1));
		if (host_state.WorldModel == null)
			Host.Error("CL.RegisterResources: host_state.WorldModel/cl.GetModel(1) == NULL\n");
	}

	public ref PrecacheUserData GetPrecacheUserData(INetworkStringTable table, int index) {
		Span<byte> d = table.GetStringUserData(index);
		ref PrecacheUserData data = ref MemoryMarshal.Cast<byte, PrecacheUserData>(d)[0];
		if (!Unsafe.IsNullRef(ref data) && d.Length != Unsafe.SizeOf<PrecacheUserData>())
			Error($"CL.GetPrecacheUserData({table.GetTableId()}, {index}) - length ({d.Length}) invalid.");

		return ref data;
	}

	public int PropsDecoded;

	public bool ProcessPacketEntities(svc_PacketEntities entmsg) {
		ClientFrame newFrame = cl.AllocateFrame();
		newFrame.Init(cl.GetServerTickCount());
		ClientFrame? oldFrame = null;

		if (entmsg.IsDelta) {
			int deltaTicks = cl.GetServerTickCount() - entmsg.DeltaFrom;

			if (cl.GetServerTickCount() == entmsg.DeltaFrom) {
				Host.Error("Update self-referencing, connection dropped.\n");
				return false;
			}

			oldFrame = cl.GetClientFrame(entmsg.DeltaFrom);

			if (oldFrame == null) {
				FlushEntityPacket(newFrame, "Update delta not found.\n");
				return false;
			}
		}
		else {
			for (int i = 0; i <= ClientDLL.EntityList.GetHighestEntityIndex(); i++) {
				DeleteDLLEntity(i, "ProcessPacketEntities", true);
			}
		}

		ClientDLL.FrameStageNotify(ClientFrameStage.NetUpdateStart);

		PropsDecoded = 0;

		Assert(entmsg.Baseline >= 0 && entmsg.Baseline < 2);

		if (entmsg.UpdateBaseline) {
			int updateBaseline = (entmsg.Baseline == 0) ? 1 : 0;
			cl.CopyEntityBaseline(entmsg.Baseline, updateBaseline);

			var msg = new clc_BaselineAck(cl.GetServerTickCount(), entmsg.Baseline);
			cl.NetChannel!.SendNetMsg(msg, true);
		}

		EntityReadInfo readInfo = EntityReadInfo.Alloc();
		readInfo.Buf = entmsg.DataIn;
		readInfo.From = oldFrame;
		readInfo.To = newFrame;
		readInfo.AsDelta = entmsg.IsDelta;
		readInfo.HeaderCount = entmsg.UpdatedEntries;
		readInfo.Baseline = entmsg.Baseline;
		readInfo.UpdateBaselines = entmsg.UpdateBaseline;

		cl.ReadPacketEntities(readInfo);

		ClientDLL.FrameStageNotify(ClientFrameStage.NetUpdatePostDataUpdateStart);

		CallPostDataUpdates(readInfo);

		ClientDLL.FrameStageNotify(ClientFrameStage.NetUpdatePostDataUpdateEnd);

		MarkEntitiesOutOfPVS(ref newFrame.TransmitEntity);

		cl.NetChannel?.UpdateMessageStats(NetChannelGroup.LocalPlayer, readInfo.LocalPlayerBits);
		cl.NetChannel?.UpdateMessageStats(NetChannelGroup.OtherPlayers, readInfo.OtherPlayerBits);
		cl.NetChannel?.UpdateMessageStats(NetChannelGroup.Entities, -(readInfo.LocalPlayerBits + readInfo.OtherPlayerBits));

		cl.DeleteClientFrames(entmsg.DeltaFrom);

		if (ClientFrame.MAX_CLIENT_FRAMES < cl.AddClientFrame(newFrame))
			DevMsg(1, "CL.ProcessPacketEntities: frame window too big (>%i)\n", ClientFrame.MAX_CLIENT_FRAMES);

		ClientDLL.FrameStageNotify(ClientFrameStage.NetUpdateEnd);

		EntityReadInfo.Free(readInfo);

		return true;
	}

	private void CallPostDataUpdates(EntityReadInfo u) {
		for (int i = 0; i < u.NumPostDataUpdateCalls; i++) {
			ref PostDataUpdateCall call = ref u.PostDataUpdateCalls[i];

			IClientNetworkable? ent = EntityList.GetClientNetworkable(call.Ent);
			ErrorIfNot(ent != null, $"CL_CallPostDataUpdates: missing ent {call.Ent}");

			ent.PostDataUpdate(call.UpdateType);
		}
	}

	private void MarkEntitiesOutOfPVS(ref MaxEdictsBitVec pvsFlags) {
		int highest_index = EntityList.GetHighestEntityIndex();
		for (int i = 0; i <= highest_index; i++) {
			IClientNetworkable? ent = EntityList.GetClientNetworkable(i);
			if (ent == null)
				continue;

			bool curstate = !ent.IsDormant();
			bool newstate = pvsFlags.Get(i) != 0 ? true : false;

			if (!curstate && newstate)
				ent.NotifyShouldTransmit(ShouldTransmiteState.Start);
			else if (curstate && !newstate) {
				ent.NotifyShouldTransmit(ShouldTransmiteState.End);
				RecordLeavePVS(i);
			}
		}
	}

	private void RecordLeavePVS(int i) {
		// throw new NotImplementedException();
	}

	public void DeleteDLLEntity(int entIndex, ReadOnlySpan<char> reason, bool onRecreatingAllEntities = false) {
		IClientNetworkable? net = EntityList.GetClientNetworkable(entIndex);

		if (net != null) {
			ClientClass clientClass = net.GetClientClass();

			RecordDeleteEntity(entIndex, clientClass);
			if (onRecreatingAllEntities)
				net.SetDestroyedOnRecreateEntities();

			net.Release();
		}
	}

	private void RecordDeleteEntity(int entIndex, ClientClass clientClass) {

	}

	private void FlushEntityPacket(ClientFrame newFrame, string v) {
		throw new NotImplementedException();
	}

	internal void PreprocessEntities() {
		bool isUsingMultiplayerNetworking = Net.IsMultiplayer();
		bool lastOutgoingCommandEqualsLastAcknowledgedCommand = cl.LastOutgoingCommand == cl.CommandAck;

		if (isUsingMultiplayerNetworking || lastOutgoingCommandEqualsLastAcknowledgedCommand)
			RunPrediction(PredictionReason.SimulationResultsArrivingOnSendFrame);

		int number_of_commands_executed = cl.CommandAck - cl.LastCommandAck;
		ClientSidePrediction.PreEntityPacketReceived(number_of_commands_executed, cl.CurrentSequence);
	}

	internal bool DetermineUpdateType(EntityReadInfo u) {
		if (!u.IsEntity || (u.NewEntity > u.OldEntity)) {
			if (u.From == null || (u.OldEntity > u.From.LastEntity)) {
				Assert(!u.IsEntity);
				u.UpdateType = UpdateType.Finished;
				return false;
			}

			u.UpdateType = UpdateType.PreserveEnt;
		}
		else {
			if ((u.UpdateFlags & DeltaEncodingFlags.EnterPVS) != 0)
				u.UpdateType = UpdateType.EnterPVS;
			else if ((u.UpdateFlags & DeltaEncodingFlags.LeavePVS) != 0)
				u.UpdateType = UpdateType.LeavePVS;
			else
				u.UpdateType = UpdateType.DeltaEnt;
		}

		return true;
	}

	public void ParseDeltaHeader(EntityReadInfo u) {
		u.UpdateFlags = DeltaEncodingFlags.Zero;

		u.NewEntity = (int)(u.HeaderBase + 1 + u.Buf!.ReadUBitVar());
		u.HeaderBase = u.NewEntity;

		if (u.Buf!.ReadOneBit() == 0) {
			if (u.Buf!.ReadOneBit() != 0)
				u.UpdateFlags |= DeltaEncodingFlags.EnterPVS;
		}
		else {
			u.UpdateFlags |= DeltaEncodingFlags.LeavePVS;

			if (u.Buf!.ReadOneBit() != 0)
				u.UpdateFlags |= DeltaEncodingFlags.Delete;
		}
	}

	internal void CopyNewEntity(EntityReadInfo u, int iClass, int iSerialNum) {
		if (u.NewEntity < 0 || u.NewEntity >= MAX_EDICTS) {
			Host.Error("CL.CopyNewEntity: u.m_nNewEntity < 0 || m_nNewEntity >= MAX_EDICTS");
			return;
		}

		IClientNetworkable? ent = EntityList.GetClientNetworkable(u.NewEntity);

		if (iClass >= cl.NumServerClasses) {
			Host.Error($"CL.CopyNewEntity: invalid class index ({iClass}).\n");
			return;
		}

		ClientClass? pClass = cl.ServerClasses[iClass]?.ClientClass;
		bool bNew = false;
		if (ent != null) {
			if (ent.GetIClientUnknown()!.GetRefEHandle()!.GetSerialNumber() != iSerialNum) {
				DeleteDLLEntity(u.NewEntity, "CopyNewEntity");
				ent = null;
			}
		}

		if (ent == null) {
			ent = CreateDLLEntity(u.NewEntity, iClass, iSerialNum);
			if (ent == null) {
				ReadOnlySpan<char> networkName = cl.ServerClasses[iClass]?.ClientClass?.NetworkName ?? "";
				clientDLL.ErrorCreatingEntity(u.NewEntity, iClass, iSerialNum);
				Host.Error($"CL.CopyNewEntity: Error creating entity {networkName}({u.NewEntity})\n");
				return;
			}

			bNew = true;
		}

		int start_bit = u.Buf!.BitsRead;

		DataUpdateType updateType = bNew ? DataUpdateType.Created : DataUpdateType.DataTableChanged;
		ent.PreDataUpdate(updateType);

		byte[]? fromData;
		int fromBits;

		PackedEntity? baseline = u.AsDelta ? cl.GetEntityBaseline(u.Baseline, u.NewEntity) : null;
		if (baseline != null && baseline.ClientClass == pClass) {
			Assert(!baseline.IsCompressed());
			fromData = baseline.GetData();
			fromBits = baseline.GetNumBits();
		}
		else {
			ErrorIfNot(cl.GetClassBaseline(iClass, out fromData, out fromBits), $"CL.CopyNewEntity: GetClassBaseline({iClass}) failed.");
			fromBits *= 8;
		}

		bf_read fromBuf = new("CL.CopyNewEntity->fromBuf", fromData, NetChannel.Bits2Bytes(fromBits), fromBits);

		RecvTable? recvTable = GetEntRecvTable(u.NewEntity);

		if (recvTable == null)
			Host.Error($"CL.CopyNewEntity: invalid recv table for ent {u.NewEntity}.\n");

		if (u.UpdateBaselines) {
			byte[] packedData = ArrayPool<byte>.Shared.Rent(MAX_PACKEDENTITY_DATA);
			bf_write writeBuf = new(packedData, packedData.Length);

			RecvTable.MergeDeltas(recvTable, fromBuf, u.Buf, writeBuf, -1, null, true);

			cl.SetEntityBaseline((u.Baseline == 0) ? 1 : 0, pClass, u.NewEntity, packedData, writeBuf.BytesWritten);

			fromBuf.StartReading(packedData, writeBuf.BytesWritten);

			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), fromBuf, u.NewEntity, false);
			ArrayPool<byte>.Shared.Return(packedData, true);
		}
		else {
			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), fromBuf, u.NewEntity, false);
			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), u.Buf, u.NewEntity, true);
		}

		AddPostDataUpdateCall(u, u.NewEntity, updateType);

		Assert(u.To!.LastEntity <= u.NewEntity);
		u.To!.LastEntity = u.NewEntity;
		u.To!.TransmitEntity.Set(u.NewEntity);

		int bit_count = u.Buf.BitsRead - start_bit;
		// if (cl_entityreport.GetBool())
		// CL.RecordEntityBits(u.NewEntity, bit_count);

		if (IsPlayerIndex(u.NewEntity)) {
			if (u.NewEntity == cl.PlayerSlot + 1)
				u.LocalPlayerBits += bit_count;
			else
				u.OtherPlayerBits += bit_count;
		}
	}

	private RecvTable? GetEntRecvTable(int entityNum) {
		IClientNetworkable? networkable = EntityList.GetClientNetworkable(entityNum);
		return networkable?.GetClientClass().RecvTable;
	}

	private IClientNetworkable? CreateDLLEntity(int iEnt, int iClass, int iSerialNum) {
		ClientClass? clientClass;
		if ((clientClass = cl.ServerClasses[iClass]?.ClientClass) != null) {
			if (!cl.IsActive())
				Common.TimestampedLog($"cl:  create '{clientClass.NetworkName}'\n");

			return clientClass.CreateFn(iEnt, iSerialNum);
		}

		Assert(false);
		return null;
	}

	public bool IsPlayerIndex(int index) {
		return (index >= 1 && index <= cl.MaxClients);
	}

	private void AddPostDataUpdateCall(EntityReadInfo u, int entIdx, DataUpdateType updateType) {
		ErrorIfNot(u.NumPostDataUpdateCalls < MAX_EDICTS, "CL_AddPostDataUpdateCall: overflowed u.m_PostDataUpdateCalls");

		u.PostDataUpdateCalls[u.NumPostDataUpdateCalls].Ent = entIdx;
		u.PostDataUpdateCalls[u.NumPostDataUpdateCalls].UpdateType = updateType;
		++u.NumPostDataUpdateCalls;
	}

	internal void CopyExistingEntity(EntityReadInfo u) {
		int start_bit = u.Buf!.BitsRead;

		IClientNetworkable? ent = EntityList.GetClientNetworkable(u.NewEntity);
		if (ent == null) {
			Host.Error($"CL.CopyExistingEntity: missing client entity {u.NewEntity}.\n");
			return;
		}

		ent.PreDataUpdate(DataUpdateType.DataTableChanged);
		RecvTable? recvTable = GetEntRecvTable(u.NewEntity);

		if (recvTable == null) {
			Host.Error($"CL.CopyExistingEntity: invalid recv table for ent {u.NewEntity}.\n");
			return;
		}

		RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), u.Buf, u.NewEntity);

		AddPostDataUpdateCall(u, u.NewEntity, DataUpdateType.DataTableChanged);

		u.To!.LastEntity = u.NewEntity;
		u.To.TransmitEntity.Set(u.NewEntity);

		int bit_count = u.Buf.BitsRead - start_bit;

		//  if (cl_entityreport.GetBool())
		//  	CL_RecordEntityBits(u.m_nNewEntity, bit_count);

		if (IsPlayerIndex(u.NewEntity)) {
			if (u.NewEntity == cl.PlayerSlot + 1)
				u.LocalPlayerBits += bit_count;
			else
				u.OtherPlayerBits += bit_count;
		}
	}

	internal void PreserveExistingEntity(int oldEntity) {
		IClientNetworkable? pEnt = EntityList.GetClientNetworkable(oldEntity);
		if (pEnt == null) {
			Host.Error($"CL.PreserveExistingEntity: missing client entity {oldEntity}.\n");
			return;
		}

		pEnt.OnDataUnchangedInPVS();
	}
}

/// <summary>
/// Loads and shuts down the client DLL
/// </summary>
/// <param name="services"></param>
public class ClientDLL(IServiceProvider services, Sys Sys, EngineRecvTable RecvTable)
{
	public IBaseClientDLL clientDLL;
	public IPrediction ClientSidePrediction;
	public IClientEntityList EntityList;
	public ICenterPrint CenterPrint;
	public IClientLeafSystemEngine ClientLeafSystem;
	public void Init() {
		clientDLL = services.GetRequiredService<IBaseClientDLL>();

		if (!clientDLL.Init())
			Sys.Error("Client.dll Init() in library client failed.");

		ClientSidePrediction = services.GetRequiredService<IPrediction>();
		EntityList = services.GetRequiredService<IClientEntityList>();
		CenterPrint = services.GetRequiredService<ICenterPrint>();
		ClientLeafSystem = services.GetRequiredService<IClientLeafSystemEngine>();

		InitRecvTableMgr();
	}

	private void InitRecvTableMgr() {
		RecvTable?[] recvTables = new RecvTable[MAX_DATATABLES];
		int nRecvTables = 0;
		for (ClientClass? cur = GetAllClasses(); cur != null; cur = cur.Next) {
			ErrorIfNot(nRecvTables < MAX_DATATABLES, "ClientDLL_InitRecvTableMgr: overflowed MAX_DATATABLES");
			recvTables[nRecvTables++] = cur.RecvTable;
		}

		RecvTable.Init(recvTables.AsSpan()[..nRecvTables]!); // << ! is acceptable here; anything beyond recvTables is null, anything before it shouldnt be
															 // (and if something is null before that point something else is already horribly broken)
	}

	public void Update() {

	}

	public void ProcessInput() {

	}

	public void FrameStageNotify(ClientFrameStage stage) {
		clientDLL.FrameStageNotify(stage);
	}

	public ClientClass? GetAllClasses() {
		return clientDLL != null ? clientDLL.GetAllClasses() : ClientClass.Head;
	}
}
