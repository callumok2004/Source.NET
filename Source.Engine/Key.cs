﻿using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Input;

using System;

namespace Source.Engine;

public struct KeyInfo
{
	public string? KeyBinding;
	public KeyUpTarget KeyUpTarget;
	public bool KeyDown;
}

public enum KeyUpTarget
{
	AnyTarget,
	Engine,
	VGui,
	Tools,
	Client
}

public class Key(IInputSystem? inputSystem, IServiceProvider services)
{
	readonly KeyInfo[] KeyInfo = new KeyInfo[(int)ButtonCode.Last];
	bool TrapMode = false;
	bool DoneTrapping = false;
	ButtonCode TrapKeyUp = ButtonCode.Invalid;
	ButtonCode TrapKey = ButtonCode.Invalid;
	delegate bool KeyFilterDelegate(in InputEvent ev);

	IEngineVGui? engineVGui;
	IEngineVGui EngineVGui => engineVGui ??= services.GetRequiredService<IEngineVGui>();

	public int CountBindings() {
		int i;
		int c = 0;

		for (i = 0; i < (int)ButtonCode.Last; i++) {
			if (string.IsNullOrEmpty(KeyInfo[i].KeyBinding))
				continue;
			c++;
		}
		return c;
	}

	public ReadOnlySpan<char> NameForBinding(ReadOnlySpan<char> binding) {
		int i;

		ReadOnlySpan<char> bind = binding;
		if (binding[0] == '+')
			bind = bind[1..];

		for (i = 0; i < (int)ButtonCode.Last; i++) {
			string? keyBinding = KeyInfo[i].KeyBinding;
			if (!string.IsNullOrEmpty(keyBinding)) {
				if (keyBinding[0] == '+') {
					if (bind.Equals(keyBinding.AsSpan()[1..], StringComparison.Ordinal))
						return inputSystem!.ButtonCodeToString((ButtonCode)i);
				}
				else {
					if (bind.Equals(keyBinding, StringComparison.Ordinal))
						return inputSystem!.ButtonCodeToString((ButtonCode)i);
				}

			}
		}

		return null;
	}

	public void SetBinding(ButtonCode keynum, ReadOnlySpan<char> cmd) {
		if (keynum == ButtonCode.Invalid)
			return;

		KeyInfo[(int)keynum].KeyBinding = new(cmd);
	}

	public void Event(in InputEvent ev) {
#if SWDS
		return;
#endif

		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (KeyInfo[(int)code].KeyDown == down)
			return;

		KeyInfo[(int)code].KeyDown = down;

		if (FilterTrappedKey(code, down))
			return;

		EngineVGui.UpdateButtonState(in ev);

		if (FilterKey(in ev, KeyUpTarget.Tools, HandleToolKey))
			return;

		if (FilterKey(in ev, KeyUpTarget.VGui, HandleVGuiKey))
			return;

		if (FilterKey(in ev, KeyUpTarget.Client, HandleClientKey))
			return;

		FilterKey(in ev, KeyUpTarget.Engine, HandleEngineKey);
	}
	public bool HandleToolKey(in InputEvent ev) {
		// TODO: Tools
		return false;
	}

	public bool HandleVGuiKey(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		return EngineVGui.Key_Event(in ev);
	}

	public bool HandleClientKey(in InputEvent ev) {
		// TODO: Client
		return false;
	}

	public bool HandleEngineKey(in InputEvent ev) {
		// TODO: Engine
		return false;
	}

	private bool FilterKey(in InputEvent ev, KeyUpTarget target, KeyFilterDelegate func) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (!down && !ShouldPassKeyUpToTarget(code, target))
			return false;

		bool filtered = func(in ev);

		if (down) {
			if (filtered) {
				Assert(KeyInfo[(int)code].KeyUpTarget == KeyUpTarget.AnyTarget);
				KeyInfo[(int)code].KeyUpTarget = target;
			}
		}
		else {
			if (KeyInfo[(int)code].KeyUpTarget == target) {
				KeyInfo[(int)code].KeyUpTarget = KeyUpTarget.AnyTarget;
				filtered = true;
			}
			else {
				Assert(!filtered);
			}
		}

		return filtered;
	}

	private bool ShouldPassKeyUpToTarget(ButtonCode code, KeyUpTarget target)
		=> (KeyInfo[(int)code].KeyUpTarget == target) || (KeyInfo[(int)code].KeyUpTarget == KeyUpTarget.AnyTarget);

	private bool FilterTrappedKey(ButtonCode code, bool down) {
		if (TrapKeyUp == code && !down) {
			TrapKeyUp = ButtonCode.Invalid;
			return true;
		}

		if (TrapMode && down) {
			TrapKey = code;
			TrapMode = false;
			DoneTrapping = true;
			TrapKeyUp = code;
			return true;
		}

		return false;
	}
	[ConCommand(flags: FCvar.DontRecord)]
	void bind(in TokenizedCommand args) {
		Span<char> cmd = stackalloc char[1024];
		int c = args.ArgC();
		if (c != 2 && c != 3) {
			ConMsg("bind <key> [command] : attach a command to a key\n");
			return;
		}

		BindKey(args[1], c == 2, cmd[..strcpy(cmd, args[2])]);
	}

	public void BindKey(ReadOnlySpan<char> bind, bool show, ReadOnlySpan<char> cmd) {
		if (inputSystem == null)
			return;

		ButtonCode code = inputSystem.StringToButtonCode(bind);
		if (code == ButtonCode.Invalid) {
			ConMsg($"\"{bind}\" isn't a valid key\n");
			return;
		}

		if (show) {
			if (KeyInfo[(int)code].KeyBinding != null)
				ConMsg($"\"{bind}\" = \"{KeyInfo[(int)code].KeyBinding}\"\n");
			else
				ConMsg($"\"{bind}\" is not bound\n");

			return;
		}

		if (code == ButtonCode.KeyEscape)
			cmd = "cancelselect";

		SetBinding(code, cmd);
	}
}