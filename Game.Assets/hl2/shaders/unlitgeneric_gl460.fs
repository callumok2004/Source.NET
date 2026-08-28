#version 460

in vec2 vs_TexCoord;
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

out vec4 fragColor;

void main()
{
    vec4 texelColor = texture(basetexture, vs_TexCoord);
    if(isAlphaTesting){
        if(alphaTestFunc == 0){ discard; }
        else if(alphaTestFunc == 1){ if(texelColor.a >= alphaTestRef){ discard; } }
        else if(alphaTestFunc == 2){ if(texelColor.a != alphaTestRef){ discard; } }
        else if(alphaTestFunc == 3){ if(texelColor.a > alphaTestRef){ discard; } }
        else if(alphaTestFunc == 4){ if(texelColor.a <= alphaTestRef){ discard; } }
        else if(alphaTestFunc == 5){ if(texelColor.a == alphaTestRef){ discard; } }
        else if(alphaTestFunc == 6){ if(texelColor.a < alphaTestRef){ discard; } }
    }

    fragColor = texelColor * vs_Color;
}