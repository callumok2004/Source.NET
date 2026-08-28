using Source.Common.MaterialSystem;

namespace Source.ShaderAPI.Veldrid;

public unsafe class BufferedMeshVeldrid : MeshVeldrid
{
	MeshVeldrid? Mesh;
	ushort LastIndex;
	ushort ExtraIndices;
	bool IsFlushing;
	bool WasRendered = true;
	bool FlushNeeded;

	public void ResetRendered() {
		WasRendered = false;
	}

	public bool WasNotRendered() => !WasRendered;

	public void Flush() {
		if (Mesh != null && !IsFlushing && FlushNeeded) {
			IsFlushing = true;
			((IMesh)Mesh!)!.Draw();
			IsFlushing = false;
			FlushNeeded = false;
		}
	}

	public void SetMesh(MeshVeldrid? mesh) {
		if (Mesh != mesh) {
			ShaderAPI.FlushBufferedPrimitives();
			Mesh = mesh;
		}
	}

	public override void SetMaterial(IMaterialInternal matInternal) {
		Assert(Mesh != null);
		Mesh.SetMaterial(matInternal);
	}

	public override void SetVertexFormat(VertexFormat fmt) {
		Assert(Mesh != null);
		if (Mesh.NeedsVertexFormatReset(fmt)) {
			ShaderAPI.FlushBufferedPrimitives();
			Mesh.SetVertexFormat(fmt);
		}
	}

	public override MaterialPrimitiveType GetPrimitiveType() {
		return Mesh!.GetPrimitiveType();
	}

	public override void SetPrimitiveType(MaterialPrimitiveType type) {
		if (type != GetPrimitiveType()) {
			ShaderAPI.FlushBufferedPrimitives();
			Mesh!.SetPrimitiveType(type);
		}
	}

	public override void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		ShaderUtil.SyncMatrices();

		Assert(Mesh != null);
		Assert(WasRendered);

		Mesh.PreLock();

		if (!Mesh.HasEnoughRoom(vertexCount, indexCount))
			ShaderAPI.FlushBufferedPrimitives();

		WasRendered = false;

		Mesh.LockMesh(vertexCount, indexCount, ref desc);
	}

	public override void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		if (Mesh!.GetPrimitiveType() == MaterialPrimitiveType.TriangleStrip && desc.Index.IndexSize > 0) {
			if (ExtraIndices > 0)
				*(desc.Index.Indices - 1) = *desc.Index.Indices;

			LastIndex = desc.Index.Indices[indexCount - 1];
			indexCount += ExtraIndices;
		}

		Mesh.UnlockMesh(vertexCount, indexCount, ref desc);
	}

	public override void Draw(int firstIndex = -1, int indexCount = 0) {
		if (!ShaderUtil.OnDrawMesh(this, firstIndex, indexCount)) {
			WasRendered = true;
			MarkAsDrawn();
			return;
		}

		Assert(!IsFlushing && !WasRendered);
		Assert(firstIndex == -1 && indexCount == 0);

		WasRendered = true;
		FlushNeeded = true;
	}
}
