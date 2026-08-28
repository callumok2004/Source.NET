using NeoVeldrid;

using Source.Common.MaterialSystem;

using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Veldrid;

public unsafe class VertexBufferVeldrid : IDisposable
{
	internal VertexFormat VertexBufferFormat;
	internal int Position;
	internal int VertexCount;
	internal int VertexSize;
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
	DeviceBuffer? vbo;
	int lastBufferSize = -1;

	public DeviceBuffer? Buffer => vbo;

	public VertexBufferVeldrid(GraphicsDevice device, bool dynamic) {
		this.device = device;
		Dynamic = dynamic;
		Locked = false;
		Flush = true;
		ExternalMemory = false;
	}

	public VertexBufferVeldrid(GraphicsDevice device, VertexFormat format, int vertexSize, int vertexCount, bool dynamic) {
		this.device = device;
		VertexBufferFormat = format;
		VertexSize = vertexSize;
		VertexCount = vertexCount;
		BufferSize = VertexSize * VertexCount;
		Dynamic = dynamic;
		Locked = false;
		Flush = true;
		ExternalMemory = false;

		RecomputeVBO();
	}

	public void FlushASAP() => Flush = true;

	public VertexLayoutDescription[] GetVertexLayout() => VeldridVertexLayout.For(VertexBufferFormat);

	public int NextLockOffset() {
		int nextOffset = VertexSize == 0 ? 0 : (Position + VertexSize - 1) / VertexSize;
		nextOffset *= VertexSize;
		return nextOffset;
	}

	internal void ChangeConfiguration(VertexFormat format, int vertexSize, int totalSize) {
		VertexBufferFormat = format;
		VertexSize = vertexSize;
		VertexCount = BufferSize / vertexSize;
		RecomputeVBO();
	}

	public void RecomputeVBO() {
		if (BufferSize > lastBufferSize || vbo == null) {
			if (SysmemBuffer != null) {
				NativeMemory.Free(SysmemBuffer);
				SysmemBuffer = null;
			}
			lastBufferSize = BufferSize;
			SysmemBuffer = NativeMemory.AllocZeroed((nuint)BufferSize);

			vbo?.Dispose();
			BufferUsage usage = BufferUsage.VertexBuffer;
			vbo = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)BufferSize, usage));
			vbo.Name = Dynamic ? "MaterialSystem DynamicVertexBuffer" : "MaterialSystem VertexBuffer";
		}
	}

	public byte* Lock(int numVerts, out int baseVertexIndex) {
		Assert(!Locked);

		if (numVerts > VertexCount) {
			baseVertexIndex = 0;
			return null;
		}

		if (Dynamic) {
			if (Position == 0 || Flush || !HasEnoughRoom(numVerts)) {
				if (SysmemBuffer != null)
					LateCreateShouldDiscard = true;

				Flush = false;
				Position = 0;
			}
		}
		else {
			Position = 0;
		}

		int lockOffset = NextLockOffset();
		baseVertexIndex = VertexSize == 0 ? 0 : (lockOffset / VertexSize);
		if (SysmemBuffer == null)
			RecomputeVBO();

		Locked = true;
		Position = lockOffset;
		return (byte*)((nint)SysmemBuffer + lockOffset);
	}

	public void Unlock(int vertexCount) {
		if (!Locked)
			return;

		int lockOffset = NextLockOffset();
		int bufferSize = vertexCount * VertexSize;

		if (bufferSize > 0 && vbo != null && SysmemBuffer != null)
			VeldridUploads.Write(device, vbo, (uint)Position, (nint)SysmemBuffer + Position, (uint)bufferSize);

		Position = lockOffset + bufferSize;
		Locked = false;
	}

	int modifyOffset;

	public byte* ModifyLock(int firstVertex, int numVerts, out int baseVertexIndex) {
		Assert(!Locked);

		if (SysmemBuffer == null)
			RecomputeVBO();

		modifyOffset = firstVertex * VertexSize;
		baseVertexIndex = firstVertex;
		Locked = true;
		return (byte*)((nint)SysmemBuffer + modifyOffset);
	}

	public void ModifyUnlock(int vertexCount) {
		if (!Locked)
			return;

		int size = vertexCount * VertexSize;
		if (size > 0 && vbo != null && SysmemBuffer != null)
			VeldridUploads.Write(device, vbo, (uint)modifyOffset, (nint)SysmemBuffer + modifyOffset, (uint)size);

		Locked = false;
	}

	internal bool HasEnoughRoom(int numVertices) {
		return NextLockOffset() + (numVertices * VertexSize) <= BufferSize;
	}

	static nint dummyData = (nint)NativeMemory.AlignedAlloc(512, 16);

	public static void ComputeVertexDescription(byte* vertexMemory, VertexFormat vertexFormat, ref VertexDesc desc) {
		desc.NumBoneWeights = vertexFormat.GetBoneWeightsSize();
		fixed (VertexDesc* descPtr = &desc) {
			nint offset = 0;
			nint baseptr = (nint)vertexMemory;
			int** vertexSizesToSet = stackalloc int*[64];
			int vertexSizesToSetPtr = 0;

			if ((vertexFormat & VertexFormat.Position) != 0) {
				descPtr->Position = (float*)(baseptr + offset);
				offset += VertexElement.Position.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->PositionSize;
			}
			else {
				descPtr->Position = (float*)dummyData;
				descPtr->PositionSize = 0;
			}

			if ((vertexFormat & VertexFormat.BoneIndex) != 0) {
				if (desc.NumBoneWeights > 0) {
					VertexElement boneWeightElement = VertexElement.BoneWeights1 + (desc.NumBoneWeights - 1);
					descPtr->BoneWeight = (float*)(baseptr + offset);
					offset += boneWeightElement.GetSize();
					vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->BoneWeightSize;
				}
				else {
					descPtr->BoneWeight = (float*)dummyData;
					descPtr->BoneWeightSize = 0;
				}

				descPtr->BoneMatrixIndex = (byte*)(baseptr + offset);
				offset += VertexElement.BoneIndex.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->BoneMatrixIndexSize;
			}
			else {
				descPtr->BoneMatrixIndex = (byte*)dummyData;
				descPtr->BoneMatrixIndexSize = 0;
			}

			if ((vertexFormat & VertexFormat.Normal) != 0) {
				descPtr->Normal = (float*)(baseptr + offset);
				offset += VertexElement.Normal.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->NormalSize;
			}
			else {
				descPtr->Normal = (float*)dummyData;
				descPtr->NormalSize = 0;
			}

			if ((vertexFormat & VertexFormat.Color) != 0) {
				descPtr->Color = (byte*)(baseptr + offset);
				offset += VertexElement.Color.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->ColorSize;
			}
			else {
				descPtr->Color = (byte*)dummyData;
				descPtr->ColorSize = 0;
			}

			if ((vertexFormat & VertexFormat.Specular) != 0) {
				descPtr->Specular = (byte*)(baseptr + offset);
				offset += VertexElement.Specular.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->SpecularSize;
			}
			else {
				descPtr->Specular = (byte*)dummyData;
				descPtr->SpecularSize = 0;
			}

			Span<VertexElement> texCoordElements = [VertexElement.TexCoord1D_0, VertexElement.TexCoord2D_0, VertexElement.TexCoord3D_0, VertexElement.TexCoord4D_0];
			for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++) {
				int size = (int)vertexFormat.GetTexCoordDimensionSize(i);
				if (size != 0) {
					desc.SetTexCoord(i, (float*)(baseptr + offset));
					offset += ((VertexElement)((int)texCoordElements[size - 1] + i)).GetSize();
					vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TexCoordSize[i];
				}
				else {
					desc.SetTexCoord(i, (float*)dummyData);
					desc.TexCoordSize[i] = 0;
				}
			}

			if ((vertexFormat & VertexFormat.TangentS) != 0) {
				descPtr->TangentS = (float*)(baseptr + offset);
				offset += VertexElement.TangentS.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TangentSSize;
			}
			else {
				descPtr->TangentS = (float*)dummyData;
				descPtr->TangentSSize = 0;
			}

			if ((vertexFormat & VertexFormat.TangentT) != 0) {
				descPtr->TangentT = (float*)(baseptr + offset);
				offset += VertexElement.TangentT.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TangentTSize;
			}
			else {
				descPtr->TangentT = (float*)dummyData;
				descPtr->TangentTSize = 0;
			}

			int userDataSize = (int)vertexFormat.GetUserDataSize();
			if (userDataSize > 0) {
				desc.UserData = (float*)(baseptr + offset);
				offset += (VertexElement.UserData1 + (userDataSize - 1)).GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->UserDataSize;
			}
			else {
				descPtr->UserData = (float*)dummyData;
				descPtr->UserDataSize = 0;
			}

			desc.ActualVertexSize = (int)offset;
			for (int i = 0; i < vertexSizesToSetPtr; i++) {
				*vertexSizesToSet[i] = (int)offset;
			}
		}
	}

	internal void HandleLateCreation() {

	}

	public void Dispose() {
		if (vbo != null) {
			vbo.Dispose();
			vbo = null;
		}

		if (SysmemBuffer != null) {
			NativeMemory.Free(SysmemBuffer);
			SysmemBuffer = null;
		}

		GC.SuppressFinalize(this);
	}
}
