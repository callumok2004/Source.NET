using NeoVeldrid;

using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;

namespace Source.ShaderAPI.Veldrid;

public class HardwareConfig : IMaterialSystemHardwareConfig
{
	public bool SupportsShadowDepthTexturesCap = true;
	public ImageFormat ShadowDepthTextureFormat = ImageFormat.NV_DST24;
	public ImageFormat NullTextureFormat = ImageFormat.NV_NULL;

	public const int MAX_NUM_LIGHTS = 4;
	const int MAX_TEXTURE_DIMENSION = 16384;

	GraphicsDevice? device;
	GraphicsDeviceFeatures? features;

	public void AttachDevice(GraphicsDevice device) {
		this.device = device;
		features = device.Features;
	}

	public enum ShadowFilterMode
	{
		None = 0,
		NvidiaPcfPoisson = 0,
		AtiNoPcf = 1,
		AtiNoPcfFetch4 = 2
	}

	public bool ActuallySupportsPixelShaders_2_b() => true;

	public bool CanDoSRGBReadFromRTs() => true;

	public bool FakeSRGBWrite() => false;

	public int GetDXSupportLevel() => 0;

	public int GetFrameBufferColorDepth() => 32;

	public HDRType GetHardwareHDRType() => HDRType.None;

	public bool GetHDREnabled() => false;

	public HDRType GetHDRType() => HDRType.None;

	public int GetMaxDXSupportLevel() => 95;

	public int GetMaxVertexTextureDimension() => MAX_TEXTURE_DIMENSION;

	public int GetSamplerCount() => (int)Sampler.MaxSamplers;

	public ReadOnlySpan<char> GetShaderDLLName() => "shaderapiveldrid";

	public int GetShadowFilterMode() => ShadowDepthTextureFormat switch {
		ImageFormat.NV_DST16 or ImageFormat.NV_DST24 => (int)ShadowFilterMode.NvidiaPcfPoisson,
		ImageFormat.ATI_DST16 or ImageFormat.ATI_DST24 => (int)ShadowFilterMode.AtiNoPcfFetch4,
		_ => (int)ShadowFilterMode.None,
	};

	public int GetTextureStageCount() => GetSamplerCount();

	public int GetVertexTextureCount() => 4;

	public bool HasDestAlphaBuffer() => true;

	public bool HasFastVertexTextures() => false;

	public bool HasProjectedBumpEnv() => true;

	public bool HasSetDeviceGammaRamp() => false;

	public bool HasStencilBuffer() => StencilBufferBits() > 0;

	public bool IsAAEnabled() => device != null && device.MainSwapchain?.Framebuffer.ColorTargets[0].Target.SampleCount != TextureSampleCount.Count1;

	public int MaxBlendMatrices() => 0;

	public int MaxBlendMatrixIndices() => 0;

	public int MaxHWMorphBatchCount() => 0;

	public int MaximumAnisotropicLevel() => features?.SamplerAnisotropy == true ? 16 : 0;

	public int MaxNumLights() => MAX_NUM_LIGHTS;

	public int MaxTextureAspectRatio() => int.MaxValue;

	public int MaxTextureDepth() => MAX_TEXTURE_DIMENSION;

	public int MaxTextureHeight() => MAX_TEXTURE_DIMENSION;

	public int MaxTextureWidth() => MAX_TEXTURE_DIMENSION;

	public int MaxUserClipPlanes() => 0;

	public int MaxVertexShaderBlendMatrices() => 0;

	public int MaxViewports() => features?.MultipleViewports == true ? 16 : 1;

	public bool NeedsAAClamp() => false;

	public bool NeedsATICentroidHack() => false;

	public int NeedsShaderSRGBConversion() => 0;

	public int NumPixelShaderConstants() => 256;

	public int NumVertexShaderConstants() => 256;

	public void OverrideStreamOffsetSupport(bool overrideEnabled, bool enableSupport) { }

	public bool PreferDynamicTextures() => false;

	public bool PreferReducedFillrate() => false;

	public bool ReadPixelsFromFrontBuffer() => false;

	public void SetHDREnabled(bool enable) { }

	public bool SpecifiesFogColorInLinearSpace() => false;

	public int StencilBufferBits() => 8;

	public bool SupportsBorderColor() => true;

	public bool SupportsColorOnSecondStream() => true;

	public bool SupportsCompressedTextures() => true;

	public VertexCompressionType SupportsCompressedVertices() => VertexCompressionType.None;

	public bool SupportsCubeMaps() => true;

	public bool SupportsFetch4() => false;

	public bool SupportsGLMixedSizeTargets() => false;

	public bool SupportsHardwareLighting() => false;

	public bool SupportsHDR() => false;

	public bool SupportsHDRMode(HDRType hdrMode) => hdrMode == HDRType.None;

	public bool SupportsMipmappedCubemaps() => false;

	public bool SupportsNonPow2Textures() => true;

	public bool SupportsOverbright() => false;

	public bool SupportsPixelShaders_1_4() => true;

	public bool SupportsPixelShaders_2_0() => true;

	public bool SupportsPixelShaders_2_b() => true;

	public bool SupportsShaderModel_3_0() => true;

	public bool SupportsSpheremapping() => true;

	public bool SupportsSRGB() => true;

	public bool SupportsStaticControlFlow() => true;

	public bool SupportsStaticPlusDynamicLighting() => true;

	public bool SupportsStreamOffset() => true;

	public bool SupportsVertexAndPixelShaders() => true;

	public bool SupportsVertexShaders_2_0() => true;

	public nint TextureMemorySize() => 256 * 1024 * 1024;

	public bool UseFastClipping() => false;

	static readonly ConVar r_shader_srgb = new("r_shader_srgb", "0", 0, "-1 = use hardware caps. 0 = use hardware srgb. 1 = use shader srgb(software lookup)");
	public bool UsesSRGBCorrectBlending() => r_shader_srgb.GetInt() == 0;
}
