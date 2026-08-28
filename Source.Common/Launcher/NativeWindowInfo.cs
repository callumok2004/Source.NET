namespace Source.Common.Launcher;

public enum NativeWindowKind
{
	Unknown,
	Win32,
	X11,
	Wayland,
	Cocoa,
}
public struct NativeWindowInfo
{
	public NativeWindowKind Kind;
	public nint Window;
	public nint Display;
	public int Width;
	public int Height;
}
