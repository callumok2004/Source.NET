﻿using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Engine;
using Source.Engine.Server;
using Source.GUI;
using Source.GUI.Controls;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Engine;

public enum LevelLoadingProgress
{
	None,
	ChangeLevel,
	SpawnServer,
	LoadWorldModel,
	CrcMap,
	CrcClientDll,
	CreateNetworkStringTables,
	PrecacheWorld,
	ClearWorld,
	LevelInit,
	Precache,
	ActivateServer,

	BeginConnect,
	SignOnChallenge,
	SignOnConnect,
	SignOnConnected,
	ProcessServerInfo,
	ProcessStringTable,
	SignOnNew,
	SendClientInfo,
	SendSignonData,
	SignOnSpawn,
	FullyConnected,
	ReadyToPlay
}

public struct LoadingProgressDescription
{
	public LevelLoadingProgress Progress;
	public int Percent;
	public int Repeat;
	public string Description;

	public LoadingProgressDescription(LevelLoadingProgress progress, int percent, int repeat, ReadOnlySpan<char> desc) {
		Progress = progress;
		Percent = percent;
		Repeat = repeat;
		Description = new(desc);
	}
}

public interface IEngineVGuiInternal : IEngineVGui
{
	// Level loading
	public void OnLevelLoadingStarted();
	public void OnLevelLoadingFinished();
	public void NotifyOfServerConnect(ReadOnlySpan<char> game, int IP, int connectionPort, int queryPort);
	public void NotifyOfServerDisconnect();
	public void EnabledProgressBarForNextLoad();
	public void UpdateProgressBar(LevelLoadingProgress progress);
	public void UpdateCustomProgressBar(float progress, ReadOnlySpan<char> desc);
	public void StartCustomProgress();
	public void FinishCustomProgress();
	public void ShowErrorMessage();
	void Simulate();
	void Paint(PaintMode paintMode);
}

public class EnginePanel(Panel? parent, string name) : EditablePanel(parent, name)
{

}


public class StaticPanel(Panel? parent, string name) : Panel(parent, name)
{

}


public class EngineVGui(
	Sys Sys, Net Net, IEngineAPI engineAPI, ISurface surface,
	IMaterialSystem materials, ILauncherManager launcherMgr,
	ICommandLine CommandLine, IFileSystem fileSystem, GameServer sv, Cbuf Cbuf, Sound Sound, Host Host
	) : IEngineVGuiInternal
{
	public static LoadingProgressDescription[] ListenServerLoadingProgressDescriptions = [

		new(LevelLoadingProgress.None,                          0,      0,      null),
		new(LevelLoadingProgress.SpawnServer,                   2,      0,      "#LoadingProgress_SpawningServer"),
		new(LevelLoadingProgress.LoadWorldModel,                4,      7,      "#LoadingProgress_LoadMap"),
		new(LevelLoadingProgress.CreateNetworkStringTables,     23,     0,      null),
		new(LevelLoadingProgress.PrecacheWorld,                 23,     0,      "#LoadingProgress_PrecacheWorld"),
		new(LevelLoadingProgress.ClearWorld,                    23,     0,      null),
		new(LevelLoadingProgress.LevelInit,                     34,     0,      "#LoadingProgress_LoadResources"),
		new(LevelLoadingProgress.Precache,                      35,     239,    null),
		new(LevelLoadingProgress.ActivateServer,                68,     0,      null),
		new(LevelLoadingProgress.SignOnChallenge,               68,     0,      null),
		new(LevelLoadingProgress.SignOnConnect,                 70,     0,      null),
		new(LevelLoadingProgress.SignOnConnected,               73,     0,      "#LoadingProgress_SignonLocal"),
		new(LevelLoadingProgress.ProcessServerInfo,             75,     0,      null),
		new(LevelLoadingProgress.ProcessStringTable,            77,     12,     null),	// 16
		new(LevelLoadingProgress.SignOnNew,                     84,     0,      null),
		new(LevelLoadingProgress.SendClientInfo,                88,     0,      null),
		new(LevelLoadingProgress.SendSignonData,                91,     0,      "#LoadingProgress_SignonDataLocal"),
		new(LevelLoadingProgress.SignOnSpawn,                   94,     0,      null),
		new(LevelLoadingProgress.FullyConnected,                97,     0,      null),
		new(LevelLoadingProgress.ReadyToPlay,                   99,     0,      null),
	];
	public static LoadingProgressDescription[] RemoteConnectLoadingProgressDescriptions = [
		new(LevelLoadingProgress.None,                          0,      0,      null),
		new(LevelLoadingProgress.ChangeLevel,                   1,      0,      "#LoadingProgress_Changelevel"),
		new(LevelLoadingProgress.BeginConnect,                  5,      0,      "#LoadingProgress_BeginConnect"),
		new(LevelLoadingProgress.SignOnChallenge,               10,     0,      "#LoadingProgress_Connecting"),
		new(LevelLoadingProgress.SignOnConnected,               15,     0,      null),
		new(LevelLoadingProgress.ProcessServerInfo,             20,     0,      "#LoadingProgress_ProcessServerInfo"),
		new(LevelLoadingProgress.CreateNetworkStringTables,     25,     11,     null),
		new(LevelLoadingProgress.LoadWorldModel,                45,     7,      "#LoadingProgress_LoadMap"),
		new(LevelLoadingProgress.SignOnNew,                     75,     0,      null),
		new(LevelLoadingProgress.SendClientInfo,                80,     0,      "#LoadingProgress_SendClientInfo"),
		new(LevelLoadingProgress.SendSignonData,                85,     0,      "#LoadingProgress_SignonData"),
		new(LevelLoadingProgress.SignOnSpawn,                   90,     0,      null),
		new(LevelLoadingProgress.FullyConnected,                95,     0,      null),
		new(LevelLoadingProgress.ReadyToPlay,                   99,     0,      null),
	];

	LoadingProgressDescription[]? activeDescriptions = null;


	Common Common;
	IGameUI staticGameUIFuncs;
	IGameConsole staticGameConsole;
	IGame game;
	ISurface matSystemSurface;
	IEngineClient engineClient;
	ILocalize localize;
	IVGui vgui;
	ISchemeManager vguiScheme;
	StaticPanel staticPanel;
	EnginePanel staticClientDLLPanel;
	EnginePanel staticClientDLLToolsPanel;
	EnginePanel staticGameUIPanel;
	EnginePanel staticGameDLLPanel;

	EnginePanel staticEngineToolsPanel;

	bool ShowProgressDialog;
	LevelLoadingProgress LastProgressPoint;
	int LastProgressPointRepeatCount;
	double LoadingStartTime;
	struct LoadingProgressEntry
	{
		public double Time;
		public LevelLoadingProgress Progress;
	}
	List<LoadingProgressEntry> LoadingProgress = [];
	bool SaveProgress;
	bool NoShaderAPI;
	double ProgressBias;

	LoadingProgressDescription def = new(LevelLoadingProgress.None, 0, 0, null);
	public ref LoadingProgressDescription GetProgressDescription(LevelLoadingProgress progress) {
		if (activeDescriptions == null) {
			return ref def;
		}

		int i = 0;
		while (i < activeDescriptions.Length) {
			ref LoadingProgressDescription desc = ref activeDescriptions[i];
			if (desc.Progress >= progress)
				return ref desc;

			i++;
		}

		return ref activeDescriptions[0];
	}

	public void OnLevelLoadingStarted() {
		LoadingStartTime = Sys.Time;
		LoadingProgress.Clear();
		LastProgressPoint = LevelLoadingProgress.None;
		LastProgressPointRepeatCount = 0;
		ProgressBias = 0;

		if (Net.IsMultiplayer())
			activeDescriptions = RemoteConnectLoadingProgressDescriptions;
		else
			activeDescriptions = ListenServerLoadingProgressDescriptions;

		ShowProgressDialog = false;
	}
	public void OnLevelLoadingFinished() {
		LastProgressPoint = LevelLoadingProgress.None;
		Sys.ExtendedError = false;
		Sys.DisconnectReason = null;
		Sys.ExtendedDisconnectReason = null;
	}
	public void NotifyOfServerConnect(ReadOnlySpan<char> game, int IP, int connectionPort, int queryPort) { }
	public void NotifyOfServerDisconnect() { }
	public void EnabledProgressBarForNextLoad() { }
	public void UpdateProgressBar(LevelLoadingProgress progress) {
		if (!Sys.InMainThread())
			return;

		if (activeDescriptions == null)
			return;

		if (progress < LastProgressPoint)
			return;

		if (progress == LastProgressPoint)
			++LastProgressPointRepeatCount;
		else
			LastProgressPointRepeatCount = 0;

		ref LoadingProgressDescription desc = ref GetProgressDescription(progress);
		double perc = desc.Percent / 100d;
		if (desc.Repeat > 1 && LastProgressPointRepeatCount > 0) {
			LastProgressPointRepeatCount = Math.Min(LastProgressPointRepeatCount, desc.Repeat);
			double nextPerc = GetProgressDescription(progress + 1).Percent / 100d;
			perc += (nextPerc - perc) * ((float)LastProgressPointRepeatCount / desc.Repeat);
		}

		perc = perc * (1.0f - ProgressBias) + ProgressBias;
		if (staticGameUIFuncs.UpdateProgressBar((float)perc, desc.Description)) {
			Host.View.RenderGuiOnly();
		}
		LastProgressPoint = progress;
	}
	public void UpdateCustomProgressBar(float progress, ReadOnlySpan<char> desc) { }
	public void StartCustomProgress() { }
	public void FinishCustomProgress() { }
	public void ShowErrorMessage() { }

	public bool IsGameUIVisible() {
		throw new NotImplementedException();
	}

	public void VGui_PlaySound(ReadOnlySpan<char> fileName) {
		Vector3 dummyOrigin = new(0);
		// do this later
	}

	IBaseClientDLL clientDLL;
	public void Init() {
		// Load gameui
		Common = engineAPI.GetRequiredService<Common>();
		matSystemSurface = engineAPI.GetRequiredService<ISurface>();
		game = engineAPI.GetRequiredService<IGame>();
		staticGameUIFuncs = engineAPI.GetRequiredService<IGameUI>();
		engineClient = engineAPI.GetRequiredService<IEngineClient>();
		vgui = engineAPI.GetRequiredService<IVGui>();
		clientDLL = engineAPI.GetRequiredService<IBaseClientDLL>();
		vguiScheme = engineAPI.GetRequiredService<ISchemeManager>();
		localize = engineAPI.GetRequiredService<ILocalize>();
		vguiScheme.Init();
		// IGameConsole, but later.

		Span<char> lang = stackalloc char[64];
		engineClient.GetUILanguage(lang);

		matSystemSurface.InstallPlaySoundFunc(VGui_PlaySound);

		ReadOnlySpan<char> str = "Resource/SourceScheme.res";
		if (vguiScheme.LoadSchemeFromFile(str, "Tracker") == null) {
			Sys.Error($"Error loading file {str}\n");
			return;
		}

		// Try loading ClientScheme
		vguiScheme.LoadSchemeFromFile("Resource/ClientScheme.res", "ClientScheme");

		// Ideal hierarchy:

		// Root -- staticPanel
		//		staticBackgroundImagePanel (from gamui) zpos == 0
		//      staticClientDLLPanel ( zpos == 25 )
		//		staticClientDLLToolsPanel ( zpos == 28
		//		staticGameDLLPanel ( zpos == 30 )
		//		staticEngineToolsPanel ( zpos == 75 )
		//		staticGameUIPanel ( GameUI stuff ) ( zpos == 100 )
		//		staticDebugSystemPanel ( Engine debug stuff ) zpos == 125 )

		int w = 0, h = 0;
		launcherMgr.RenderedSize(false, ref w, ref h);

		staticPanel = engineAPI.New<StaticPanel>(null, "staticPanel");
		staticPanel.SetBounds(0, 0, w, h);
		staticPanel.SetPaintBorderEnabled(false);
		staticPanel.SetPaintBackgroundEnabled(false);
		staticPanel.SetPaintEnabled(false);
		staticPanel.SetVisible(true);
		staticPanel.SetCursor(CursorCode.None);
		staticPanel.SetZPos(0);
		staticPanel.SetVisible(true);
		staticPanel.SetParent(matSystemSurface.GetEmbeddedPanel());

		staticClientDLLPanel = engineAPI.New<EnginePanel>(staticPanel, "staticClientDLLPanel");
		staticClientDLLPanel.SetBounds(0, 0, w, h);
		staticClientDLLPanel.SetPaintBorderEnabled(false);
		staticClientDLLPanel.SetPaintBackgroundEnabled(false);
		staticClientDLLPanel.SetKeyboardInputEnabled(false);
		staticClientDLLPanel.SetPaintEnabled(false);
		staticClientDLLPanel.SetVisible(false);
		staticClientDLLPanel.SetCursor(CursorCode.None);
		staticClientDLLPanel.SetZPos(25);

		CreateAskConnectPanel(staticPanel);

		staticClientDLLToolsPanel = engineAPI.New<EnginePanel>(staticPanel, "staticClientDLLToolsPanel");
		staticClientDLLToolsPanel.SetBounds(0, 0, w, h);
		staticClientDLLToolsPanel.SetPaintBorderEnabled(false);
		staticClientDLLToolsPanel.SetPaintBackgroundEnabled(false);
		staticClientDLLToolsPanel.SetKeyboardInputEnabled(false);
		staticClientDLLToolsPanel.SetPaintEnabled(false);
		staticClientDLLToolsPanel.SetVisible(true);
		staticClientDLLToolsPanel.SetCursor(CursorCode.None);
		staticClientDLLToolsPanel.SetZPos(28);

		staticEngineToolsPanel = engineAPI.New<EnginePanel>(staticPanel, "Engine Tools");
		staticEngineToolsPanel.SetBounds(0, 0, w, h);
		staticEngineToolsPanel.SetPaintBorderEnabled(false);
		staticEngineToolsPanel.SetPaintBackgroundEnabled(false);
		staticEngineToolsPanel.SetPaintEnabled(false);
		staticEngineToolsPanel.SetVisible(true);
		staticEngineToolsPanel.SetCursor(CursorCode.None);
		staticEngineToolsPanel.SetZPos(75);

		staticGameUIPanel = engineAPI.New<EnginePanel>(staticPanel, "GameUI Panel");
		staticGameUIPanel.SetBounds(0, 0, w, h);
		staticGameUIPanel.SetPaintBorderEnabled(false);
		staticGameUIPanel.SetPaintBackgroundEnabled(false);
		staticGameUIPanel.SetPaintEnabled(false);
		staticGameUIPanel.SetVisible(true);
		staticGameUIPanel.SetCursor(CursorCode.None);
		staticGameUIPanel.SetZPos(100);

		staticGameDLLPanel = engineAPI.New<EnginePanel>(staticPanel, "staticGameDLLPanel");
		staticGameDLLPanel.SetBounds(0, 0, w, h);
		staticGameDLLPanel.SetPaintBorderEnabled(false);
		staticGameDLLPanel.SetPaintBackgroundEnabled(false);
		staticGameDLLPanel.SetKeyboardInputEnabled(false);
		staticGameDLLPanel.SetPaintEnabled(false);
		staticGameDLLPanel.SetCursor(CursorCode.None);
		staticGameDLLPanel.SetZPos(135);

		if (CommandLine.CheckParm("-tools"))
			staticGameDLLPanel.SetVisible(true);
		else
			staticGameDLLPanel.SetVisible(false);

		// TODO: the other panels...
		// Specifically,
		// - DebugSystemPanel
		// - DemoUIPanel (if we even do demos)
		// - FogUIPanel
		// - TxViewPanel
		// - FocusOverlayPanel
		// - Con_CreateConsolePanel
		// - CL_CreateEntityReportPanel
		// - VGui_CreateDrawTreePanel
		// - CL_CreateTextureListPanel
		// - CreateVProfPanels

		// cacheusedmaterials

		localize.AddFile($"Resource/valve_%language%.txt");
		localize.AddFile($"Resource/{engineAPI.GetRequiredService<EngineParms>().Mod}_%language%.txt");

		staticGameUIFuncs.Initialize(engineAPI);
		staticGameUIFuncs.Start();

		if (IsPC()) {
			staticGameConsole = engineAPI.GetRequiredService<IGameConsole>();
		}

		if(staticGameConsole != null) {
			staticGameConsole.Initialize();
			staticGameConsole.SetParent(staticGameUIPanel);
			staticGameConsole.Activate();
		}

		ActivateGameUI();
	}
	void DumpPanels_r(IPanel panel, int level) {
		int i;
		ReadOnlySpan<char> name = panel.GetName();

		Span<char> indentBuff = stackalloc char[64];
		for (i = 0; i < level; i++)
			indentBuff[i] = '.';

		indentBuff = indentBuff[..i];

		ConMsg($"{indentBuff}{name} popup == {panel.IsPopup()} kb == {panel.IsKeyboardInputEnabled()} mouse == {panel.IsMouseInputEnabled()}\n");

		int children = panel.GetChildCount();
		for (i = 0; i < children; i++) {
			IPanel child = panel.GetChild(i);
			DumpPanels_r(child, level + 1);
		}
	}
	[ConCommand(helpText: "Dump panel tree.")]
	void dump_panels() {
		DumpPanels_r(surface.GetEmbeddedPanel(), 0);
	}

	private void ActivateGameUI() {
		if (staticGameUIFuncs == null)
			return;

		staticGameUIPanel.SetVisible(true);
		staticGameUIPanel.MoveToFront();

		staticClientDLLPanel.SetVisible(false);
		staticGameUIPanel.SetMouseInputEnabled(true);

		matSystemSurface.SetCursor(CursorCode.Arrow);
		SetEngineVisible(false);
		staticGameUIFuncs.OnGameUIActivated();
	}

	private void SetEngineVisible(bool state) {
		if (staticClientDLLPanel != null) {
			staticClientDLLPanel.SetVisible(state);
		}
	}

	private void CreateAskConnectPanel(Panel staticPanel) {

	}

	public void Simulate() {
		vgui.GetAnimationController().UpdateAnimations(Sys.Time);
		int w = 0, h = 0;
		launcherMgr.RenderedSize(false, ref w, ref h);
		using (MatRenderContextPtr renderContext = new(materials))
			renderContext.Viewport(0, 0, w, h);

		staticGameUIFuncs.RunFrame();
		vgui.RunFrame();

		surface.CalculateMouseVisible();
		VGui_ActivateMouse();
	}

	private void VGui_ActivateMouse() {
		if (clientDLL == null)
			return;

		if (!game.IsActiveApp()) {
			clientDLL.IN_DeactivateMouse();
			return;
		}

		if (surface.IsCursorLocked()) {
			clientDLL.IN_ActivateMouse();
		}
		else {
			clientDLL.IN_DeactivateMouse();
		}
	}

	readonly ConVar r_drawvgui = new("r_drawvgui", "1", FCvar.Cheat, "Enable the rendering of vgui panels");

	public void Paint(PaintMode mode) {
		if (staticPanel == null)
			return;

		IPanel embedded = matSystemSurface.GetEmbeddedPanel();
		if (embedded == null)
			return;

		bool drawGui = r_drawvgui.GetBool();
		if (!drawGui)
			return;

		Panel panel = staticPanel;
		using (MatRenderContextPtr renderContext = new(materials)) {
			renderContext.GetViewport(out int x, out int y, out int w, out int h);
			panel.SetBounds(0, 0, w, h);

		}
		panel.Repaint();

		if ((mode & PaintMode.UIPanels) == PaintMode.UIPanels) {
			bool saveVisible = staticClientDLLPanel.IsVisible();
			bool saveToolsVisible = staticClientDLLToolsPanel.IsVisible();
			staticClientDLLPanel.SetVisible(false);
			staticClientDLLToolsPanel.SetVisible(false);

			matSystemSurface.PaintTraverseEx(embedded, true);

			staticClientDLLPanel.SetVisible(saveVisible);
			staticClientDLLToolsPanel.SetVisible(saveToolsVisible);
		}

		if ((mode & PaintMode.InGamePanels) == PaintMode.InGamePanels) {
			bool saveVisible = panel.IsVisible();
			panel.SetVisible(false);

			IPanel? saveParent = staticClientDLLPanel.GetParent();
			staticClientDLLPanel.SetParent(null);
			matSystemSurface.PaintTraverseEx(staticClientDLLPanel, true);
			staticClientDLLPanel.SetParent(saveParent);

			IPanel? saveToolParent = staticClientDLLToolsPanel.GetParent();
			staticClientDLLToolsPanel.SetParent(null);
			matSystemSurface.PaintTraverseEx(staticClientDLLToolsPanel, true);
			staticClientDLLToolsPanel.SetParent(saveParent);

			embedded.SetVisible(saveVisible);
		}

		if ((mode & PaintMode.Cursor) == PaintMode.Cursor) {
			matSystemSurface.PaintSoftwareCursor();
		}
	}

	IPanel IEngineVGui.GetPanel(VGuiPanelType type) => GetRootPanel(type);

	private IPanel GetRootPanel(VGuiPanelType type) {
		if (sv.IsDedicated())
			return null;

		switch (type) {
			default:
			case VGuiPanelType.Root:
				return staticPanel;
			case VGuiPanelType.ClientDll:
				return staticClientDLLPanel;
			case VGuiPanelType.GameUIDll:
				return staticGameUIPanel;
			case VGuiPanelType.Tools:
				return staticEngineToolsPanel;
			case VGuiPanelType.GameDll:
				return staticGameDLLPanel;
			case VGuiPanelType.ClientDllTools:
				return staticClientDLLToolsPanel;
		}
	}

	public void UpdateButtonState(in InputEvent ev) {
		vgui.GetInput().UpdateButtonState(in ev);
	}

	public bool Key_Event(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (IsPC() && IsShiftKeyDown()) {
			switch (code) {
				case ButtonCode.KeyF1:
					if (down)
						Cbuf.AddText("debugsystemui");

					return true;

				case ButtonCode.KeyF2:
					if (down)
						Cbuf.AddText("demoui");

					return true;
			}
		}

#if WIN32
		if (IsPC() && code == ButtonCode.KeyBackquote && (IsAltKeyDown() || IsCtrlKeyDown()))
			return true;
#endif

		if (down && code == ButtonCode.KeyEscape && !clientDLL.HandleUiToggle()) {
			if (IsPC()) {
				if (IsGameUIVisible()) {
					ReadOnlySpan<char> levelName = engineClient.GetLevelName();
					if (levelName != null && levelName.Length > 0) {
						Cbuf.AddText("gameui_hide");
						if (IsDebugSystemVisible())
							Cbuf.AddText("debugsystemui 0");
					}
				}
				else {
					Cbuf.AddText("gameui_activate");
				}
				return true;
			}
		}

		if (surface.HandleInputEvent(in ev)) {
			if (IsPC() && (code == ButtonCode.KeyBackquote))
				return false;
			return true;
		}
		return false;
	}

	private bool IsShiftKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLShift) | input.IsKeyDown(ButtonCode.KeyRShift);
	}

	private bool IsCtrlKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLControl) | input.IsKeyDown(ButtonCode.KeyRControl);
	}

	private bool IsAltKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLAlt) | input.IsKeyDown(ButtonCode.KeyRAlt);
	}

	private bool IsDebugSystemVisible() {
		return false; // Would require staticDebugSystemPanel... todo then
	}
}