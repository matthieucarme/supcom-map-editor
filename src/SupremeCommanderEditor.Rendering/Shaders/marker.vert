
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec4 aColor;

uniform mat4 uViewProjection;
uniform float uPointSize;

out vec4 vColor;

void main()
{
    vColor = aColor;
    vec4 clipPos = uViewProjection * vec4(aPosition, 1.0);
    gl_Position = clipPos;

    // Scale point size by depth so markers stay visible
    float dist = clipPos.w;
    gl_PointSize = clamp(uPointSize / max(dist, 1.0), 4.0, 32.0);
}
