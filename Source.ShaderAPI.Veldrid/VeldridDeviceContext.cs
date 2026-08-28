using NeoVeldrid;
using NeoVeldrid.OpenGL;

using Source.Common.Launcher;
using Source.Common.ShaderAPI;

namespace Source.ShaderAPI.Veldrid;

public class VeldridDeviceContext : IGraphicsContext, IDisposable
{
	public GraphicsDevice Device { get; }
	public Swapchain Swapchain => Device.MainSwapchain;
	public GraphicsBackend Backend => Device.BackendType;

	nint IGraphicsContext.HardwareHandle => 0;

	readonly IOpenGLPlatform? glPlatform;

	VeldridDeviceContext(GraphicsDevice device, IOpenGLPlatform? glPlatform = null) {
		Device = device;
		this.glPlatform = glPlatform;
	}

	public static GraphicsBackend SelectBackend(GraphicsDriver driver) {
		GraphicsBackend? requested = GraphicsSelection.Selected switch {
			GraphicsBackendChoice.Direct3D11 => GraphicsBackend.Direct3D11,
			GraphicsBackendChoice.Vulkan => GraphicsBackend.Vulkan,
			GraphicsBackendChoice.VeldridOpenGL => GraphicsBackend.OpenGL,
			_ => null,
		};

		if (requested is GraphicsBackend explicitBackend) {
			if (GraphicsDevice.IsBackendSupported(explicitBackend))
				return explicitBackend;

			Warning($"Veldrid: requested backend {explicitBackend} is unsupported here, falling back.\n");
		}

		foreach (GraphicsBackend candidate in (ReadOnlySpan<GraphicsBackend>)[
			GraphicsBackend.Vulkan, GraphicsBackend.Direct3D11
		]) {
			if (GraphicsDevice.IsBackendSupported(candidate))
				return candidate;
		}

		throw new PlatformNotSupportedException("No supported Veldrid graphics backend available.");
	}

	static SwapchainSource CreateSwapchainSource(in NativeWindowInfo info) => info.Kind switch {
		NativeWindowKind.Win32 => SwapchainSource.CreateWin32(info.Window, info.Display),
		NativeWindowKind.X11 => SwapchainSource.CreateXlib(info.Display, info.Window),
		NativeWindowKind.Wayland => SwapchainSource.CreateWayland(info.Display, info.Window),
		NativeWindowKind.Cocoa => SwapchainSource.CreateNSWindow(info.Window),
		_ => throw new PlatformNotSupportedException($"Cannot build a swapchain for {info.Kind}."),
	};

	static GraphicsDevice CreateOpenGL(IOpenGLPlatform platform, in GraphicsDeviceOptions options, uint width, uint height) {
		OpenGLPlatformInfo info = new(
			platform.ContextHandle,
			platform.GetProcAddress,
			platform.MakeCurrent,
			platform.GetCurrentContext,
			platform.ClearCurrentContext,
			platform.DeleteContext,
			platform.SwapBuffers,
			platform.SetSyncToVerticalBlank);

		return GraphicsDevice.CreateOpenGL(options, info, width, height);
	}

	public static VeldridDeviceContext Create(in NativeWindowInfo windowInfo, in ShaderDeviceInfo deviceInfo, GraphicsDriver driver, IOpenGLPlatform? glPlatform = null) {
		SwapchainDescription swapchainDesc = new(
			CreateSwapchainSource(in windowInfo),
			(uint)windowInfo.Width,
			(uint)windowInfo.Height,
			PixelFormat.D24_UNorm_S8_UInt,
			syncToVerticalBlank: false,
			colorSrgb: false);

		GraphicsDeviceOptions options = new() {
			Debug = Platform.GetCommandLine().Contains("-gpudebug", StringComparison.OrdinalIgnoreCase),
			HasMainSwapchain = true,
			SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
			SyncToVerticalBlank = false,
			PreferDepthRangeZeroToOne = false,
			PreferStandardClipSpaceYDirection = true,
			ResourceBindingModel = ResourceBindingModel.Improved,
			SwapchainSrgbFormat = false,
		};

		GraphicsBackend backend = SelectBackend(driver);
		GraphicsDevice device = backend switch {
			GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options, swapchainDesc),
			GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options, swapchainDesc),
			GraphicsBackend.OpenGL when glPlatform != null =>
				CreateOpenGL(glPlatform, in options, (uint)windowInfo.Width, (uint)windowInfo.Height),
			_ => throw new PlatformNotSupportedException(
				$"Backend {backend} has no device creation path here."),
		};

		DevMsg($"Veldrid: initialized {device.BackendType} ({device.DeviceName})\n");
		return new VeldridDeviceContext(device, backend == GraphicsBackend.OpenGL ? glPlatform : null);
	}

	public void MakeCurrent() {
	}

	public void SetSwapInterval(float swapInterval) {
		Device.SyncToVerticalBlank = swapInterval > 0;
	}

	public void SwapBuffers() {
		Device.SwapBuffers();
	}

	public void ResizeMainWindow(uint width, uint height) {
		Device.MainSwapchain?.Resize(width, height);
	}

	public void Dispose() {
		Device.WaitForIdle();
		Device.Dispose();
		GC.SuppressFinalize(this);
	}
}
