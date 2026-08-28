namespace Source.Common.Launcher;

public enum GraphicsBackendChoice
{
	OpenGL46,
	Vulkan,
	Direct3D11,
	VeldridOpenGL,
}

public static class GraphicsSelection
{
	public static GraphicsBackendChoice Selected { get; private set; } = GraphicsBackendChoice.OpenGL46;

	public static bool IsVeldrid => Selected != GraphicsBackendChoice.OpenGL46;

	public static bool NeedsOpenGLWindow => Selected is GraphicsBackendChoice.OpenGL46 or GraphicsBackendChoice.VeldridOpenGL;

	public static Func<GraphicsBackendChoice>? Picker { get; set; }

	public static void Resolve(bool allowPrompt) {
		ReadOnlySpan<char> cmdLine = Platform.GetCommandLine();

		if (cmdLine.Contains("-opengl", StringComparison.OrdinalIgnoreCase)) {
			Selected = GraphicsBackendChoice.OpenGL46;
			return;
		}

		if (cmdLine.Contains("-vulkan", StringComparison.OrdinalIgnoreCase)) {
			Selected = GraphicsBackendChoice.Vulkan;
			return;
		}

		if (cmdLine.Contains("-d3d11", StringComparison.OrdinalIgnoreCase)) {
			Selected = GraphicsBackendChoice.Direct3D11;
			return;
		}

		if (cmdLine.Contains("-veldridgl", StringComparison.OrdinalIgnoreCase)) {
			Selected = GraphicsBackendChoice.VeldridOpenGL;
			return;
		}

		if (!allowPrompt || Picker == null) {
			Selected = GraphicsBackendChoice.OpenGL46;
			return;
		}

		Selected = Picker();
	}
}
