# Genera el icono de ShrinkStudio con GDI+ (sin dependencias externas).
#
# El glifo es EL MISMO que luce la barra de título de la app: squircle con el
# degradado morado de la paleta Nocturne (Accent #968AE0 -> Accent700 #5D5294),
# un play central y dos chevrones que lo aprietan por los lados — la metáfora de
# comprimir. Antes el icono era turquesa y no se parecía en nada al de la app.
#
# Salidas: src\ShrinkVideo\Assets\app.ico (multi-resolución 16..256),
#          src\ShrinkVideo\Assets\app-256.png y docs\icon.png (README/repo).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

# Paleta: exactamente los mismos valores que Theme.xaml
$Accent    = [System.Drawing.Color]::FromArgb(255, 150, 138, 224)   # #968AE0
$Accent700 = [System.Drawing.Color]::FromArgb(255,  93,  82, 148)   # #5D5294
$Ink       = [System.Drawing.Color]::FromArgb(255, 245, 244, 255)   # #F5F4FF

function Render([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode   = 'HighQuality'

    # --- squircle de fondo ---
    $m = [float]($S * 0.055)
    $side = [float]($S - 2*$m)
    $rad = [float]($side * 0.30)          # misma redondez que el logo de la barra de título
    $rect = New-Object System.Drawing.RectangleF($m, $m, $side, $side)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    # degradado en diagonal, como el LinearGradientBrush 0,0 -> 1,1 del XAML
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $Accent, $Accent700, 45.0)
    $g.FillPath($grad, $path)

    # brillo superior muy sutil, para que no quede plano
    $glow = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, [System.Drawing.Color]::FromArgb(46, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
    $g.FillPath($glow, $path)
    $g.SetClip($path)

    # --- glifo, definido sobre un lienzo de 16x16 igual que en el XAML ---
    # En tamaños pequeños los chevrones se emborronan: se quitan y el play se
    # agranda, que es lo único que se distingue a 16 px.
    $conChevrones = $S -ge 32
    $escala = if ($conChevrones) { 0.62 } else { 0.42 }
    $gs = [float]($S * $escala)
    $u  = [float]($gs / 16.0)
    $gx = [float](($S - $gs) / 2)
    $gy = [float](($S - $gs) / 2)

    $P = { param([float]$x, [float]$y) New-Object System.Drawing.PointF(($gx + $x*$u), ($gy + $y*$u)) }
    $brush = New-Object System.Drawing.SolidBrush($Ink)

    if ($conChevrones) {
        # play, algo mayor que en la barra de título para que sea el protagonista
        $tri = [System.Drawing.PointF[]]@((& $P 5.7 4.3), (& $P 11.0 8.0), (& $P 5.7 11.7))
        $g.FillPolygon($brush, $tri)

        $pen = New-Object System.Drawing.Pen($Ink, [float](1.5*$u))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        # chevrón izquierdo: M2 5.5 L3.5 8 L2 10.5   (apunta hacia dentro)
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((& $P 2.0 5.5), (& $P 3.5 8.0), (& $P 2.0 10.5)))
        # chevrón derecho: M14 5.5 L12.5 8 L14 10.5
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((& $P 14.0 5.5), (& $P 12.5 8.0), (& $P 14.0 10.5)))
        $pen.Dispose()
    }
    else {
        # solo el play, centrado y más grande
        $tri = [System.Drawing.PointF[]]@((& $P 4.0 2.5), (& $P 12.5 8.0), (& $P 4.0 13.5))
        $g.FillPolygon($brush, $tri)
    }

    $brush.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16,24,32,48,64,128,256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}

$out = Join-Path $PSScriptRoot "src\ShrinkVideo\Assets\app.ico"
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]$(if ($s -ge 256) {0} else {$s}))
    $bw.Write([byte]$(if ($s -ge 256) {0} else {$s}))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $fs.Close()

# PNG grande: vista previa interna y portada del README
$big = Render 256
$big.Save((Join-Path $PSScriptRoot "src\ShrinkVideo\Assets\app-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$docs = Join-Path $PSScriptRoot "docs"
if (-not (Test-Path $docs)) { New-Item -ItemType Directory $docs | Out-Null }
$big.Save((Join-Path $docs "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

"Icono generado: $out ($([math]::Round((Get-Item $out).Length/1KB,1)) KB)"
