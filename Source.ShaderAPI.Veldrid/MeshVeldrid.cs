using NeoVeldrid;

using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Veldrid;

public struct PrimList
{
	public int FirstIndex;
	public int NumIndices;
}

public unsafe class MeshVeldrid : IMesh
{
	public IShaderAPI ShaderAPI = null!;
	public IShaderUtil ShaderUtil = null!;
	public MeshMgr MeshMgr = null!;
	public IShaderDevice ShaderDevice = null!;

	protected VertexBufferVeldrid VertexBuffer = null!;
	protected IndexBufferVeldrid IndexBuffer = null!;

	protected VertexFormat LastVertexFormat;
	protected VertexFormat VertexFormat;
	protected IMaterialInternal Material = null!;
	protected MaterialPrimitiveType Type = MaterialPrimitiveType.Triangles;
	protected bool IsDrawing;

	protected static PrimList* s_Prims;
	protected static int s_PrimsCount;
	protected static uint s_FirstVertex;
	protected static uint s_NumVertices;

	protected VdPrimitiveTopology Mode = ComputeMode(MaterialPrimitiveType.Triangles);
	public bool Locked;

	public VertexBufferVeldrid GetVertexBuffer() => VertexBuffer;
	public IndexBufferVeldrid GetIndexBuffer() => IndexBuffer;

	GraphicsDevice Device => ((ShaderAPIVeldrid)ShaderAPI).GraphicsDevice!;

	public virtual void BeginCastBuffer(VertexFormat format) {
		throw new NotImplementedException();
	}

	public void DrawMesh() {
		Assert(!IsDrawing);
		IsDrawing = true;

		ShaderAPI.DrawMesh(this);

		IsDrawing = false;
	}

	protected bool SetRenderState(int vertexOffsetInBytes, int firstIndex) => SetRenderState(vertexOffsetInBytes, firstIndex, VertexFormat.Invalid);

	protected virtual bool SetRenderState(int vertexOffsetInBytes, int firstIndex, VertexFormat vertexFormat) {
		if (ShaderDevice.IsDeactivated()) {
			ResetMeshRenderState();
			return false;
		}

		LastVertexFormat = vertexFormat;
		return true;
	}

	private void ResetMeshRenderState() {
		s_Prims = null;
		s_PrimsCount = 0;
		s_FirstVertex = 0;
		s_NumVertices = 0;
	}

	public virtual void BeginCastBuffer(MaterialIndexFormat format) {
		throw new NotImplementedException();
	}

	public virtual void Draw(int firstIndex = -1, int indexCount = 0) {
		Assert(VertexBuffer != null);
		if (VertexBuffer == null)
			return;

		if (!ShaderUtil.OnDrawMesh(this, firstIndex, indexCount)) {
			MarkAsDrawn();
			return;
		}

		PrimList* primList = stackalloc PrimList[1];
		if (firstIndex == -1 || indexCount == 0) {
			primList->FirstIndex = 0;
			primList->NumIndices = NumIndices;
		}
		else {
			primList->FirstIndex = firstIndex;
			primList->NumIndices = indexCount;
		}
		DrawInternal(primList, 1);
	}

	public virtual void Draw(ReadOnlySpan<Source.Common.MaterialSystem.PrimList> lists, int numLists) {
		Assert(VertexBuffer != null);
		if (VertexBuffer == null)
			return;

		if (!ShaderUtil.OnDrawMesh(this, -1, 0)) {
			MarkAsDrawn();
			return;
		}

		fixed (PrimList* p = MemoryMarshal.Cast<Source.Common.MaterialSystem.PrimList, PrimList>(lists))
			DrawInternal(p, numLists);
	}

	private void DrawInternal(PrimList* primList, int lists) {
		HandleLateCreation();

		int i;
		for (i = 0; i < lists; i++) {
			if (primList[i].NumIndices > 0)
				break;
		}

		if (i == lists)
			return;

		if (!SetRenderState(0, 0))
			return;

		s_Prims = primList;
		s_PrimsCount = lists;

#if DEBUG
		for (i = 0; i < lists; ++i)
			Assert(primList[i].NumIndices > 0);
#endif

		s_FirstVertex = 0;
		s_NumVertices = (uint)VertexBuffer.VertexCount;

		DrawMesh();

		s_Prims = null;
		s_PrimsCount = 0;
	}

	public virtual void EndCastBuffer() {
		throw new NotImplementedException();
	}

	public virtual int GetRoomRemaining() {
		throw new NotImplementedException();
	}

	public virtual VertexFormat GetVertexFormat() {
		return VertexFormat;
	}

	public virtual int IndexCount() => NumIndices;

	public virtual MaterialIndexFormat IndexFormat() => MaterialIndexFormat.x16;

	public virtual bool IsDynamic() => false;

	public void HandleLateCreation() {
		VertexBuffer?.HandleLateCreation();
		IndexBuffer?.HandleLateCreation();
	}

	public virtual bool Lock(int vertexCount, bool append, ref VertexDesc desc) {
		if (VertexBuffer == null) {
			int size = MeshMgr.VertexFormatSize(VertexFormat);
			VertexBuffer = new VertexBufferVeldrid(Device, VertexFormat, size, vertexCount, false);
		}

		byte* vertexMemory = VertexBuffer.Lock(vertexCount, out desc.FirstVertex);
		VertexBufferVeldrid.ComputeVertexDescription(vertexMemory, VertexFormat, ref desc);

		return true;
	}

	public virtual int Lock(bool readOnly, int firstIndex, int indexCount, ref IndexDesc desc) {
		if (ShaderDevice.IsDeactivated() || indexCount == 0) {
			desc.Indices = ScratchIndexBuffer;
			desc.IndexSize = 0;
			return 0;
		}

		IndexBuffer ??= new IndexBufferVeldrid(Device, indexCount, false);

		desc.Indices = (ushort*)IndexBuffer.Lock(readOnly, indexCount, out int startIndex, firstIndex);
		if (desc.Indices == null) {
			desc.IndexSize = 0;
			Assert(false);
			Warning("Failed to lock index buffer...\n");
			return 0;
		}

		desc.IndexSize = 1;
		IsIBLocked = true;
		return startIndex;
	}

	bool IsIBLocked;
	static readonly ushort* ScratchIndexBuffer = (ushort*)NativeMemory.Alloc(6 * sizeof(ushort));

	public virtual void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		ShaderUtil.SyncMatrices();

		Lock(vertexCount, false, ref desc.Vertex);
		if (Type != MaterialPrimitiveType.Points)
			Lock(false, -1, indexCount, ref desc.Index);
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
		}

		Locked = true;
	}

	public virtual void MarkAsDrawn() { }

	int modifyVertexCount;
	int modifyFirstIndex;
	int modifyIndexCount;

	public virtual void ModifyBegin(int firstVertex, int vertexCount, int firstIndex, int indexCount, ref MeshDesc desc) {
		Assert(VertexBuffer != null);

		byte* vertexMemory = VertexBuffer.ModifyLock(firstVertex, vertexCount, out desc.Vertex.FirstVertex);
		VertexBufferVeldrid.ComputeVertexDescription(vertexMemory, VertexFormat, ref desc.Vertex);

		if (indexCount > 0) {
			IndexBuffer ??= new IndexBufferVeldrid(Device, indexCount, false);
			desc.Index.Indices = (ushort*)IndexBuffer.ModifyLock(firstIndex, indexCount, out int startIndex);
			desc.Index.IndexSize = 1;
			IsIBLocked = true;
		}
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
		}

		modifyVertexCount = vertexCount;
		modifyFirstIndex = firstIndex;
		modifyIndexCount = indexCount;
		Locked = true;
	}

	public virtual void ModifyEnd(ref MeshDesc desc) {
		Assert(Locked);

		if (IsIBLocked) {
			IndexBuffer.ModifyUnlock(modifyFirstIndex, modifyIndexCount);
			IsIBLocked = false;
		}

		VertexBuffer.ModifyUnlock(modifyVertexCount);
		Locked = false;
	}

	IMesh? ColorMesh;
	int ColorMeshVertOffsetInBytes = 0;

	public virtual void SetColorMesh(IMesh colorMesh, int vertexOffset) {
		ColorMesh = colorMesh;
		ColorMeshVertOffsetInBytes = vertexOffset;
	}

	public virtual MaterialPrimitiveType GetPrimitiveType() {
		return Type;
	}

	public virtual void SetPrimitiveType(MaterialPrimitiveType type) {
		if (!ShaderUtil.OnSetPrimitiveType(this, type))
			return;

		Type = type;
		Mode = ComputeMode(type);
	}

	public virtual bool Unlock(int vertexCount, ref VertexDesc desc) {
		VertexBuffer.Unlock(vertexCount);
		return true;
	}

	public virtual bool Unlock(int indexCount, ref IndexDesc desc) {
		if (!IsIBLocked)
			return true;
		IndexBuffer.Unlock(indexCount);
		IsIBLocked = false;
		return true;
	}

	int NumVertices;
	int NumIndices;

	public virtual void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		Assert(Locked);

		Unlock(vertexCount, ref desc.Vertex);
		if (Type != MaterialPrimitiveType.Points)
			Unlock(indexCount, ref desc.Index);

		NumVertices = vertexCount;
		NumIndices = indexCount;
		Locked = false;
	}

	public virtual int VertexCount() {
		return NumVertices;
	}

	public virtual void SetMaterial(IMaterialInternal matInternal) {
		Material = matInternal;
	}

	public virtual void SetVertexFormat(VertexFormat fmt) {
		VertexFormat = fmt;
	}

	public virtual void RenderPass() {
		HandleLateCreation();
		Assert(Type != MaterialPrimitiveType.Heterogenous);

		ShaderAPIVeldrid api = (ShaderAPIVeldrid)ShaderAPI;
		if (VertexBuffer?.Buffer == null || IndexBuffer?.Buffer == null)
			return;

		VertexBufferVeldrid? colorVertexBuffer = (ColorMesh as MeshVeldrid)?.VertexBuffer;
		DeviceBuffer? colorBuffer = colorVertexBuffer?.Buffer;

		for (int iPrim = 0; iPrim < s_PrimsCount; iPrim++) {
			PrimList* prim = &s_Prims[iPrim];

			if (prim->NumIndices == 0)
				continue;

			if (Type == MaterialPrimitiveType.Points || Type == MaterialPrimitiveType.InstancedQuads)
				throw new NotImplementedException();

			int numPrimitives = NumPrimitives(s_NumVertices, prim->NumIndices);
			CheckIndices(prim, numPrimitives);

			api.DrawIndexed(
				VertexBuffer.Buffer,
				IndexBuffer.Buffer,
				VertexFormat,
				Mode,
				(uint)prim->FirstIndex,
				(uint)prim->NumIndices,
				colorBuffer,
				(uint)ColorMeshVertOffsetInBytes,
				(uint)(colorVertexBuffer?.VertexSize ?? 0));
		}
	}

	public static VdPrimitiveTopology ComputeMode(MaterialPrimitiveType type) => type switch {
		MaterialPrimitiveType.Points => VdPrimitiveTopology.PointList,
		MaterialPrimitiveType.Lines => VdPrimitiveTopology.LineList,
		MaterialPrimitiveType.Triangles => VdPrimitiveTopology.TriangleList,
		MaterialPrimitiveType.TriangleStrip => VdPrimitiveTopology.TriangleStrip,
		MaterialPrimitiveType.Heterogenous => VdPrimitiveTopology.TriangleList,
		_ => throw new Exception(),
	};

	private int NumPrimitives(uint vertexCount, int indexCount) => Mode switch {
		VdPrimitiveTopology.PointList => (int)vertexCount,
		VdPrimitiveTopology.LineList => indexCount / 2,
		VdPrimitiveTopology.TriangleList => indexCount / 3,
		VdPrimitiveTopology.TriangleStrip => indexCount - 2,
		_ => 0,
	};

	[Conditional("DEBUG")]
	private void CheckIndices(PrimList* prim, int numPrimitives) {
		int indexCount = 0;
		if (Mode == VdPrimitiveTopology.TriangleList)
			indexCount = numPrimitives * 3;
		else if (Mode == VdPrimitiveTopology.TriangleStrip)
			indexCount = numPrimitives + 2;

		if (indexCount != 0)
			Assert(prim->FirstIndex >= 0 && prim->FirstIndex < IndexBuffer.IndexCount);
	}

	internal bool HasColorMesh() => ColorMesh != null;

	internal bool HasFlexMesh() => false;

	public virtual bool NeedsVertexFormatReset(VertexFormat fmt) {
		return VertexFormat != fmt;
	}

	public virtual bool HasEnoughRoom(int vertexCount, int indexCount) => true;

	public virtual void PreLock() {
		throw new NotImplementedException();
	}

	public virtual void UseVertexBuffer(VertexBufferVeldrid vertexBuffer) {
		VertexBuffer = vertexBuffer;
	}

	public virtual void UseIndexBuffer(IndexBufferVeldrid indexBuffer) {
		IndexBuffer = indexBuffer;
	}

	public virtual void OverrideVertexBuffer(VertexBufferVeldrid vertexBuffer) => UseVertexBuffer(vertexBuffer);
	public virtual void OverrideIndexBuffer(IndexBufferVeldrid indexBuffer) => UseIndexBuffer(indexBuffer);
}
