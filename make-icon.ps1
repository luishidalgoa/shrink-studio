# Genera Assets\app.ico con GDI+ (sin dependencias externas).
# Icono: squircle con degradado turquesa->teal, botón play central y
# flechas de compresión en las esquinas. Multi-resolución (16..256).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function Render([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode   = 'HighQuality'

    # --- squircle de fondo con degradado ---
    $m = [float]($S * 0.055)
    $side = [float]($S - 2*$m)
    $rad = [float]($side * 0.28)
    $rect = New-Object System.Drawing.RectangleF($m, $m, $side, $side)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 22, 224, 180)   # turquesa vivo
    $c2 = [System.Drawing.Color]::FromArgb(255, 9, 92, 92)      # teal profundo
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 55.0)
    $g.FillPath($grad, $path)

    # brillo superior sutil
    $glow = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, [System.Drawing.Color]::FromArgb(60, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
    $g.FillPath($glow, $path)

    $g.SetClip($path)

    $cx = [float]($S * 0.5); $cy = [float]($S * 0.5)

    # --- flechas de compresión en las esquinas (solo si hay sitio) ---
    if ($S -ge 44) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 233, 255, 246), [float]($S*0.052))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $off = [float]($S * 0.205)   # distancia de la esquina al vértice de la flecha
        $arm = [float]($S * 0.086)   # largo de cada brazo del ángulo
        $corners = @(
            @{ x=$off;      y=$off;      sx= 1; sy= 1 },  # sup-izq -> apunta abajo-der
            @{ x=$S-$off;   y=$off;      sx=-1; sy= 1 },  # sup-der
            @{ x=$off;      y=$S-$off;   sx= 1; sy=-1 },  # inf-izq
            @{ x=$S-$off;   y=$S-$off;   sx=-1; sy=-1 }   # inf-der
        )
        foreach ($k in $corners) {
            $bx = [float]$k.x; $by = [float]$k.y; $sx = [float]$k.sx; $sy = [float]$k.sy
            $tipx = [float]($bx + $sx*$arm); $tipy = [float]($by + $sy*$arm)   # punta hacia el centro
            # cabeza de flecha (abre hacia la esquina), apuntando al centro
            $g.DrawLines($pen, [System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF($bx, $tipy)),
                (New-Object System.Drawing.PointF($tipx, $tipy)),
                (New-Object System.Drawing.PointF($tipx, $by))
            ))
            # cola hacia la esquina
            $g.DrawLine($pen, $tipx, $tipy, [float]($bx - $sx*$arm*0.5), [float]($by - $sy*$arm*0.5))
        }
    }

    # --- botón play central ---
    $pr = [float]($S * 0.205)   # radio del círculo
    $circle = New-Object System.Drawing.RectangleF(($cx-$pr), ($cy-$pr), ($pr*2), ($pr*2))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 244, 255, 251))), $circle)

    $t = [float]($pr * 0.62)
    $tri = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(($cx - $t*0.55), ($cy - $t))),
        (New-Object System.Drawing.PointF(($cx - $t*0.55), ($cy + $t))),
        (New-Object System.Drawing.PointF(($cx + $t*0.95), $cy))
    )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 8, 60, 52))), $tri)

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

# PNG grande para el README / vista previa
$big = Render 256
$big.Save((Join-Path $PSScriptRoot "src\ShrinkVideo\Assets\app-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
"Icono generado: $out ($([math]::Round((Get-Item $out).Length/1KB,1)) KB)"
