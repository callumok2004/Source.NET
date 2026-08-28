using NeoVeldrid;

using Source.Common.MaterialSystem;

namespace Source.ShaderAPI.Veldrid;

public enum ShaderInputAttribute
{
	Position = 0,
	Normal = 1,
	Color = 2,
	Specular = 3,
	TangentS = 4,
	TangentT = 5,
	Wrinkle = 6,
	BoneIndex = 7,
	BoneWeights = 8,
	UserData = 9,
	TexCoord0 = 10,
	TexCoord1 = 11,
	TexCoord2 = 12,
	TexCoord3 = 13,
	TexCoord4 = 14,
	TexCoord5 = 15,
	TexCoord6 = 16,
	TexCoord7 = 17,
	Count
}

public static class VeldridVertexLayout
{
	static readonly Dictionary<VertexFormat, VertexLayoutDescription[]> cache = [];

	public static int SlotLimit = (int)ShaderInputAttribute.Count;

	public static void Configure(GraphicsBackend backend) {
		SlotLimit = backend == GraphicsBackend.OpenGL ? 16 : (int)ShaderInputAttribute.Count;
		cache.Clear();
		infoCache.Clear();
	}

	public static string SemanticName(ShaderInputAttribute attr) => attr switch {
		ShaderInputAttribute.Position => "POSITION",
		ShaderInputAttribute.Normal => "NORMAL",
		ShaderInputAttribute.Color => "COLOR",
		ShaderInputAttribute.Specular => "COLOR1",
		ShaderInputAttribute.TangentS => "TANGENT",
		ShaderInputAttribute.TangentT => "BINORMAL",
		ShaderInputAttribute.Wrinkle => "WRINKLE",
		ShaderInputAttribute.BoneIndex => "BLENDINDICES",
		ShaderInputAttribute.BoneWeights => "BLENDWEIGHT",
		ShaderInputAttribute.UserData => "USERDATA",
		_ => $"TEXCOORD{(int)attr - (int)ShaderInputAttribute.TexCoord0}",
	};

	static VdVertexElementFormat ToElementFormat(int count, VertexAttributeType type, bool normalized) => type switch {
		VertexAttributeType.Float => count switch {
			1 => VdVertexElementFormat.Float1,
			2 => VdVertexElementFormat.Float2,
			3 => VdVertexElementFormat.Float3,
			4 => VdVertexElementFormat.Float4,
			_ => throw new NotSupportedException($"Unsupported float element count {count}"),
		},
		VertexAttributeType.UnsignedByte => count switch {
			2 => normalized ? VdVertexElementFormat.Byte2_Norm : VdVertexElementFormat.Byte2,
			4 => normalized ? VdVertexElementFormat.Byte4_Norm : VdVertexElementFormat.Byte4,
			_ => throw new NotSupportedException($"Unsupported byte element count {count}"),
		},
		VertexAttributeType.Byte => count switch {
			2 => normalized ? VdVertexElementFormat.SByte2_Norm : VdVertexElementFormat.SByte2,
			4 => normalized ? VdVertexElementFormat.SByte4_Norm : VdVertexElementFormat.SByte4,
			_ => throw new NotSupportedException($"Unsupported sbyte element count {count}"),
		},
		VertexAttributeType.UnsignedShort => count switch {
			2 => normalized ? VdVertexElementFormat.UShort2_Norm : VdVertexElementFormat.UShort2,
			4 => normalized ? VdVertexElementFormat.UShort4_Norm : VdVertexElementFormat.UShort4,
			_ => throw new NotSupportedException($"Unsupported ushort element count {count}"),
		},
		VertexAttributeType.Short => count switch {
			2 => normalized ? VdVertexElementFormat.Short2_Norm : VdVertexElementFormat.Short2,
			4 => normalized ? VdVertexElementFormat.Short4_Norm : VdVertexElementFormat.Short4,
			_ => throw new NotSupportedException($"Unsupported short element count {count}"),
		},
		_ => throw new NotSupportedException($"Unsupported vertex attribute type {type}"),
	};

	public sealed class LayoutInfo
	{
		public required VertexLayoutDescription[] Layouts;
		public bool ColorMesh;
		public required bool[] Present;
		public required uint[] Offsets;
		public required uint Stride;
	}

	static readonly Dictionary<(VertexFormat, uint), LayoutInfo> infoCache = [];

	public static LayoutInfo InfoFor(VertexFormat format, uint colorStride = 0) {
		if (infoCache.TryGetValue((format, colorStride), out LayoutInfo? cached))
			return cached;

		LayoutInfo info = Build(format, colorStride);
		infoCache.Add((format, colorStride), info);
		return info;
	}

	public static VertexLayoutDescription[] For(VertexFormat format) => InfoFor(format).Layouts;

	public static VertexLayoutDescription[] For(VertexFormat format, uint colorStride) => InfoFor(format, colorStride).Layouts;

	static LayoutInfo Build(VertexFormat format, uint colorStride) {
		bool colorMesh = colorStride > 0;
		VertexElementDescription[] slots = new VertexElementDescription[(int)ShaderInputAttribute.Count];
		uint[] offsets = new uint[(int)ShaderInputAttribute.Count];
		bool[] filled = new bool[(int)ShaderInputAttribute.Count];
		uint offset = 0;

		void Add(ShaderInputAttribute attr, VertexElement element) {
			element.GetInformation(out int count, out VertexAttributeType type);
			bool normalized = attr is ShaderInputAttribute.Color or ShaderInputAttribute.Specular;
			VdVertexElementFormat elementFormat = ToElementFormat(count, type, normalized);

			slots[(int)attr] = new VertexElementDescription(
				SemanticName(attr),
				VertexElementSemantic.TextureCoordinate,
				elementFormat);
			offsets[(int)attr] = offset;
			filled[(int)attr] = true;

			offset += (uint)(count * (int)type.SizeOf());
		}

		if ((format & VertexFormat.Position) != 0)
			Add(ShaderInputAttribute.Position, VertexElement.Position);

		if ((format & VertexFormat.BoneIndex) != 0) {
			int numBoneWeights = format.GetBoneWeightsSize();
			if (numBoneWeights > 0)
				Add(ShaderInputAttribute.BoneWeights, VertexElement.BoneWeights1 + (numBoneWeights - 1));

			Add(ShaderInputAttribute.BoneIndex, VertexElement.BoneIndex);
		}

		if ((format & VertexFormat.Normal) != 0)
			Add(ShaderInputAttribute.Normal, VertexElement.Normal);

		if ((format & VertexFormat.Color) != 0)
			Add(ShaderInputAttribute.Color, VertexElement.Color);

		if ((format & VertexFormat.Specular) != 0)
			Add(ShaderInputAttribute.Specular, VertexElement.Specular);

		ReadOnlySpan<VertexElement> texCoordElements = [
			VertexElement.TexCoord1D_0, VertexElement.TexCoord2D_0, VertexElement.TexCoord3D_0, VertexElement.TexCoord4D_0
		];
		for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++) {
			int texCoordSize = format.GetTexCoordDimensionSize(i);
			if (texCoordSize <= 0)
				continue;

			VertexElement element = (VertexElement)((int)texCoordElements[texCoordSize - 1] + i);
			Add((ShaderInputAttribute)((int)ShaderInputAttribute.TexCoord0 + i), element);
		}

		if ((format & VertexFormat.TangentS) != 0)
			Add(ShaderInputAttribute.TangentS, VertexElement.TangentS);

		if ((format & VertexFormat.TangentT) != 0)
			Add(ShaderInputAttribute.TangentT, VertexElement.TangentT);

		int userDataSize = format.GetUserDataSize();
		if (userDataSize > 0)
			Add(ShaderInputAttribute.UserData, VertexElement.UserData1 + (userDataSize - 1));

		uint stride = offset;

		if (colorMesh) {
			VertexElement.Specular.GetInformation(out int specularCount, out VertexAttributeType specularType);
			slots[(int)ShaderInputAttribute.Specular] = new VertexElementDescription(
				SemanticName(ShaderInputAttribute.Specular),
				VertexElementSemantic.TextureCoordinate,
				ToElementFormat(specularCount, specularType, true));
			offsets[(int)ShaderInputAttribute.Specular] = 0;
			filled[(int)ShaderInputAttribute.Specular] = true;
		}

		int slotCount = Math.Min(filled.Length, SlotLimit);
		for (int i = slotCount; i < filled.Length; i++) {
			if (filled[i])
				slotCount = i + 1;
		}

		VertexLayoutDescription[] layouts = new VertexLayoutDescription[slotCount];
		for (int i = 0; i < slotCount; i++) {
			if (!filled[i]) {
				slots[i] = new VertexElementDescription(
					$"{SemanticName((ShaderInputAttribute)i)}_unused",
					VertexElementSemantic.TextureCoordinate,
					VdVertexElementFormat.Float4);
				offsets[i] = 0;
				layouts[i] = new VertexLayoutDescription(16, 1, slots[i]);
				continue;
			}

			uint slotStride = colorMesh && i == (int)ShaderInputAttribute.Specular
				? colorStride
				: stride;

			layouts[i] = new VertexLayoutDescription(slotStride, slots[i]);
		}

		return new LayoutInfo {
			Layouts = layouts,
			Offsets = offsets,
			Stride = stride,
			ColorMesh = colorMesh,
			Present = filled,
		};
	}

	public static void Clear() => cache.Clear();
}
