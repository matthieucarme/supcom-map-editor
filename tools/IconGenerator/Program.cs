using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.RegularExpressions;
using Pfim;
using SkiaSharp;

// Batch generator: scan vanilla `env.scd`, render every `_prop.bp` mesh + albedo into a PNG.
// Output goes into the repo so the icons are checked in and embedded as resources at compile time.

// usage: dotnet run -- <env.scd> <units.scd> <renderingProjectDir>
// Writes:   <renderingProjectDir>/PropIcons/{biome}_{basename}.png   (from env.scd)
//           <renderingProjectDir>/UnitIcons/{basename}.png            (from units.scd)
if (args.Length < 3) { System.Console.WriteLine("usage: dotnet run -- <env.scd> <units.scd> <renderingProjectDir>"); return; }
var envScdPath = args[0];
var unitsScdPath = args[1];
var rootOutDir = args[2];
var propOutDir = Path.Combine(rootOutDir, "PropIcons");
var unitOutDir = Path.Combine(rootOutDir, "UnitIcons");
Directory.CreateDirectory(propOutDir);
Directory.CreateDirectory(unitOutDir);

// 192 = 2x the on-screen card (48-64px). Keeps icons sharp on HiDPI displays where the OS
// scales 2x; on regular displays Avalonia downscales with bilinear filtering and still looks
// crisp. ~3-4 MB total embedded — fine for a self-contained .exe.
const int IconSize = 192;
var albedoRx = new Regex(@"AlbedoName\s*=\s*'([^']+)'");

ProcessProps(envScdPath, propOutDir, albedoRx);
ProcessUnits(unitsScdPath, envScdPath, unitOutDir, albedoRx);

static void ProcessProps(string scdPath, string outDir, Regex albedoRx)
{
    System.Console.WriteLine($"=== Props from {Path.GetFileName(scdPath)} ===");
    using var zip = ZipFile.OpenRead(scdPath);
    var byLower = new Dictionary<string, ZipArchiveEntry>(System.StringComparer.OrdinalIgnoreCase);
    foreach (var e in zip.Entries) byLower[e.FullName.Replace('\\', '/')] = e;

    int ok = 0, skipNoMesh = 0, skipNoAlbedo = 0, skipErr = 0;
    foreach (var entry in zip.Entries) {
        if (!entry.FullName.EndsWith("_prop.bp", System.StringComparison.OrdinalIgnoreCase)) continue;
        var bpFull = entry.FullName.Replace('\\', '/');
        if (!bpFull.StartsWith("env/", System.StringComparison.OrdinalIgnoreCase)) continue;

        var dir = Path.GetDirectoryName(bpFull)!.Replace('\\', '/');
        var bpName = Path.GetFileName(bpFull);
        var baseName = bpName.Substring(0, bpName.Length - "_prop.bp".Length);

        var scmKey = $"{dir}/{baseName}_lod0.scm";
        if (!byLower.TryGetValue(scmKey, out var scmEntry)) { skipNoMesh++; continue; }

        string? albedoName;
        using (var bpReader = new StreamReader(entry.Open())) {
            var bpText = bpReader.ReadToEnd();
            var m = albedoRx.Match(bpText);
            albedoName = m.Success ? m.Groups[1].Value : $"{baseName}_albedo.dds";
        }
        var ddsKey = NormalizePath(albedoName.StartsWith('/') ? albedoName.TrimStart('/') : $"{dir}/{albedoName}");
        if (!byLower.TryGetValue(ddsKey, out var ddsEntry)) { skipNoAlbedo++; continue; }

        var parts = bpFull.Split('/');
        var biome = parts[1].ToLowerInvariant();
        var slug = $"{biome}_{baseName.ToLowerInvariant()}";
        var outPath = Path.Combine(outDir, $"{slug}.png");
        try {
            byte[] scmBytes; using (var s = scmEntry.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); scmBytes = ms.ToArray(); }
            byte[] ddsBytes; using (var s = ddsEntry.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); ddsBytes = ms.ToArray(); }
            RenderIcon(scmBytes, ddsBytes, outPath, IconSize);
            ok++;
            if (ok % 50 == 0) System.Console.WriteLine($"  …{ok} props");
        } catch (System.Exception ex) { skipErr++; System.Console.WriteLine($"  ERR {slug}: {ex.Message}"); }
    }
    System.Console.WriteLine($"Props done. ok={ok}  noMesh={skipNoMesh}  noAlbedo={skipNoAlbedo}  err={skipErr}");
}

static void ProcessUnits(string unitsScdPath, string envScdPath, string outDir, Regex albedoRx)
{
    System.Console.WriteLine($"=== Units from {Path.GetFileName(unitsScdPath)} ===");
    // Combine both archives' entries in one lookup. The unit .bp can reference assets in either
    // (e.g. OPC1001 uses MeshName='/Env/Structures/Props/UEF_Warehouse01_lod0.scm' from env.scd).
    using var unitsZip = ZipFile.OpenRead(unitsScdPath);
    using var envZip = ZipFile.OpenRead(envScdPath);
    var byLower = new Dictionary<string, (ZipArchiveEntry Entry, ZipArchive Owner)>(System.StringComparer.OrdinalIgnoreCase);
    foreach (var e in unitsZip.Entries) byLower[e.FullName.Replace('\\', '/')] = (e, unitsZip);
    foreach (var e in envZip.Entries)
    {
        var key = e.FullName.Replace('\\', '/');
        if (!byLower.ContainsKey(key)) byLower[key] = (e, envZip); // unit-archive entry wins on overlap (rare)
    }
    // Word-boundary: avoid matching "PlaceholderMeshName" which is a totally different field.
    var meshRx = new Regex(@"\bMeshName\s*=\s*'([^']+)'");

    int ok = 0, skipNoMesh = 0, skipNoAlbedo = 0, skipErr = 0;
    foreach (var entry in unitsZip.Entries) {
        if (!entry.FullName.EndsWith("_unit.bp", System.StringComparison.OrdinalIgnoreCase)) continue;
        var bpFull = entry.FullName.Replace('\\', '/');
        if (!bpFull.StartsWith("units/", System.StringComparison.OrdinalIgnoreCase)) continue;

        var dir = Path.GetDirectoryName(bpFull)!.Replace('\\', '/');
        var bpName = Path.GetFileName(bpFull);
        var baseName = bpName.Substring(0, bpName.Length - "_unit.bp".Length);

        // Read .bp and look for MeshName / AlbedoName overrides. Most units don't list them
        // (engine uses the convention) but campaign/civilian units (OPCxxxx, OPExxxx) often borrow
        // a mesh from env.scd via an absolute "/Env/..." path.
        string bpText;
        using (var r = new StreamReader(entry.Open())) bpText = r.ReadToEnd();
        var mMesh = meshRx.Match(bpText);
        var mAlb = albedoRx.Match(bpText);
        var meshName = mMesh.Success ? mMesh.Groups[1].Value : $"{baseName}_LOD0.scm";
        var albedoName = mAlb.Success ? mAlb.Groups[1].Value : $"{baseName}_Albedo.dds";

        // <none> sentinel means the unit has no mesh (invisible wall, etc.) — skip.
        if (meshName.Equals("<none>", System.StringComparison.OrdinalIgnoreCase)) { skipNoMesh++; continue; }

        // Resolve to archive paths. Absolute (start with '/') → root-relative; relative → next to the .bp.
        var scmKey = NormalizePath(meshName.StartsWith('/') ? meshName.TrimStart('/') : $"{dir}/{meshName}");
        var ddsKey = NormalizePath(albedoName.StartsWith('/') ? albedoName.TrimStart('/') : $"{dir}/{albedoName}");

        if (!byLower.TryGetValue(scmKey, out var scmRef)) { skipNoMesh++; continue; }
        if (!byLower.TryGetValue(ddsKey, out var ddsRef)) { skipNoAlbedo++; continue; }

        var slug = baseName.ToLowerInvariant();
        var outPath = Path.Combine(outDir, $"{slug}.png");
        try {
            byte[] scmBytes; using (var s = scmRef.Entry.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); scmBytes = ms.ToArray(); }
            byte[] ddsBytes; using (var s = ddsRef.Entry.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); ddsBytes = ms.ToArray(); }
            RenderIcon(scmBytes, ddsBytes, outPath, IconSize);
            ok++;
            if (ok % 50 == 0) System.Console.WriteLine($"  …{ok} units");
        } catch (System.Exception ex) { skipErr++; System.Console.WriteLine($"  ERR {slug}: {ex.Message}"); }
    }
    System.Console.WriteLine($"Units done. ok={ok}  noMesh={skipNoMesh}  noAlbedo={skipNoAlbedo}  err={skipErr}");
}

static void RenderIcon(byte[] scmBytes, byte[] ddsBytes, string outPath, int size)
{
    // ---- Parse SCM ----
    using var br = new BinaryReader(new MemoryStream(scmBytes));
    if (new string(br.ReadChars(4)) != "MODL") throw new System.Exception("not MODL");
    br.ReadInt32(); br.ReadInt32(); br.ReadInt32();
    int vertOffset = br.ReadInt32(); br.ReadInt32();
    int vertexCount = br.ReadInt32();
    int indexOffset = br.ReadInt32();
    int indexCount = br.ReadInt32();

    var pos = new Vector3[vertexCount];
    var nrm = new Vector3[vertexCount];
    var uvs = new Vector2[vertexCount];
    br.BaseStream.Position = vertOffset;
    for (int i = 0; i < vertexCount; i++) {
        pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        br.BaseStream.Position += 24; // tangent + binormal
        uvs[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
        br.BaseStream.Position += 12; // uv1 + bone
    }
    br.BaseStream.Position = indexOffset;
    var idx = new int[indexCount];
    for (int i = 0; i < indexCount; i++) idx[i] = br.ReadUInt16();

    // ---- Load DDS ----
    using var dds = Pfimage.FromStream(new MemoryStream(ddsBytes));
    int texW = dds.Width, texH = dds.Height;
    byte[] tex = new byte[texW * texH * 4];
    int stride = dds.Stride;
    var src = dds.Data;
    int bpp;
    if (dds.Format == ImageFormat.Rgba32) bpp = 4;
    else if (dds.Format == ImageFormat.Rgb24) bpp = 3;
    else throw new System.Exception($"unsupported DDS format {dds.Format}");
    for (int y = 0; y < texH; y++)
        for (int x = 0; x < texW; x++) {
            int s = y * stride + x * bpp;
            int d = (y * texW + x) * 4;
            tex[d + 0] = src[s + 2];
            tex[d + 1] = src[s + 1];
            tex[d + 2] = src[s + 0];
            tex[d + 3] = bpp == 4 ? src[s + 3] : (byte)255;
        }

    // ---- Camera ----
    Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
    for (int i = 0; i < vertexCount; i++) { bmin = Vector3.Min(bmin, pos[i]); bmax = Vector3.Max(bmax, pos[i]); }
    var center = (bmin + bmax) * 0.5f;
    var sz = bmax - bmin;
    // 0.5 = vertex at max-extent lands at 90% of the canvas → tight crop with ~5% margin.
    // Lower this further if a model still feels lost in whitespace.
    float radius = System.MathF.Max(System.MathF.Max(sz.X, sz.Y), sz.Z) * 0.5f;
    if (radius < 1e-4f) radius = 1f;
    var view = Matrix4x4.CreateTranslation(-center) *
               Matrix4x4.CreateRotationY(35f * System.MathF.PI / 180f) *
               Matrix4x4.CreateRotationX(-25f * System.MathF.PI / 180f);

    int W = size, H = size;
    var color = new byte[W * H * 4];
    var depth = new float[W * H];
    for (int i = 0; i < depth.Length; i++) depth[i] = float.PositiveInfinity;
    var lightDir = Vector3.Normalize(new Vector3(0.35f, 0.85f, -0.4f));

    Vector3 P(Vector3 p) {
        var v = Vector3.Transform(p, view);
        return new Vector3((v.X / radius) * (W * 0.45f) + W * 0.5f,
                           -(v.Y / radius) * (H * 0.45f) + H * 0.5f, v.Z);
    }

    for (int t = 0; t < indexCount; t += 3) {
        int i0 = idx[t], i1 = idx[t+1], i2 = idx[t+2];
        var s0 = P(pos[i0]); var s1 = P(pos[i1]); var s2 = P(pos[i2]);
        float area = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
        if (System.MathF.Abs(area) < 0.5f) continue;
        if (area < 0) { (s1, s2) = (s2, s1); (i1, i2) = (i2, i1); area = -area; }
        int xmin = System.Math.Max(0, (int)System.MathF.Floor(System.MathF.Min(s0.X, System.MathF.Min(s1.X, s2.X))));
        int ymin = System.Math.Max(0, (int)System.MathF.Floor(System.MathF.Min(s0.Y, System.MathF.Min(s1.Y, s2.Y))));
        int xmax = System.Math.Min(W - 1, (int)System.MathF.Ceiling(System.MathF.Max(s0.X, System.MathF.Max(s1.X, s2.X))));
        int ymax = System.Math.Min(H - 1, (int)System.MathF.Ceiling(System.MathF.Max(s0.Y, System.MathF.Max(s1.Y, s2.Y))));
        var n0v = Vector3.TransformNormal(nrm[i0], view);
        var n1v = Vector3.TransformNormal(nrm[i1], view);
        var n2v = Vector3.TransformNormal(nrm[i2], view);
        var uv0 = uvs[i0]; var uv1 = uvs[i1]; var uv2 = uvs[i2];

        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++) {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = (s1.X - s0.X) * (py - s0.Y) - (s1.Y - s0.Y) * (px - s0.X);
                float w1 = (s2.X - s1.X) * (py - s1.Y) - (s2.Y - s1.Y) * (px - s1.X);
                float w2 = (s0.X - s2.X) * (py - s2.Y) - (s0.Y - s2.Y) * (px - s2.X);
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float b0 = w1 / area, b1 = w2 / area, b2 = w0 / area;
                float z = s0.Z * b0 + s1.Z * b1 + s2.Z * b2;
                int didx = y * W + x;
                if (z >= depth[didx]) continue;
                var uv = uv0 * b0 + uv1 * b1 + uv2 * b2;
                float u = uv.X - System.MathF.Floor(uv.X); float v = uv.Y - System.MathF.Floor(uv.Y);
                int tx = System.Math.Clamp((int)(u * texW), 0, texW - 1);
                int ty = System.Math.Clamp((int)(v * texH), 0, texH - 1);
                int ti = (ty * texW + tx) * 4;
                if (tex[ti + 3] < 128) continue; // alpha cutoff for foliage
                depth[didx] = z;
                var n = Vector3.Normalize(n0v * b0 + n1v * b1 + n2v * b2);
                float lambert = System.MathF.Max(0, Vector3.Dot(n, lightDir));
                float intensity = (0.85f + 0.55f * lambert) * 1.6f;
                int ci = didx * 4;
                color[ci + 0] = (byte)System.Math.Clamp(tex[ti + 0] * intensity, 0, 255);
                color[ci + 1] = (byte)System.Math.Clamp(tex[ti + 1] * intensity, 0, 255);
                color[ci + 2] = (byte)System.Math.Clamp(tex[ti + 2] * intensity, 0, 255);
                color[ci + 3] = 255;
            }
    }

    using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
    System.Runtime.InteropServices.Marshal.Copy(color, 0, bmp.GetPixels(), color.Length);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.OpenWrite(outPath);
    data.SaveTo(fs);
}

static string NormalizePath(string path)
{
    // Collapse "/./" and "/../" segments. Used because some prop .bp's reference albedos via
    // a relative path with ".." (e.g. "../Pine06_V1_albedo.dds" inside Trees/Groups/).
    var stack = new System.Collections.Generic.List<string>();
    foreach (var seg in path.Split('/'))
    {
        if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
        else if (seg != "." && seg.Length > 0) stack.Add(seg);
    }
    return string.Join('/', stack);
}
