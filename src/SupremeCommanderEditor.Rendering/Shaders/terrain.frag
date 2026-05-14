
in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vTexCoord;
in float vHeight;

uniform vec3 uSunDirection;
uniform vec3 uSunColor;
uniform vec3 uAmbientColor;
uniform float uHeightMin;
uniform float uHeightMax;
uniform float uLightingMultiplier;
uniform int uRenderMode;
uniform int uHasTextures;
uniform int uCartographicMode; // 0=off, 1=on (overrides textured/gradient render)
uniform float uWaterElevation; // world-Y water plane, used for the shoreline tint

uniform vec2 uBrushPos;
uniform float uBrushRadius;

uniform sampler2D uStratum0, uStratum1, uStratum2, uStratum3, uStratum4;
uniform sampler2D uStratum5, uStratum6, uStratum7, uStratum8, uStratum9;
uniform float uScale0, uScale1, uScale2, uScale3, uScale4;
uniform float uScale5, uScale6, uScale7, uScale8, uScale9;
uniform int uStratumCount;

uniform sampler2D uSplatLow;
uniform sampler2D uSplatHigh;
uniform int uTerrainShaderType;
uniform int uUpperLayerIndex; // index of the upper/macro layer (5 for v53, 9 for v56)

out vec4 FragColor;

vec3 heightToColor(float h)
{
    float t = clamp((h - uHeightMin) / max(uHeightMax - uHeightMin, 0.01), 0.0, 1.0);
    vec3 low = vec3(0.15, 0.35, 0.1);
    vec3 mid = vec3(0.4, 0.35, 0.2);
    vec3 high = vec3(0.6, 0.55, 0.45);
    vec3 peak = vec3(0.9, 0.9, 0.85);
    if (t < 0.3) return mix(low, mid, t / 0.3);
    if (t < 0.7) return mix(mid, high, (t - 0.3) / 0.4);
    return mix(high, peak, (t - 0.7) / 0.3);
}

// Stylised topographic palette + contour lines. Lighting is still applied afterwards
// so the 3D shape stays readable — the cartographic view is meant to make texture-
// painting easier, not flatten the terrain into a 2D minimap.
vec3 cartographicColor(float h)
{
    float range = max(uHeightMax - uHeightMin, 0.01);
    float t = clamp((h - uHeightMin) / range, 0.0, 1.0);

    // Cream → tan → brown → snow ramp. Higher contrast than the default height palette.
    vec3 c0 = vec3(0.94, 0.92, 0.78);
    vec3 c1 = vec3(0.86, 0.80, 0.58);
    vec3 c2 = vec3(0.72, 0.60, 0.40);
    vec3 c3 = vec3(0.55, 0.42, 0.28);
    vec3 c4 = vec3(0.96, 0.96, 0.96);
    vec3 col;
    if (t < 0.25)      col = mix(c0, c1, t / 0.25);
    else if (t < 0.5)  col = mix(c1, c2, (t - 0.25) / 0.25);
    else if (t < 0.75) col = mix(c2, c3, (t - 0.5) / 0.25);
    else               col = mix(c3, c4, (t - 0.75) / 0.25);

    // Shoreline tint just above water level: a soft sand band so the coastline reads instantly.
    if (h > uWaterElevation && h < uWaterElevation + range * 0.04)
        col = mix(col, vec3(0.98, 0.94, 0.74), 0.6);

    // Contour lines: 10 evenly-spaced bands across the height range. Width modulated by
    // screen-space derivative so the lines stay roughly 1px thick at any zoom.
    float bands = 10.0;
    float bandPos = t * bands;
    float aa = max(fwidth(bandPos), 0.001);
    float lineDist = abs(fract(bandPos) - 0.5);
    float line = smoothstep(0.5 - aa * 1.5, 0.5 - aa * 0.5, lineDist);
    col = mix(col, col * 0.45, line * 0.8);

    return col;
}

void main()
{
    vec3 color;

    if (uCartographicMode != 0)
    {
        color = cartographicColor(vHeight);
    }
    else if (uRenderMode == 1 && uHasTextures != 0 && uStratumCount > 0)
    {
        vec4 mask0 = texture(uSplatLow, vTexCoord);
        vec4 mask1 = texture(uSplatHigh, vTexCoord);

        if (uTerrainShaderType == 1)
        {
            mask0 = clamp(mask0 * 2.0 - 1.0, 0.0, 1.0);
            mask1 = clamp(mask1 * 2.0 - 1.0, 0.0, 1.0);
        }

        color = texture(uStratum0, vWorldPos.xz / max(uScale0, 0.1)).rgb;

        if (uStratumCount > 1)
            color = mix(color, texture(uStratum1, vWorldPos.xz / max(uScale1, 0.1)).rgb, mask0.r);
        if (uStratumCount > 2)
            color = mix(color, texture(uStratum2, vWorldPos.xz / max(uScale2, 0.1)).rgb, mask0.g);
        if (uStratumCount > 3)
            color = mix(color, texture(uStratum3, vWorldPos.xz / max(uScale3, 0.1)).rgb, mask0.b);
        if (uStratumCount > 4)
            color = mix(color, texture(uStratum4, vWorldPos.xz / max(uScale4, 0.1)).rgb, mask0.a);
        // Layers 5-8 blend using mask1 (only for 10-strata maps)
        if (uStratumCount > 5 && uUpperLayerIndex > 5)
            color = mix(color, texture(uStratum5, vWorldPos.xz / max(uScale5, 0.1)).rgb, mask1.r);
        if (uStratumCount > 6 && uUpperLayerIndex > 6)
            color = mix(color, texture(uStratum6, vWorldPos.xz / max(uScale6, 0.1)).rgb, mask1.g);
        if (uStratumCount > 7 && uUpperLayerIndex > 7)
            color = mix(color, texture(uStratum7, vWorldPos.xz / max(uScale7, 0.1)).rgb, mask1.b);
        if (uStratumCount > 8 && uUpperLayerIndex > 8)
            color = mix(color, texture(uStratum8, vWorldPos.xz / max(uScale8, 0.1)).rgb, mask1.a);

        // Upper/macro layer: uses its own alpha channel as blend weight
        if (uUpperLayerIndex < uStratumCount)
        {
            vec4 upper;
            if (uUpperLayerIndex == 5) upper = texture(uStratum5, vWorldPos.xz / max(uScale5, 0.1));
            else if (uUpperLayerIndex == 6) upper = texture(uStratum6, vWorldPos.xz / max(uScale6, 0.1));
            else if (uUpperLayerIndex == 7) upper = texture(uStratum7, vWorldPos.xz / max(uScale7, 0.1));
            else if (uUpperLayerIndex == 8) upper = texture(uStratum8, vWorldPos.xz / max(uScale8, 0.1));
            else upper = texture(uStratum9, vWorldPos.xz / max(uScale9, 0.1));
            color = mix(color, upper.rgb, upper.a);
        }
    }
    else
    {
        color = heightToColor(vHeight);
    }

    // sRGB → linear (SupCom textures are in sRGB). Skip when the cartographic palette
    // is driving the color — that ramp is already authored in display space.
    if (uRenderMode == 1 && uCartographicMode == 0) color = pow(color, vec3(0.55));

    // Lighting — favor brightness over physical accuracy so the map is readable both from above
    // (ortho minimap capture) and from a 3D angle. Sun adds directional shading, but a generous
    // ambient floor keeps surfaces lit even when nearly perpendicular to the sun.
    vec3 N = normalize(vNormal);
    vec3 L = normalize(-uSunDirection);
    float NdotL = max(dot(N, L), 0.0);
    float mult = max(uLightingMultiplier, 1.6);
    vec3 ambient = max(uAmbientColor, vec3(0.6));
    vec3 lit = color * (ambient + uSunColor * NdotL * 0.6) * mult;

    // Brush cursor
    if (uBrushRadius > 0.0)
    {
        float dist = length(vWorldPos.xz - uBrushPos);
        float ring = abs(dist - uBrushRadius);
        float thickness = max(1.0, uBrushRadius * 0.04);
        float angle = atan(vWorldPos.z - uBrushPos.y, vWorldPos.x - uBrushPos.x);
        float dashes = step(0.5, fract(angle * 3.0 / 3.14159));
        if (ring < thickness && dashes > 0.0)
            lit = mix(lit, vec3(1.0, 1.0, 0.2), 0.9);
        if (dist < uBrushRadius)
            lit = mix(lit, vec3(1.0, 1.0, 0.5), (1.0 - dist / uBrushRadius) * 0.08);
    }

    FragColor = vec4(clamp(lit, 0.0, 1.0), 1.0);
}
