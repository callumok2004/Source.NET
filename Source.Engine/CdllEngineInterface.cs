﻿using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Engine.Client;
using Source.Engine.Server;

using System.Numerics;
using System.Security.Cryptography;

namespace Source.Engine;

public class EngineClient(ClientState cl, GameServer sv, Cbuf Cbuf, Scr Scr, Con Con, IMaterialSystem materials, MaterialSystem_Config MaterialSystemConfig) : IEngineClient
{
	public ReadOnlySpan<char> Key_LookupBinding(ReadOnlySpan<char> binding) {
		return "";
	}
	public void GetUILanguage(Span<char> destination) {
		"english".CopyTo(destination);
	}

	public void GetMainMenuBackgroundName(Span<char> dest) {
		"kagami".CopyTo(dest);
	}

	public bool IsDrawingLoadingImage() => Scr.DrawLoading;

	public int GetMaxClients() => cl.MaxClients;

	public ReadOnlySpan<char> GetLevelName() {
		if (sv.IsDedicated())
			return "Dedicated Server";
		else if (!cl.IsConnected())
			return "";
		return cl.LevelFileName;
	}

	public int GetLocalPlayer() => cl.PlayerSlot + 1;
	public void ClientCmd_Unrestricted(ReadOnlySpan<char> cmdString) => Cbuf.AddText(cmdString);
	public void ExecuteClientCmd(ReadOnlySpan<char> cmdString) {
		Cbuf.AddText(cmdString);
		Cbuf.Execute();
	}

	public bool IsLevelMainMenuBackground() => sv.IsLevelMainMenuBackground();

	public bool IsPaused() => cl.IsPaused();

	public bool GetPlayerInfo(int playerIndex, out PlayerInfo playerInfo) {
		playerIndex--;
		if (playerIndex >= cl.MaxClients || playerIndex < 0) {
			playerInfo = new();
			return false;
		}

		Assert(cl.UserInfoTable != null);
		if (cl.UserInfoTable == null) {
			playerInfo = new();
			return false;
		}

		Assert(playerIndex < cl.UserInfoTable.GetNumStrings());
		if (playerIndex >= cl.UserInfoTable.GetNumStrings()) {
			playerInfo = new();
			return false;
		}

		Span<byte> pi = cl.UserInfoTable.GetStringUserData(playerIndex);
		PlayerInfo.FromBytes(pi, out playerInfo);
		return true; 
	}

	public bool Con_IsVisible() => Con.IsVisible();

	public void GetViewAngles(out QAngle viewangles) {
		viewangles = cl.ViewAngles; 
	}

	public void SetViewAngles(in QAngle viewangles) {
		cl.ViewAngles = QAngle.Normalize(in viewangles);
	}

	public void GetScreenSize(out int w, out int h) {
		// Is this even right???
		using MatRenderContextPtr renderContext = new(materials );
		renderContext.GetWindowSize(out w, out h);
	}

	readonly ILauncherManager launcherMgr = Singleton<ILauncherManager>();
	public void GetMouseDelta(out int dx, out int dy) {
		launcherMgr.GetMouseDelta(out dx, out dy);
	}

	public bool IsConnected() {
		return cl.IsConnected();
	}

	public bool IsInGame() {
		return cl.IsActive();
	}

	public double GetLastTimeStamp() {
		return cl.LastServerTickTime;
	}

	public uint GetProtocolVersion() => Protocol.VERSION;

	public SkyboxVisibility IsSkyboxVisibleFromPoint(in Vector3 point) {
		if (MaterialSystemConfig.Fullbright == 1)
			return SkyboxVisibility.Skybox3D;

		int leaf = CM.PointLeafnum(point);
		int flags = GetCollisionBSPData()!.MapLeafs[leaf].Flags;
		if ((flags & BSPFileCommon.LEAF_FLAGS_SKY) != 0)
			return SkyboxVisibility.Skybox3D;
		return ((flags & BSPFileCommon.LEAF_FLAGS_SKY2D) != 0) ? SkyboxVisibility.Skybox2D : SkyboxVisibility.NotVisible;
	}
}
