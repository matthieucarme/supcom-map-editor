
in vec4 vColor;
out vec4 FragColor;

void main()
{
    // Circle shape from point sprite
    vec2 center = gl_PointCoord - vec2(0.5);
    float dist = dot(center, center);
    if (dist > 0.25)
        discard;

    // Soft edge
    float alpha = smoothstep(0.25, 0.2, dist) * vColor.a;
    FragColor = vec4(vColor.rgb, alpha);
}
