using NeoVeldrid;

using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Veldrid;

internal static class VeldridUploads
{
	static CommandList? frame;
	static bool recordUpdates;
	static bool shadowUniforms;

	static readonly Dictionary<DeviceBuffer, byte[]> shadows = [];
	static readonly List<DeviceBuffer> dirty = [];

	public static void Configure(GraphicsBackend backend) {
		recordUpdates = true;
		shadowUniforms = backend == GraphicsBackend.Direct3D11;
		shadows.Clear();
		dirty.Clear();
	}

	public static void BeginFrame(CommandList commands) => frame = commands;

	public static void EndFrame() {
		Flush();
		frame = null;
	}

	static bool UseCommandList => recordUpdates && frame != null;

	static bool Shadowed(DeviceBuffer buffer, uint offset, uint size) =>
		shadowUniforms
		&& (buffer.Usage & BufferUsage.UniformBuffer) != 0
		&& (offset != 0 || size != buffer.SizeInBytes);

	static byte[] ShadowFor(DeviceBuffer buffer) {
		if (!shadows.TryGetValue(buffer, out byte[]? shadow)) {
			shadow = new byte[buffer.SizeInBytes];
			shadows.Add(buffer, shadow);
		}

		return shadow;
	}

	static void MarkDirty(DeviceBuffer buffer) {
		if (!dirty.Contains(buffer))
			dirty.Add(buffer);
	}

	public static void Flush() {
		if (dirty.Count == 0)
			return;

		foreach (DeviceBuffer buffer in dirty) {
			byte[] shadow = shadows[buffer];
			unsafe {
				fixed (byte* source = shadow) {
					if (UseCommandList)
						frame!.UpdateBuffer(buffer, 0, (nint)source, (uint)shadow.Length);
					else
						device!.UpdateBuffer(buffer, 0, (nint)source, (uint)shadow.Length);
				}
			}
		}

		dirty.Clear();
	}

	static GraphicsDevice? device;

	public static void Write<T>(GraphicsDevice device, DeviceBuffer buffer, uint offset, ref T value) where T : unmanaged {
		VeldridUploads.device = device;

		uint size = (uint)Marshal.SizeOf<T>();
		if (Shadowed(buffer, offset, size)) {
			MemoryMarshal.Write(ShadowFor(buffer).AsSpan((int)offset, (int)size), in value);
			MarkDirty(buffer);
			return;
		}

		if (UseCommandList)
			frame!.UpdateBuffer(buffer, offset, ref value);
		else
			device.UpdateBuffer(buffer, offset, ref value);
	}

	public static void Write(GraphicsDevice device, DeviceBuffer buffer, uint offset, nint source, uint size) {
		if (size == 0)
			return;

		VeldridUploads.device = device;

		if (Shadowed(buffer, offset, size)) {
			unsafe {
				new ReadOnlySpan<byte>((void*)source, (int)size).CopyTo(ShadowFor(buffer).AsSpan((int)offset, (int)size));
			}

			MarkDirty(buffer);
			return;
		}

		if (UseCommandList)
			frame!.UpdateBuffer(buffer, offset, source, size);
		else
			device.UpdateBuffer(buffer, offset, source, size);
	}
}
