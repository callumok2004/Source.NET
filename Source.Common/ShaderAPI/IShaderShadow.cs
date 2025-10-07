﻿using Source.Common.MaterialSystem;

namespace Source.Common.ShaderAPI;

public enum ShaderDepthFunc
{
	Never,
	Nearer,
	Equal,
	NearerOrEqual,
	Farther,
	NotEqual,
	FartherOrEqual,
	Always
}

public enum ShaderBlendFactor
{
	Zero,
	One,
	SrcColor,
	OneMinusSrcColor,
	SrcAlpha,
	OneMinusSrcAlpha,
	DstAlpha,
	OneMinusDstAlpha,
	DstColor,
	OneMinusDstColor,
	SrcAlphaSat,
	BothSrcAlpha,
	BothInvSrcAlpha,
}

public enum ShaderBlendOp
{
	Add,
	Subtract,
	RevSubtract,
	Min,
	Max
}

public enum ShaderAlphaFunc
{
	Never,
	Less,
	Equal,
	LessEqual,
	Greater,
	NotEqual,
	GreaterEqual,
	Always
}

public enum ShaderCompareFunc
{
	Never,
	Less,
	Equal,
	LessEqual,
	Greater,
	NotEqual,
	GreaterEqual,
	Always
}

public enum ShaderStencilFunc
{
	Never = 0,
	Less,
	Equal,
	LessEqual,
	Greater,
	NotEqual,
	GreaterEqual,
	Always
}

public enum ShaderStencilOp
{
	Keep = 0,
	Zero,
	SetToReference,
	IncrementClamp,
	DecrementClamp,
	invert,
	IncrementWrap,
	DecrementWrap,
}

public enum ShaderTexChannel
{
	Color = 0,
	Alpha
}

public enum ShaderPolyModeFace
{
	Front,
	Back,
	FrontAndBack,
}

public enum ShaderPolyMode
{
	Point,
	Line,
	Fill
}

public enum ShaderTexArg
{
	Texture = 0,
	VertexColor,
	SpecularColor,
	ConstantColor,
	PreviousStage,
	None,
	Zero,
	TextureAlpha,
	InvTextureAlpha,
	One,
}

public enum ShaderTexOp
{
	// DX5 shaders support these
	Modulate = 0,
	Modulate2x,
	Modulate4x,
	SelectArg1,
	SelectArg2,
	Disable,

	// DX6 shaders support these
	Add,
	Subtract,
	AddSigned2x,
	BlendConstantAlpha,
	BlendTextureAlpha,
	BlendPreviousStageAlpha,
	ModulateColorAddAlpha,
	ModulateInvColorAddAlpha,

	// DX7
	DotProduct3
}

public enum ShaderTexGenParam
{
	ObjectLinear,
	EyeLinear,
	SphereMap,
	CameraSpaceReflectionVector,
	CameraSpaceNormal
}

public enum ShaderFogMode
{
	Disabled = 0,
	Overbright,
	Black,
	Grey,
	FogColor,
	White,
	Num
}
public enum ShaderMaterialSource
{
	Material,
	Color1,
	Color2
}
public enum PolygonOffsetMode
{
	Disable = 0x0,
	Decal = 0x1,
	ShadowBias = 0x2,
	Reserved = 0x3
}
// We differ from Source heavily here.
// IShaderShadow becomes an object that every shader instance has.
// ie. every Material has its own shader object.
// It will then activate the shader object when its in use.
// This consists of various GL states and binding of UBOs.
public interface IShaderShadow
{
	void DepthFunc(ShaderDepthFunc depthFunc);
	void EnableDepthWrites(bool enable);
	void EnableDepthTest(bool enable);
	void EnablePolyOffset(PolygonOffsetMode offsetMode);

	// Suppresses/activates color writing 
	void EnableColorWrites(bool enable);
	void EnableAlphaWrites(bool enable);

	// Methods related to alpha blending
	void EnableBlending(bool enable);
	void BlendFunc(ShaderBlendFactor srcFactor, ShaderBlendFactor dstFactor);

	// Alpha testing
	void EnableAlphaTest(bool enable);
	void AlphaFunc(ShaderAlphaFunc alphaFunc, float alphaRef /* [0-1] */ );

	// Wireframe/filled polygons
	void PolyMode(ShaderPolyModeFace face, ShaderPolyMode polyMode);

	// Back face culling
	void EnableCulling(bool enable);

	// constant color + transparency
	void EnableConstantColor(bool enable);

	// Indicates the vertex format for use with a vertex shader
	// The flags to pass in here come from the VertexFormatFlags_t enum
	// If pTexCoordDimensions is *not* specified, we assume all coordinates
	// are 2-dimensional
	void VertexShaderVertexFormat(VertexFormat format, int texCoordCount, Span<int> texCoordDimensions, int userDataSize);

	// Pixel and vertex shader methods
	void SetVertexShader(ReadOnlySpan<char> fileName);
	void SetPixelShader(ReadOnlySpan<char> fileName);

	// Todo.
	void EnableVertexBlend(bool enable);

	// per texture unit stuff
	void OverbrightValue(TextureStage stage, float value);
	void EnableTexture(Sampler sampler, bool enable);
	void EnableTexGen(TextureStage stage, bool enable);
	void TexGen(TextureStage stage, ShaderTexGenParam param);

	// alternate method of specifying per-texture unit stuff, more flexible and more complicated
	// Can be used to specify different operation per channel (alpha/color)...
	void EnableCustomPixelPipe(bool enable);
	void CustomTextureStages(int stageCount);
	void CustomTextureOperation(TextureStage stage, ShaderTexChannel channel, ShaderTexOp op, ShaderTexArg arg1, ShaderTexArg arg2);

	// A simpler method of dealing with alpha modulation
	void EnableAlphaPipe(bool enable);
	void EnableConstantAlpha(bool enable);
	void EnableVertexAlpha(bool enable);
	void EnableTextureAlpha(TextureStage stage, bool enable);

	// GR - Separate alpha blending
	void EnableBlendingSeparateAlpha(bool enable);
	void BlendFuncSeparateAlpha(ShaderBlendFactor srcFactor, ShaderBlendFactor dstFactor);
	void FogMode(ShaderFogMode fogMode);

	void SetDiffuseMaterialSource(ShaderMaterialSource materialSource);
	void SetShaderUniform(IMaterialVar textureVar);

	// Indicates the morph format for use with a vertex shader
	// The flags to pass in here come from the MorphFormatFlags_t enum
	// void SetMorphFormat(MorphFormat flags);

	void DisableFogGammaCorrection(bool bDisable); //some blending modes won't work properly with corrected fog

	// Alpha to coverage
	void EnableAlphaToCoverage(bool enable);

	// Shadow map filtering
	void SetShadowDepthFiltering(Sampler stage);

	// More alpha blending state
	void BlendOp(ShaderBlendOp blendOp);
	void BlendOpSeparateAlpha(ShaderBlendOp blendOp);
	GraphicsDriver GetDriver();
	void SetDefaultState();
	VertexFormat GetVertexFormat();
	void Activate();
}
