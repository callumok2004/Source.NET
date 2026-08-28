#version 460

in vec4 vs_Color;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

layout(std140, binding = 1) uniform source_base_sharedUBO {
    int flags;
};

out vec4 fragColor;

void main()
{
    fragColor = vec4(1.0, 1.0, 1.0, 1.0);
}