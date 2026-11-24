<#
.SYNOPSIS
    Télécharge les personnages réalistes d'Ivory State (Microsoft Rocketbox, licence MIT) et les
    prépare pour le mobile.

.DESCRIPTION
    - Avatars : maillage FBX (squelette Bip01, compatible Humanoid) + textures de couleur, de relief
      et d'opacité (cheveux, cils).
    - Textures réduites de 2048 à 1024 px (TGA de 12 Mo -> JPEG/PNG de quelques centaines de Ko) :
      les TGA d'origine ne sont jamais copiés dans le projet.
    - Teint : Rocketbox n'a pas de femme noire adulte, et deux des hommes retenus ont le teint
      clair. La peau de ces cinq avatars (Dark = $true) est assombrie : détection des tons chair,
      ombrage conservé. Les vêtements ne changent pas.
    - Animations : marche, course et attente, en place (déplacement extrait), hommes et femmes.

    Destination : IvoryState/Assets/_Project/ThirdParty/Rocketbox/. Idempotent.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'IvoryState\Assets\_Project\ThirdParty\Rocketbox'
$cache = Join-Path $env:TEMP 'ivory-rocketbox'
$raw = 'https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets'

# Avatar -> préfixe des textures ; Dark = assombrir la peau.
$avatars = @(
    @{ Name = 'Male_Adult_04';   Prefix = 'm006'; Dark = $true },
    @{ Name = 'Male_Adult_12';   Prefix = 'm007'; Dark = $true },
    @{ Name = 'Male_Adult_17';   Prefix = 'm022'; Dark = $false },
    @{ Name = 'Male_Adult_18';   Prefix = 'm023'; Dark = $false },
    @{ Name = 'Female_Adult_03'; Prefix = 'f003'; Dark = $true },
    @{ Name = 'Female_Adult_12'; Prefix = 'f012'; Dark = $true },
    @{ Name = 'Female_Adult_15'; Prefix = 'f018'; Dark = $true }
)

$animations = @(
    'all_animations_max_motextr_static/m_idle_neutral_01.max.fbx',
    'all_animations_max_motextr_static/f_idle_neutral_01.max.fbx',
    'all_animations_max_motextr_xy/m_walk_neutral_01.max.fbx',
    'all_animations_max_motextr_xy/f_walk_neutral_01.max.fbx',
    'all_animations_max_motextr_xy/m_run_neutral_01.max.fbx',
    'all_animations_max_motextr_xy/f_run_neutral_01.max.fbx'
)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class IvoryTga
{
    // Lit un TGA (24/32 bits, brut ou RLE).
    public static Bitmap Load(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        int idLength = d[0], type = d[2];
        int w = d[12] | (d[13] << 8), h = d[14] | (d[15] << 8), bpp = d[16] / 8;
        bool topLeft = (d[17] & 0x20) != 0;
        int pos = 18 + idLength;
        var px = new byte[w * h * 4];
        int count = w * h, i = 0;
        while (i < count)
        {
            if (type == 10)
            {
                int header = d[pos++];
                int n = (header & 0x7F) + 1;
                if ((header & 0x80) != 0)
                {
                    for (int k = 0; k < n; k++) Put(px, i++, d, pos, bpp);
                    pos += bpp;
                }
                else
                {
                    for (int k = 0; k < n; k++) { Put(px, i++, d, pos, bpp); pos += bpp; }
                }
            }
            else
            {
                Put(px, i++, d, pos, bpp); pos += bpp;
            }
        }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
        {
            int srcRow = topLeft ? y : h - 1 - y;
            System.Runtime.InteropServices.Marshal.Copy(px, srcRow * w * 4, data.Scan0 + y * data.Stride, w * 4);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    private static void Put(byte[] px, int i, byte[] d, int pos, int bpp)
    {
        px[i * 4] = d[pos]; px[i * 4 + 1] = d[pos + 1]; px[i * 4 + 2] = d[pos + 2];
        px[i * 4 + 3] = bpp == 4 ? d[pos + 3] : (byte)255;
    }

    public static Bitmap Resize(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(src, 0, 0, size, size);
        }
        return dst;
    }

    // Assombrit les tons chair (teinte orangée, saturation moyenne) en conservant l'ombrage.
    public static void DarkenSkin(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
        for (int i = 0; i < px.Length; i += 4)
        {
            float b = px[i] / 255f, g = px[i + 1] / 255f, r = px[i + 2] / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max < 0.2f || r < g || g < b * 0.85f) continue;
            float sat = (max - min) / max;
            float hue = 60f * ((g - b) / Math.Max(max - min, 1e-4f)); // 0 = rouge, 60 = jaune
            // Poids : teinte 5-45°, saturation 0,12-0,65, valeur > 0,3.
            float wHue = Clamp01(1f - Math.Abs(hue - 22f) / 24f);
            float wSat = Clamp01((sat - 0.10f) / 0.08f) * Clamp01((0.70f - sat) / 0.1f);
            float wVal = Clamp01((max - 0.28f) / 0.12f);
            float w = wHue * wSat * wVal;
            if (w <= 0f) continue;
            float nr = r * 0.46f, ng = g * 0.36f, nb = b * 0.32f;
            px[i + 2] = (byte)(255f * (r + (nr - r) * w));
            px[i + 1] = (byte)(255f * (g + (ng - g) * w));
            px[i]     = (byte)(255f * (b + (nb - b) * w));
        }
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
    }

    private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

    public static void SaveJpeg(Bitmap bmp, string path, long quality)
    {
        ImageCodecInfo codec = null;
        foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        using (var rgb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(rgb)) g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
            rgb.Save(path, codec, p);
        }
    }
}
'@

function Get-File([string]$url, [string]$path) {
    if (Test-Path $path) { return }
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    Invoke-WebRequest -UseBasicParsing $url -OutFile "$path.part" -TimeoutSec 300
    Move-Item "$path.part" $path
}

# Convertit un TGA (cache) en texture 1024 dans le projet.
function Convert-Texture([string]$tga, [string]$out, [bool]$darken, [bool]$alpha) {
    if (Test-Path $out) { return }
    $src = [IvoryTga]::Load($tga)
    $small = [IvoryTga]::Resize($src, 1024)
    $src.Dispose()
    if ($darken) { [IvoryTga]::DarkenSkin($small) }
    if ($out.EndsWith('.jpg')) { [IvoryTga]::SaveJpeg($small, $out, 90) } else { $small.Save($out, [System.Drawing.Imaging.ImageFormat]::Png) }
    $small.Dispose()
}

foreach ($a in $avatars) {
    $name = $a.Name; $p = $a.Prefix
    $avatarDir = Join-Path $dest "Avatars\$name"
    New-Item -ItemType Directory -Force (Join-Path $avatarDir 'Textures') | Out-Null
    Get-File "$raw/Avatars/Adults/$name/Export/$name.fbx" (Join-Path $avatarDir "$name.fbx")

    $maps = @('body_color', 'head_color', 'body_normal', 'head_normal')
    $files = (Invoke-RestMethod -Headers @{ 'User-Agent' = 'ivory-tools' } "https://api.github.com/repos/microsoft/Microsoft-Rocketbox/contents/Assets/Avatars/Adults/$name/Textures") | ForEach-Object { $_.name }
    if ($files -contains "${p}_opacity_color.tga") { $maps += 'opacity_color' }

    foreach ($m in $maps) {
        $tga = Join-Path $cache "$name\${p}_$m.tga"
        $ext = if ($m -like '*normal' -or $m -like 'opacity*') { 'png' } else { 'jpg' }
        $out = Join-Path $avatarDir "Textures\${p}_$m.$ext"
        if (Test-Path $out) { continue }
        Get-File "$raw/Avatars/Adults/$name/Textures/${p}_$m.tga" $tga
        Convert-Texture $tga $out ($a.Dark -and $m -like '*_color' -and $m -notlike 'opacity*') ($m -like 'opacity*')
    }
    Write-Host "avatar $name"
}

foreach ($anim in $animations) {
    $file = Split-Path $anim -Leaf
    Get-File "$raw/Animations/$anim" (Join-Path $dest "Animations\$($file.Replace('.max.fbx', '.fbx'))")
    Write-Host "animation $file"
}

$size = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Total : {0:N1} Mo dans {1}" -f $size, $dest)
