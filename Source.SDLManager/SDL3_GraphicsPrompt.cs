using SDL;

using Source.Common.Launcher;

using System.Text;

namespace Source.SDLManager;

public static unsafe class SDL3_GraphicsPrompt
{
	public static GraphicsBackendChoice Ask() {
		SDL3_State.InitializeIfRequired();

		byte[] title = Encoding.UTF8.GetBytes("Source.NET\0");
		byte[] message = Encoding.UTF8.GetBytes("Select a rendering backend for this session.\0");
		byte[] openGL = Encoding.UTF8.GetBytes("OpenGL 4.6\0");
		byte[] vulkan = Encoding.UTF8.GetBytes("Vulkan\0");
		byte[] direct3D = Encoding.UTF8.GetBytes("Direct3D 11\0");
		byte[] veldridGL = Encoding.UTF8.GetBytes("OpenGL (Veldrid)\0");

		fixed (byte* titlePtr = title)
		fixed (byte* messagePtr = message)
		fixed (byte* openGLPtr = openGL)
		fixed (byte* vulkanPtr = vulkan)
		fixed (byte* direct3DPtr = direct3D)
		fixed (byte* veldridGLPtr = veldridGL) {
			SDL_MessageBoxButtonData* buttons = stackalloc SDL_MessageBoxButtonData[4];

			buttons[0].flags = SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT;
			buttons[0].buttonID = (int)GraphicsBackendChoice.OpenGL46;
			buttons[0].text = openGLPtr;

			buttons[1].flags = default;
			buttons[1].buttonID = (int)GraphicsBackendChoice.Vulkan;
			buttons[1].text = vulkanPtr;

			buttons[2].flags = default;
			buttons[2].buttonID = (int)GraphicsBackendChoice.Direct3D11;
			buttons[2].text = direct3DPtr;

			buttons[3].flags = SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT;
			buttons[3].buttonID = (int)GraphicsBackendChoice.VeldridOpenGL;
			buttons[3].text = veldridGLPtr;

			SDL_MessageBoxData data = default;
			data.flags = SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION;
			data.window = null;
			data.title = titlePtr;
			data.message = messagePtr;
			data.numbuttons = 4;
			data.buttons = buttons;
			data.colorScheme = null;

			int result;
			if (!SDL3.SDL_ShowMessageBox(&data, &result))
				return GraphicsBackendChoice.OpenGL46;

			return result switch {
				(int)GraphicsBackendChoice.Vulkan => GraphicsBackendChoice.Vulkan,
				(int)GraphicsBackendChoice.Direct3D11 => GraphicsBackendChoice.Direct3D11,
				(int)GraphicsBackendChoice.VeldridOpenGL => GraphicsBackendChoice.VeldridOpenGL,
				_ => GraphicsBackendChoice.OpenGL46,
			};
		}
	}
}
