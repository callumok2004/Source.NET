#version 460
in vec2 vs_TexCoord0;
in vec2 vs_TexCoord1;
in vec4 vs_Color;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

const int VertexColor = 16;
const int VertexAlpha = 32;

layout(std140, binding = 1) uniform source_base_sharedUBO {
    int flags;
};
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 0) uniform texture2D basetexture_tex;
layout(set = 1, binding = 1) uniform sampler basetexture_smp;
#define basetexture sampler2D(basetexture_tex, basetexture_smp)
#else
layout(binding = 0) uniform sampler2D basetexture;
#endif
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 2) uniform texture2D basetexture2_tex;
layout(set = 1, binding = 3) uniform sampler basetexture2_smp;
#define basetexture2 sampler2D(basetexture2_tex, basetexture2_smp)
#else
layout(binding = 1) uniform sampler2D basetexture2;
#endif
#ifdef SOURCE_VULKAN
layout(set = 1, binding = 4) uniform texture2D lightmaptexture_tex;
layout(set = 1, binding = 5) uniform sampler lightmaptexture_smp;
#define lightmaptexture sampler2D(lightmaptexture_tex, lightmaptexture_smp)
#else
layout(binding = 2) uniform sampler2D lightmaptexture;
#endif

out vec4 fragColor;

void main()
{
    vec4 tex1Color = texture(basetexture, vs_TexCoord0);
    vec4 tex2Color = texture(basetexture2, vs_TexCoord0);
    
    vec4 texelColor = mix(tex1Color, tex2Color, vs_Color.a);
    
    vec4 lightmapColor = texture(lightmaptexture, vs_TexCoord1);

    if(isAlphaTesting){
        if(alphaTestFunc == 0){ discard; }
        else if(alphaTestFunc == 1){ if(texelColor.a >= alphaTestRef){ discard; } }
        else if(alphaTestFunc == 2){ if(texelColor.a != alphaTestRef){ discard; } }
        else if(alphaTestFunc == 3){ if(texelColor.a > alphaTestRef){ discard; } }
        else if(alphaTestFunc == 4){ if(texelColor.a <= alphaTestRef){ discard; } }
        else if(alphaTestFunc == 5){ if(texelColor.a == alphaTestRef){ discard; } }
        else if(alphaTestFunc == 6){ if(texelColor.a < alphaTestRef){ discard; } }
    }

    vec4 vertexColor = vec4(1.0, 1.0, 1.0, 1.0);

    if((flags & VertexColor) != 0){
        vertexColor.r = vs_Color.r;
        vertexColor.g = vs_Color.g;
        vertexColor.b = vs_Color.b;
    }

    if((flags & VertexAlpha) != 0){
        vertexColor.a = vs_Color.a;
    }

    fragColor = texelColor * vertexColor * lightmapColor * 2.2;
}