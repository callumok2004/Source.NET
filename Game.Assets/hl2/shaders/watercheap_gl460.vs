#version 460
// STATIC: "BLEND"		"0..1"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 4) in vec3 v_TangentS;
layout(location = 5) in vec3 v_TangentT;
layout(location = 10) in vec2 v_TexCoord0;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 2) uniform source_vertex_sharedUBO {
    int numBones;
    int lightCount;
    int vertexSharedPad0;
    int vertexSharedPad1;
    vec4 lightEnabled;
};

layout(std140, binding = 4) uniform source_bone_matrices {
    mat4 bones[256];
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

const int VERTEX_SHADER_CAMERA_POS = 2;
const int VERTEX_SHADER_AMBIENT_LIGHT = 21;
const int VERTEX_SHADER_LIGHT_INFO = 27;
const int VERTEX_SHADER_MODULATION_COLOR = 47;
const int SHADER_SPECIFIC_CONST_0 = 48;
const int SHADER_SPECIFIC_CONST_3 = 51;

#include "common_gl460.vs"

#define VSHADER_VECT_SCALE 1.0

#define cNormalMapTransform0	vs_const[SHADER_SPECIFIC_CONST_0 + 0]
#define cNormalMapTransform1	vs_const[SHADER_SPECIFIC_CONST_0 + 1]
#define TexOffsets				vs_const[SHADER_SPECIFIC_CONST_3]

out vec2 vs_NormalMapTexCoord;
out vec3 vs_WorldVertToEyeVector;
out mat3 vs_TangentSpaceTranspose;
out vec4 vs_Refract_W_ProjZ;
out vec4 vs_ExtraBumpTexCoord;
out vec4 vs_FogFactorW;

void main()
{
    vs_Refract_W_ProjZ = vec4(0.0);

    vec3 vObjNormal = v_Normal;

    vec4 projPos;
    vec3 worldPos;

    worldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;
    projPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = projPos;

#if BLEND
    // Map projected position to the reflection texture
    vs_Refract_W_ProjZ.x = projPos.x;
    vs_Refract_W_ProjZ.y = -projPos.y; // invert Y
    vs_Refract_W_ProjZ.xy = (vs_Refract_W_ProjZ.xy + projPos.w) * 0.5;
    vs_Refract_W_ProjZ.z = projPos.w;
#endif

    vs_Refract_W_ProjZ.w = projPos.z;

    vec3 worldTangentS = mat3(modelMatrix) * v_TangentS;
    vec3 worldTangentT = mat3(modelMatrix) * v_TangentT;
    vec3 worldNormal = mat3(modelMatrix) * vObjNormal;
    vs_TangentSpaceTranspose[0] = worldTangentS;
    vs_TangentSpaceTranspose[1] = worldTangentT;
    vs_TangentSpaceTranspose[2] = worldNormal;

    vec3 worldVertToEyeVector = VSHADER_VECT_SCALE * (cEyePos - worldPos);
    vs_WorldVertToEyeVector = worldVertToEyeVector;

    // FIXME: need to add a normalMapTransform to all of the water shaders.
    //vs_NormalMapTexCoord.x = dot（v_TexCoord0, cNormalMapTransform0.xy) + cNormalMapTransform0.w;
    //vs_NormalMapTexCoord.y = dot( v_TexCoord0, cNormalMapTransform1.xy) + cNormalMapTransform1.w;
    vs_NormalMapTexCoord = v_TexCoord0;

    float f45x = v_TexCoord0.x + v_TexCoord0.y;
    float f45y = v_TexCoord0.y - v_TexCoord0.x;
    vs_ExtraBumpTexCoord.x = f45x * 0.1 + TexOffsets.x;
    vs_ExtraBumpTexCoord.y = f45y * 0.1 + TexOffsets.y;
    vs_ExtraBumpTexCoord.z = v_TexCoord0.y * 0.45 + TexOffsets.z;
    vs_ExtraBumpTexCoord.w = v_TexCoord0.x * 0.45 + TexOffsets.w;

    vs_FogFactorW = vec4(CalcFog(worldPos, projPos.xyz, FOGTYPE_RANGE));
}
