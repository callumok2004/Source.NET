#version 460
// STATIC: "BASETEXTURE"					"0..1"
// STATIC: "MULTITEXTURE"					"0..1"
// STATIC: "REFLECT"						"0..1"
// STATIC: "REFRACT"						"0..1"
// STATIC: "ABOVEWATER"						"0..1"
// STATIC: "BLURRY_REFRACT"					"0..1"

// When we turn NORMAL_DECODE_MODE on, this shader only needs 0..1, not 0..2
// STATIC: "NORMAL_DECODE_MODE"				"0..0"

// DYNAMIC: "PIXELFOGTYPE"					"0..1"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"		"0..1"

in vec2 vs_BumpTexCoord;
in vec3 vs_TangentEyeVect;
in vec4 vs_ReflectXY_RefractYX;
in float vs_W;
in vec4 vs_ProjPos;
in float vs_ScreenCoord;
#if MULTITEXTURE
in vec4 vs_ExtraBumpTexCoord;
#endif
#if BASETEXTURE
in vec4 vs_LightmapTexCoord1And2;
in vec4 vs_LightmapTexCoord3;
#endif
in vec4 vs_FogFactorW;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

out vec4 fragColor;

#include "common_water_gl460.fs"

#ifdef SOURCE_VULKAN
layout(set = 1, binding = 0) uniform texture2D RefractSampler_tex;
layout(set = 1, binding = 1) uniform sampler RefractSampler_smp;
#define RefractSampler sampler2D(RefractSampler_tex, RefractSampler_smp)
#else
layout(binding = 0) uniform sampler2D RefractSampler;
#endif
#if BASETEXTURE
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 2) uniform texture2D BaseTextureSampler_tex;
layout(set = 1, binding = 3) uniform sampler BaseTextureSampler_smp;
#define BaseTextureSampler sampler2D(BaseTextureSampler_tex, BaseTextureSampler_smp)
#else
layout(binding = 1) uniform sampler2D BaseTextureSampler;
#endif
#endif
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 4) uniform texture2D ReflectSampler_tex;
layout(set = 1, binding = 5) uniform sampler ReflectSampler_smp;
#define ReflectSampler sampler2D(ReflectSampler_tex, ReflectSampler_smp)
#else
layout(binding = 2) uniform sampler2D ReflectSampler;
#endif
#if BASETEXTURE
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 6) uniform texture2D LightmapSampler_tex;
layout(set = 1, binding = 7) uniform sampler LightmapSampler_smp;
#define LightmapSampler sampler2D(LightmapSampler_tex, LightmapSampler_smp)
#else
layout(binding = 3) uniform sampler2D LightmapSampler;
#endif
#endif
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 8) uniform texture2D NormalSampler_tex;
layout(set = 1, binding = 9) uniform sampler NormalSampler_smp;
#define NormalSampler sampler2D(NormalSampler_tex, NormalSampler_smp)
#else
layout(binding = 4) uniform sampler2D NormalSampler;
#endif

#define g_RefractTint			ps_const[1]
#define g_ReflectTint			ps_const[4]
#define g_ReflectRefractScale	ps_const[5] // xy - reflect scale, zw - refract scale
#define g_WaterFogColor			ps_const[6]
#define g_WaterFogParams		ps_const[7]

#define g_PixelFogParams		ps_const[8]

#define g_WaterFogStart			g_WaterFogParams.x
#define g_WaterFogEndMinusStart	g_WaterFogParams.y
#define g_Reflect_OverBright	g_WaterFogParams.z

void main()
{
    DrawWater_params_t params;

    params.vBumpTexCoord = vs_BumpTexCoord;
#if MULTITEXTURE
    params.vExtraBumpTexCoord = vs_ExtraBumpTexCoord;
#endif
    params.vReflectXY_vRefractYX = vs_ReflectXY_RefractYX;
    params.w = vs_W;
    params.vReflectRefractScale = g_ReflectRefractScale;
    params.fReflectOverbright = g_Reflect_OverBright;
    params.vReflectTint = g_ReflectTint;
    params.vRefractTint = g_RefractTint;
    params.vTangentEyeVect = vs_TangentEyeVect;
    params.waterFogColor = g_WaterFogColor;
#if BASETEXTURE
    params.lightmapTexCoord1And2 = vs_LightmapTexCoord1And2;
    params.lightmapTexCoord3 = vs_LightmapTexCoord3;
#endif
    params.vProjPos = vs_ProjPos;
    params.pixelFogParams = g_PixelFogParams;
    params.fWaterFogStart = g_WaterFogStart;
    params.fWaterFogEndMinusStart = g_WaterFogEndMinusStart;

    vec4 result;
    float fogFactor;
    DrawWater(params,
              // yay. . can't put sampler in a struct.
#if BASETEXTURE
              TEX2D_ARG(BaseTextureSampler),
              TEX2D_ARG(LightmapSampler),
#endif
              TEX2D_ARG(NormalSampler), TEX2D_ARG(RefractSampler), TEX2D_ARG(ReflectSampler),
              result, fogFactor);

    fragColor = FinalOutput(vec4(result.rgb, 1.0), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_NONE, (WRITE_DEPTH_TO_DESTALPHA != 0), vs_ProjPos.z);
}
