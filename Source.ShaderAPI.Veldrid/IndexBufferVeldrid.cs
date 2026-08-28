using NeoVeldrid;

using Source.Common.MaterialSystem;

using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Veldrid;

public unsafe class IndexBufferVeldrid : IDisposable
{
	internal MaterialIndexFormat IndexFormat;
	internal int IndexCount;
	internal int Position;
	internal void* SysmemBuffer;
	internal int SysmemBufferStartBytes;
	internal int BufferSize;

	internal uint LockCount;
	internal bool Dynamic;
	internal bool Locked;
	internal bool Flush;
	internal bool ExternalMemory;
	internal bool SoftwareVertexProcessing;
	internal bool LateCreateShouldDiscard;

	readonly GraphicsDevice device;
	DeviceBuffer? ibo;
	int lastBufferSize = -1;

	public DeviceBuffer? Buffer => ibo;

	public IndexBufferVeldrid(GraphicsDevice device, int count, bool dynamic = false) {
		this.device = device;
		Position = 0;
		Locked = false;
		Flush = true;
		Dynamic = dynamic;
		ExternalMemory = false;
		LateCreateShouldDiscard = false;

		count += count % 2;
		IndexCount = count;

		BufferSize = sizeof(ushort) * IndexCount;

		RecomputeIBO();
	}

	public void RecomputeIBO() {
		if (BufferSize > lastBufferSize || ibo == null) {
			if (SysmemBuffer != null) {
				NativeMemory.Free(SysmemBuffer);
				SysmemBuffer = null;
			}
			lastBufferSize = BufferSize;
			SysmemBuffer = NativeMemory.AllocZeroed((nuint)BufferSize);

			ibo?.Dispose();
			BufferUsage usage = BufferUsage.IndexBuffer;
			ibo = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)BufferSize, usage));
			ibo.Name = Dynamic ? "MaterialSystem DynamicIndexBuffer" : "MaterialSystem IndexBuffer";
		}

		if (SysmemBuffer == null) {
			Warning("WARNING: RecomputeIBO failure (Veldrid's not happy...)\n");
			Warning($"    Index buffer object  : {ibo?.Name}\n");
			Warning($"    Attempted alloc size : {BufferSize}\n");
		}
	}

	public void FlushASAP() => Flush = true;

	public short* Lock(bool readOnly, int indexCount, out int startIndex, int firstIndex) {
		Assert(!Locked);

		if (Dynamic) {
			if (Position == 0 || Flush || !HasEnoughRoom(indexCount)) {
				if (SysmemBuffer != null)
					LateCreateShouldDiscard = true;

				Flush = false;
				Position = 0;
			}
		}

		int position = Position;
		if (firstIndex >= 0)
			position = firstIndex;

		startIndex = position;
		if (SysmemBuffer == null)
			RecomputeIBO();

		Locked = true;
		return (short*)SysmemBuffer + position;
	}

	public void Unlock(int indexCount) {
		if (!Locked)
			return;

		if (indexCount > 0 && ibo != null && SysmemBuffer != null)
			VeldridUploads.Write(device, ibo, (uint)(Position * 2), (nint)SysmemBuffer + Position * 2, (uint)(indexCount * 2));

		Position += indexCount;
		Locked = false;
	}

	public short* ModifyLock(int firstIndex, int indexCount, out int startIndex) {
		Assert(!Locked);

		if (SysmemBuffer == null)
			RecomputeIBO();

		startIndex = firstIndex;
		Locked = true;
		return (short*)SysmemBuffer + firstIndex;
	}

	public void ModifyUnlock(int firstIndex, int indexCount) {
		if (!Locked)
			return;

		if (indexCount > 0 && ibo != null && SysmemBuffer != null)
			VeldridUploads.Write(device, ibo, (uint)(firstIndex * 2), (nint)SysmemBuffer + firstIndex * 2, (uint)(indexCount * 2));

		Locked = false;
	}

	internal bool HasEnoughRoom(int indices) {
		return indices + Position <= IndexCount;
	}

	internal void HandleLateCreation() {

	}

	public void Dispose() {
		if (ibo != null) {
			ibo.Dispose();
			ibo = null;
		}

		if (SysmemBuffer != null) {
			NativeMemory.Free(SysmemBuffer);
			SysmemBuffer = null;
		}

		GC.SuppressFinalize(this);
	}
}
