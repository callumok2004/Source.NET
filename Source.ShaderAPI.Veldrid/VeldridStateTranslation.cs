using NeoVeldrid;

using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

namespace Source.ShaderAPI.Veldrid;

public static class VeldridStateTranslation
{
	public static VdBlendFactor ToVeldrid(this ShaderBlendFactor factor) => factor switch {
		ShaderBlendFactor.Zero => VdBlendFactor.Zero,
		ShaderBlendFactor.One => VdBlendFactor.One,
		ShaderBlendFactor.SrcColor => VdBlendFactor.SourceColor,
		ShaderBlendFactor.OneMinusSrcColor => VdBlendFactor.InverseSourceColor,
		ShaderBlendFactor.SrcAlpha => VdBlendFactor.SourceAlpha,
		ShaderBlendFactor.OneMinusSrcAlpha => VdBlendFactor.InverseSourceAlpha,
		ShaderBlendFactor.DstAlpha => VdBlendFactor.DestinationAlpha,
		ShaderBlendFactor.OneMinusDstAlpha => VdBlendFactor.InverseDestinationAlpha,
		ShaderBlendFactor.DstColor => VdBlendFactor.DestinationColor,
		ShaderBlendFactor.OneMinusDstColor => VdBlendFactor.InverseDestinationColor,
		ShaderBlendFactor.SrcAlphaSat => VdBlendFactor.SourceAlpha,
		ShaderBlendFactor.BothSrcAlpha => VdBlendFactor.SourceAlpha,
		ShaderBlendFactor.BothInvSrcAlpha => VdBlendFactor.InverseSourceAlpha,
		_ => VdBlendFactor.One,
	};

	public static VdBlendFunction ToVeldrid(this ShaderBlendOp op) => op switch {
		ShaderBlendOp.Add => VdBlendFunction.Add,
		ShaderBlendOp.Subtract => VdBlendFunction.Subtract,
		ShaderBlendOp.RevSubtract => VdBlendFunction.ReverseSubtract,
		ShaderBlendOp.Min => VdBlendFunction.Minimum,
		ShaderBlendOp.Max => VdBlendFunction.Maximum,
		_ => VdBlendFunction.Add,
	};

	public static VdComparisonKind ToVeldrid(this ShaderDepthFunc func) => func switch {
		ShaderDepthFunc.Never => VdComparisonKind.Never,
		ShaderDepthFunc.Nearer => VdComparisonKind.Less,
		ShaderDepthFunc.Equal => VdComparisonKind.Equal,
		ShaderDepthFunc.NearerOrEqual => VdComparisonKind.LessEqual,
		ShaderDepthFunc.Farther => VdComparisonKind.Greater,
		ShaderDepthFunc.NotEqual => VdComparisonKind.NotEqual,
		ShaderDepthFunc.FartherOrEqual => VdComparisonKind.GreaterEqual,
		ShaderDepthFunc.Always => VdComparisonKind.Always,
		_ => VdComparisonKind.LessEqual,
	};

	public static VdComparisonKind ToVeldrid(this StencilComparisonFunction func) => func switch {
		StencilComparisonFunction.Never => VdComparisonKind.Never,
		StencilComparisonFunction.Less => VdComparisonKind.Less,
		StencilComparisonFunction.Equal => VdComparisonKind.Equal,
		StencilComparisonFunction.LessEqual => VdComparisonKind.LessEqual,
		StencilComparisonFunction.Greater => VdComparisonKind.Greater,
		StencilComparisonFunction.NotEqual => VdComparisonKind.NotEqual,
		StencilComparisonFunction.GreaterEqual => VdComparisonKind.GreaterEqual,
		StencilComparisonFunction.Always => VdComparisonKind.Always,
		_ => VdComparisonKind.Always,
	};

	public static VdStencilOperation ToVeldrid(this StencilOperation op) => op switch {
		StencilOperation.Keep => VdStencilOperation.Keep,
		StencilOperation.Zero => VdStencilOperation.Zero,
		StencilOperation.Replace => VdStencilOperation.Replace,
		StencilOperation.IncrSat => VdStencilOperation.IncrementAndClamp,
		StencilOperation.DecrSat => VdStencilOperation.DecrementAndClamp,
		StencilOperation.Invert => VdStencilOperation.Invert,
		StencilOperation.Incr => VdStencilOperation.IncrementAndWrap,
		StencilOperation.Decr => VdStencilOperation.DecrementAndWrap,
		_ => VdStencilOperation.Keep,
	};

	public static VdPolygonFillMode ToVeldrid(this ShaderPolyMode mode) => mode switch {
		ShaderPolyMode.Line => VdPolygonFillMode.Wireframe,
		_ => VdPolygonFillMode.Solid,
	};

	public static VdSamplerAddressMode ToVeldrid(this TexWrapMode mode) => mode switch {
		TexWrapMode.Clamp => VdSamplerAddressMode.Clamp,
		TexWrapMode.Repeat => VdSamplerAddressMode.Wrap,
		TexWrapMode.Border => VdSamplerAddressMode.Border,
		_ => VdSamplerAddressMode.Wrap,
	};

	public static VdSamplerFilter ToVeldrid(this TexFilterMode mode) => mode switch {
		TexFilterMode.Nearest => VdSamplerFilter.MinPoint_MagPoint_MipPoint,
		TexFilterMode.Linear => VdSamplerFilter.MinLinear_MagLinear_MipPoint,
		TexFilterMode.NearestMipmapNearest => VdSamplerFilter.MinPoint_MagPoint_MipPoint,
		TexFilterMode.LinearMipmapNearest => VdSamplerFilter.MinLinear_MagLinear_MipPoint,
		TexFilterMode.NearestMipmapLinear => VdSamplerFilter.MinPoint_MagPoint_MipLinear,
		TexFilterMode.LinearMipmapLinear => VdSamplerFilter.MinLinear_MagLinear_MipLinear,
		_ => VdSamplerFilter.MinLinear_MagLinear_MipLinear,
	};

	public static (float slopeScale, float constant) ToDepthBias(this PolygonOffsetMode mode) => mode switch {
		PolygonOffsetMode.Decal => (-1.0f, -1.0f),
		PolygonOffsetMode.ShadowBias => (2.0f, 16.0f),
		_ => (0.0f, 0.0f),
	};
}
