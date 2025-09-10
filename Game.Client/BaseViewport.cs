﻿using Game.Client.HUD;

using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Engine;
using Source.GUI.Controls;

using System.Numerics;
using System.Xml.Linq;

namespace Game.Client;

public interface IViewPortPanel
{
	ReadOnlySpan<char> GetName();
	void SetData(KeyValues? data);
	void Reset();
	void Update();
	bool NeedsUpdate();
	bool HasInputElements();
	void ShowPanel(bool state);
	GameActionSet GetPreferredActionSet();
	bool IsVisible();
	void SetParent(IPanel? parent);
}

public interface IViewPort
{
	void UpdateAllPanels();
	void ShowPanel(ReadOnlySpan<char> name, bool state);
	void ShowPanel(IViewPortPanel? panel, bool state);
	void ShowBackGround(bool show);
	IViewPortPanel? FindPanelByName(ReadOnlySpan<char> panelName);
	IViewPortPanel? GetActivePanel();
	void PostMessageToPanel(ReadOnlySpan<char> name, KeyValues? keyValues);
}

public class BaseViewport : EditablePanel, IViewPort
{
	IViewPortPanel? ActivePanel;
	IViewPortPanel? LastActivePanel;
	IPanel? LastPanel;
	readonly List<IViewPortPanel> Panels = [];

	readonly Hud HUD = Singleton<Hud>();
	readonly IEngineVGui EngineVGui = Singleton<IEngineVGui>();
	bool Initialized;
	bool HasParent;
	InlineArray2<int> OldSize;


	public override void SetParent(IPanel? newParent) {
		base.SetParent(newParent);
		base.SetProportional(true);

		if (newParent != null)
			AnimController.SetProportional(true);
		HasParent = newParent != null;
	}

	public override void OnThink() {
		if (ActivePanel != null && !ActivePanel.IsVisible()) {
			if (LastActivePanel != null) {
				ActivePanel = LastActivePanel;
				ShowPanel(ActivePanel, true);
				LastActivePanel = null;
			}
			else
				ActivePanel = null;
		}

		int count = Panels.Count;

		for (int i = 0; i < count; i++) {
			IViewPortPanel panel = Panels[i];
			if (panel.NeedsUpdate() && panel.IsVisible())
				panel.Update();
		}

		EngineVGui.GetPanel(VGuiPanelType.ClientDll).GetSize(out int w, out int h);

		if (OldSize[0] != w || OldSize[1] != h) {
			OldSize[0] = w;
			OldSize[1] = h;
			HLClient.ClientMode!.Layout();
		}

		base.OnThink();
	}

	public override void OnParentChanged(IPanel? oldParent, IPanel? newParent) {
		base.OnParentChanged(oldParent, newParent);
	}

	public bool AddNewPanel(IViewPortPanel panel, ReadOnlySpan<char> debugName) {
		if (panel == null) {
			DevMsg($"BaseViewport.AddNewPanel({debugName}): NULL panel.\n");
			return false;
		}

		if (FindPanelByName(panel.GetName()) != null) {
			DevMsg($"BaseViewport.AddNewPanel: panel with name '{panel.GetName()}' already exists.\n");
			return false;
		}

		Panels.Add(panel);
		panel.SetParent(this);

		return true;
	}

	public BaseViewport() : base(null, "CBaseViewport") {
		SetSize(10, 10);
		Initialized = false;
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
		IScheme? scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), "resource/ClientScheme.res", "ClientScheme");
		SetScheme(scheme!);
		SetProportional(true);

		AnimController = new AnimationController(this);
		AnimController.SetScheme(scheme!);
		AnimController.SetProportional(true);

		// TODO: Hud animations
	}

	AnimationController AnimController;

	public IViewPortPanel? FindPanelByName(ReadOnlySpan<char> panelName) {
		foreach (var panel in Panels) {
			if (panel.GetName().Equals(panelName, StringComparison.Ordinal))
				return panel;
		}
		return null;
	}

	public IViewPortPanel? GetActivePanel() {
		return ActivePanel;
	}

	public void PostMessageToPanel(ReadOnlySpan<char> name, KeyValues? keyValues) {
		PostMessage(((Panel)FindPanelByName(name)!)!, keyValues!);
	}

	public void ShowBackGround(bool show) {
		throw new NotImplementedException();
	}

	public void ShowPanel(ReadOnlySpan<char> name, bool state) {
		((Panel)FindPanelByName(name)!)!.SetVisible(state);
	}

	public void ShowPanel(IViewPortPanel? panel, bool state) {
		((Panel?)panel)!.SetVisible(state);
	}

	public void UpdateAllPanels() {
		foreach (var panel in Panels) {
			if (panel.IsVisible())
				panel.Update();
		}
	}

	internal void ReloadScheme(ReadOnlySpan<char> from) {
		if (from != null) {
			IScheme scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), from, "HudScheme")!;

			SetScheme(scheme);
			SetProportional(true);
			AnimController.SetScheme(scheme);
		}

		if (LoadHudAnimations() == false)
			if (!AnimController.SetScriptFile(this, "scripts/HudAnimations.txt", true))
				Assert(false);

		SetProportional(true);
		// todo: conditions
		// reload the .res file from disk
		LoadControlSettings("scripts/HudLayout.res", null, null, null);

		HUD.RefreshHudTextures();
		InvalidateLayout(true, true);
		HUD.ResetHUD();
	}

	private bool LoadHudAnimations() {
		return true; // lie for now
	}

	public void Start() {
		CreateDefaultPanels();

		Initialized = true;
	}

	public override void Paint() {
		
	}

	private void CreateDefaultPanels() {

	}
}
