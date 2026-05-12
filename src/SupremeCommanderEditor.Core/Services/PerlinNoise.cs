namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Deterministic 2D Perlin noise — given a seed, the output is fully reproducible. Returns values
/// in roughly [-1, 1]. Multi-octave fractal noise via <see cref="OctaveNoise"/>.
/// </summary>
public class PerlinNoise
{
    private readonly int[] _perm = new int[512];

    public PerlinNoise(long seed)
    {
        // Build a deterministic permutation of 0..255 using a seeded RNG, then duplicate the
        // table to 512 entries so we can index with (perm[xi]+yi) without wrap-around.
        var table = new int[256];
        for (int i = 0; i < 256; i++) table[i] = i;
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        // Fisher-Yates shuffle.
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (table[i], table[j]) = (table[j], table[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = table[i & 255];
    }

    /// <summary>Single-octave 2D Perlin noise at (x, y). Output in [-1, 1].</summary>
    public float Noise(float x, float y)
    {
        int xi = (int)MathF.Floor(x) & 255;
        int yi = (int)MathF.Floor(y) & 255;
        float xf = x - MathF.Floor(x);
        float yf = y - MathF.Floor(y);
        float u = Fade(xf);
        float v = Fade(yf);

        int aa = _perm[_perm[xi]     + yi];
        int ab = _perm[_perm[xi]     + yi + 1];
        int ba = _perm[_perm[xi + 1] + yi];
        int bb = _perm[_perm[xi + 1] + yi + 1];

        float x1 = Lerp(Grad(aa, xf,     yf),     Grad(ba, xf - 1, yf),     u);
        float x2 = Lerp(Grad(ab, xf,     yf - 1), Grad(bb, xf - 1, yf - 1), u);
        return Lerp(x1, x2, v);
    }

    /// <summary>Sum multiple noise octaves with falling amplitude — gives natural-looking terrain
    /// (large rolling hills + medium bumps + small detail). Output normalised to [-1, 1].</summary>
    public float OctaveNoise(float x, float y, int octaves, float persistence)
    {
        float total = 0;
        float frequency = 1;
        float amplitude = 1;
        float maxAmplitude = 0;
        for (int i = 0; i < octaves; i++)
        {
            total += Noise(x * frequency, y * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }
        return total / maxAmplitude;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y)
    {
        // 4 gradient directions encoded in the low 2 bits of hash. Good enough for 2D.
        int h = hash & 3;
        float u = h < 2 ? x : y;
        float v = h < 2 ? y : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
