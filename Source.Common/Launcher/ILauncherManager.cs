﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Source.Common.Launcher;

public interface ILauncherManager
{
	bool CreateGameWindow(string title, bool windowed, int width, int height);

	void SetCursorPosition(int x, int y);
	void SetWindowFullScreen(bool fullscreen, int width, int height);
	bool IsWindowFullScreen();

	void MoveWindow(int x, int y) ;
	void SizeWindow(int width, int tall) ;
	void PumpWindowsMessageLoop();
		
	void DestroyGameWindow();
	void SetApplicationIcon(ReadOnlySpan<char> appIconFile) ;

	void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta = false);

	void GetNativeDisplayInfo(int nDisplay, out uint width, out uint height, out uint refreshHz);
	void RenderedSize(bool set, ref int width, ref int height);
	void DisplayedSize(out int width, out int height);

	nint GetMainContext();
	nint GetGLContextForWindow(nint windowref);
	nint CreateExtraContext();
	void DeleteContext(nint context);
	bool MakeContextCurrent(nint context);
	nint GetWindowHandle();
	int GetEvents(WindowEvent[] eventBuffer, int length);
	void Swap();
	void CenterWindow(int v2, int v3);
}
