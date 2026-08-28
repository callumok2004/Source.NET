using NeoVeldrid;

using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

namespace Source.ShaderAPI.Veldrid;

public enum DeviceState
{
	OK,
	NeedsReset
}

public class MeshMgr : IMeshMgr
{
	internal IMaterialSystem MaterialSystem = null!;
	internal ShaderAPIVeldrid ShaderAPI = null!;

	public const int VERTEX_BUFFER_SIZE = 32768;
	public const int MAX_QUAD_INDICES = 16384;

	bool BufferedMode;

	readonly List<VertexBufferVeldrid> DynamicVertexBuffers = [];
	IndexBufferVeldrid? DynamicIndexBuffer;

	BufferedMeshVeldrid BufferedMesh = null!;
	DynamicMeshVeldrid DynamicMesh = null!;

	GraphicsDevice Device => ShaderAPI.GraphicsDevice!;

	internal void Init() {
		BufferedMesh = InitMesh<BufferedMeshVeldrid>();
		DynamicMesh = InitMesh<DynamicMeshVeldrid>();
		DynamicMesh.Init(0);
		CreateDynamicIndexBuffer();
		BufferedMode = true;
	}

	private TMesh InitMesh<TMesh>() where TMesh : MeshVeldrid, new() {
		TMesh ret = new TMesh();
		ret.ShaderAPI = MaterialSystem.GetRenderContext().GetShaderAPI();
		ret.ShaderUtil = MaterialSystem.GetShaderUtil();
		ret.MeshMgr = ShaderAPI.MeshMgr;
		ret.ShaderDevice = ret.ShaderAPI.GetShaderDevice();
		return ret;
	}

	internal void Flush() {
		if (IsPC())
			BufferedMesh.Flush();
	}

	internal void DiscardVertexBuffers() {
		for (int i = 0; i < DynamicVertexBuffers.Count; i++)
			DynamicVertexBuffers[i].FlushASAP();
		DynamicIndexBuffer?.FlushASAP();
	}

	internal void RestoreBuffers() {
		Init();
	}

	internal void RenderPassWithVertexAndIndexBuffers() {
		throw new NotImplementedException();
	}

	public IMesh GetDynamicMesh(IMaterial? material, VertexFormat vertexFormat, int hwSkinBoneCount, bool buffered, IMesh? vertexOverride, IMesh? indexOverride) {
		Assert(material == null || ((IMaterialInternal)material).IsRealTimeVersion());

		if (BufferedMode != buffered && BufferedMode)
			BufferedMesh.SetMesh(null);

		BufferedMode = buffered;

		IMaterialInternal matInternal = (IMaterialInternal)material!;
		MeshVeldrid mesh = DynamicMesh;

		if (BufferedMode) {
			Assert(!BufferedMesh.WasNotRendered());
			BufferedMesh.SetMesh(mesh);
			mesh = BufferedMesh;
		}

		if (vertexOverride == null) {
			VertexFormat fmt = matInternal.GetVertexFormat();
			mesh.SetVertexFormat(fmt);
		}
		else {
			MeshVeldrid vertexMesh = (MeshVeldrid)vertexOverride;
			mesh.SetVertexFormat(vertexMesh.GetVertexFormat());
		}

		mesh.SetMaterial(matInternal);
		if (mesh == DynamicMesh) {
			MeshVeldrid? baseVertex = (MeshVeldrid?)vertexOverride;
			if (baseVertex != null)
				DynamicMesh.OverrideVertexBuffer(baseVertex.GetVertexBuffer());
			MeshVeldrid? baseIndex = (MeshVeldrid?)indexOverride;
			if (baseIndex != null)
				DynamicMesh.OverrideIndexBuffer(baseIndex.GetIndexBuffer());
		}

		return mesh;
	}

	private void CreateDynamicIndexBuffer() {
		DestroyDynamicIndexBuffer();
		DynamicIndexBuffer = new IndexBufferVeldrid(Device, IMesh.INDEX_BUFFER_SIZE, true);
	}

	private void DestroyDynamicIndexBuffer() {
		DynamicIndexBuffer?.Dispose();
		DynamicIndexBuffer = null;
	}

	internal VertexBufferVeldrid FindOrCreateVertexBuffer(int dynamicBufferID, VertexFormat vertexFormat) {
		int vertexSize = VertexFormatSize(vertexFormat);

		while (DynamicVertexBuffers.Count <= dynamicBufferID) {
			int bufferMemory = ShaderAPI.GetCurrentDynamicVBSize();
			VertexBufferVeldrid vertexBuffer = new VertexBufferVeldrid(Device, true);
			vertexBuffer.VertexSize = 0;
			int initVertexSize = bufferMemory / VERTEX_BUFFER_SIZE, initVertexCount = VERTEX_BUFFER_SIZE;
			vertexBuffer.BufferSize = initVertexSize * initVertexCount;
			DynamicVertexBuffers.Add(vertexBuffer);
		}

		VertexBufferVeldrid buffer = DynamicVertexBuffers[dynamicBufferID];

		if (buffer.VertexSize != vertexSize || buffer.VertexBufferFormat != vertexFormat) {
			int bufferMemory = ShaderAPI.GetCurrentDynamicVBSize();
			buffer.VertexSize = vertexSize;
			buffer.ChangeConfiguration(vertexFormat, vertexSize, bufferMemory);
		}

		return DynamicVertexBuffers[dynamicBufferID];
	}

	internal IndexBufferVeldrid GetDynamicIndexBuffer() {
		return DynamicIndexBuffer!;
	}

	internal IMesh CreateStaticMesh(VertexFormat format, ReadOnlySpan<char> textureGroup, IMaterial? material) {
		MeshVeldrid mesh = InitMesh<MeshVeldrid>();
		mesh.SetVertexFormat(format);
		if (material != null)
			mesh.SetMaterial((IMaterialInternal)material);
		return mesh;
	}

	internal void DestroyStaticMesh(IMesh mesh) {

	}

	internal VertexFormat ComputeVertexFormat(VertexFormat flags, int texCoordArraySize, Span<int> texCoordDimensions, int numBoneWeights, int userDataSize) {
		VertexFormat fmt = flags;

		Assert(numBoneWeights <= 4);

		if (numBoneWeights > 0)
			fmt |= VertexFormat.BoneWeights2;

		Assert(userDataSize <= 4);
		fmt |= VertexExts.GetUserDataSize(userDataSize);

		texCoordArraySize = Math.Min(texCoordArraySize, VertexExts.VERTEX_MAX_TEXTURE_COORDINATES);
		for (int i = 0; i < texCoordArraySize; ++i) {
			if (!texCoordDimensions.IsEmpty) {
				Assert(texCoordDimensions[i] >= 0 && texCoordDimensions[i] <= 4);
				fmt |= VertexExts.GetTexCoordSize(i, texCoordDimensions[i]);
			}
			else
				fmt |= VertexExts.GetTexCoordSize(i, 2);
		}

		return fmt;
	}

	internal unsafe int VertexFormatSize(VertexFormat vertexFormat) {
		MeshDesc desc = new();
		VertexBufferVeldrid.ComputeVertexDescription(null, vertexFormat, ref desc.Vertex);
		return desc.Vertex.ActualVertexSize;
	}

	internal int GetMaxIndicesToRender() {
		return IMesh.INDEX_BUFFER_SIZE;
	}

	internal int GetMaxVerticesToRender(IMaterial material) {
		VertexFormat fmt = material.GetVertexFormat();
		int vertexSize = VertexFormatSize(fmt);
		if (vertexSize == 0) {
			Warning($"bad vertex size for material {material.GetName()}\n");
			return 0;
		}

		int maxVerts = ShaderAPI.GetCurrentDynamicVBSize() / vertexSize;
		return Math.Min(maxVerts, 32767);
	}
}
