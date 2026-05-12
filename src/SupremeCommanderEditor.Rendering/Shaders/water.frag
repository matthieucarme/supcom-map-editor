
uniform vec3 uWaterColor;
uniform float uAlpha;

out vec4 FragColor;

void main()
{
    // SupCom water SurfaceColor often defaults to a near-cyan that blends greenish over grass terrain.
    // Bias the result strongly toward a saturated blue so water reads as water on a minimap and in 3D.
    vec3 c = clamp(uWaterColor, 0.0, 1.0);
    vec3 blue = vec3(c.r * 0.15, c.g * 0.4, max(c.b, 0.85));
    FragColor = vec4(blue, uAlpha);
}
