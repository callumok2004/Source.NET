﻿using Source.Common.Input;
using Source.GUI.Controls;

namespace Game.Client;

public enum MessageModeType {
	None,
	Say,
	SayTeam
}

public interface IClientMode
{
	Panel GetViewport();
	void Init();
	int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding);
	void Layout();
	void OverrideMouseInput(ref float mouse_x, ref float mouse_y);
	void StartMessageMode(MessageModeType messageModeType);
	AnimationController? GetViewportAnimationController();
	void Enable();
}
