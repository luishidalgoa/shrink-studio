<#
.SYNOPSIS
    Comprime vídeos (series, películas) reduciendo el bitrate y quitando pistas de audio sobrantes.

.DESCRIPTION
    Recodifica a HEVC usando aceleración por hardware si está disponible, convierte el audio
    a AAC y conserva solo los idiomas que te interesan, poniendo tu idioma preferido como
    predeterminado. Los ficheros originales NUNCA se tocan: el resultado va a otra carpeta.

    Es re-ejecutable sin miedo: salta lo ya comprimido y lo que aún se está descargando.

.PARAMETER Path
    Fichero o carpeta a procesar. Por defecto, la carpeta actual.

.PARAMETER Output
    Carpeta de destino. Por defecto, "<carpeta>\comprimido".

.PARAMETER Lang
    Idioma preferido (código ISO de 3 letras). Se marca como pista predeterminada. Por defecto "spa".

.PARAMETER KeepLangs
    Idiomas de audio a conservar. Por defecto, el preferido + inglés.
    Usa "all" para conservarlos todos.

.PARAMETER Quality
    Calidad: número más bajo = mejor calidad y más tamaño. Por defecto 27 (hardware) o 23 (software).
    Rango útil: 22 (muy buena) a 30 (muy comprimida).

.PARAMETER MaxHeight
    Reescala si el vídeo es más alto que esto (p.ej. 1080 para bajar 4K a 1080p). Por defecto, no reescala.

.PARAMETER Recurse
    Busca también en subcarpetas.

.PARAMETER NoSubs
    No conservar subtítulos.

.PARAMETER Force
    Procesa también los ficheros que ya parecen comprimidos (HEVC/AV1 con bitrate bajo).

.PARAMETER DryRun
    Muestra qué haría, sin codificar nada.

.EXAMPLE
    Shrink-Video.ps1
    Comprime todos los vídeos de la carpeta actual.

.EXAMPLE
    Shrink-Video.ps1 -Path "D:\Series\Bob Esponja" -Recurse
    Toda una serie con sus subcarpetas de temporadas.

.EXAMPLE
    Shrink-Video.ps1 -Path "D:\Pelis" -Quality 24 -MaxHeight 1080
    Películas con más calidad, bajando el 4K a 1080p.

.EXAMPLE
    Shrink-Video.ps1 -Lang eng -KeepLangs eng
    Solo audio inglés, tirando el resto.
#>
[CmdletBinding()]
param(
    [string]   $Path = ".",
    [string]   $Output,
    [string]   $Lang = "spa",
    [string[]] $KeepLangs,
    [int]      $Quality = 0,
    [int]      $MaxHeight = 0,
    # 0 = máxima calidad: copiar el audio original sin recomprimir (por defecto)
    [int]      $AudioBitrate = 0,
    [string[]] $SubLangs,
    [string[]] $Extensions = @("mkv","mp4","avi","m4v","mov","wmv","ts","webm"),
    [switch]   $Recurse,
    [switch]   $NoSubs,
    [switch]   $Force,
    [switch]   $DryRun
)

$ErrorActionPreference = "Stop"

# ---------- comprobaciones previas ----------
foreach ($exe in @("ffmpeg","ffprobe")) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        Write-Host "Falta '$exe' en el PATH. Instálalo con: winget install Gyan.FFmpeg" -ForegroundColor Red
        exit 1
    }
}

if (-not $KeepLangs) { $KeepLangs = @($Lang, "eng") }
# La GUI (y -File) pasan "spa,eng" como un único string: separar aquí
$KeepLangs = @($KeepLangs | ForEach-Object { $_ -split '[,;\s]+' } | Where-Object { $_ })
if ($SubLangs) { $SubLangs = @($SubLangs | ForEach-Object { $_ -split '[,;\s]+' } | Where-Object { $_ }) }
$keepAll = $KeepLangs -contains "all"
$subsAll = -not $SubLangs -or ($SubLangs -contains "all")

# ---------- elegir codificador ----------
function Select-Encoder {
    $available = (ffmpeg -hide_banner -encoders 2>$null) -join "`n"
    foreach ($cand in @("hevc_qsv","hevc_nvenc","hevc_amf")) {
        if ($available -notmatch [regex]::Escape($cand)) { continue }
        # Verificar que realmente funciona (estar listado no basta)
        $null = ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc=size=640x480:duration=0.1 `
                       -c:v $cand -f null - 2>&1
        if ($LASTEXITCODE -eq 0) { return $cand }
    }
    return "libx265"
}

Write-Host "Detectando codificador..." -ForegroundColor DarkGray
$encoder = Select-Encoder
if ($Quality -eq 0) { $Quality = if ($encoder -eq "libx265") { 23 } else { 27 } }

$encArgs = switch ($encoder) {
    "hevc_qsv"   { @("-c:v","hevc_qsv","-global_quality","$Quality","-preset","slow") }
    "hevc_nvenc" { @("-c:v","hevc_nvenc","-rc","vbr","-cq","$Quality","-preset","p6","-tune","hq") }
    "hevc_amf"   { @("-c:v","hevc_amf","-rc","cqp","-qp_i","$Quality","-qp_p","$Quality","-quality","quality") }
    default      { @("-c:v","libx265","-crf","$Quality","-preset","medium") }
}
$hw = if ($encoder -eq "libx265") { "software (CPU, lento)" } else { "hardware" }
Write-Host "Codificador: $encoder [$hw] · calidad $Quality" -ForegroundColor Cyan

# ---------- reunir ficheros ----------
$item = Get-Item -LiteralPath $Path
if ($item.PSIsContainer) {
    $baseDir = $item.FullName
    $files = Get-ChildItem -LiteralPath $baseDir -File -Recurse:$Recurse |
             Where-Object { $Extensions -contains $_.Extension.TrimStart('.').ToLower() }
} else {
    $baseDir = $item.DirectoryName
    $files = @($item)
}
if (-not $Output) { $Output = Join-Path $baseDir "comprimido" }

$files = $files | Where-Object { $_.DirectoryName -ne (Convert-Path -LiteralPath $Output -ErrorAction SilentlyContinue) } |
         Sort-Object FullName

if (-not $files) { Write-Host "No hay vídeos que procesar en '$baseDir'." -ForegroundColor Yellow; exit 0 }
Write-Host "Encontrados $($files.Count) vídeo(s). Destino: $Output`n" -ForegroundColor Cyan
if (-not $DryRun) { New-Item -ItemType Directory -Force $Output | Out-Null }

# ---------- helpers ----------
function Test-StillDownloading($file) {
    # Fichero parcial al lado -> seguro que no ha terminado
    foreach ($ext in @(".part",".crdownload",".!ut",".downloading",".tmp","!qB")) {
        if (Test-Path -LiteralPath "$($file.FullName)$ext") { return $true }
    }
    # Abierto en exclusiva por el gestor de descargas
    try { $s = [IO.File]::Open($file.FullName,'Open','Read','None'); $s.Close() }
    catch { return $true }
    # Si se tocó hace poco, confirmar mirando si sigue creciendo (más fiable que la fecha)
    if (((Get-Date) - $file.LastWriteTime).TotalMinutes -lt 2) {
        $size1 = $file.Length
        Start-Sleep -Seconds 3
        $size2 = (Get-Item -LiteralPath $file.FullName).Length
        return ($size2 -ne $size1)
    }
    return $false
}

$lossyAudio = @("aac","opus","mp3","vorbis")   # ya comprimido: se copia tal cual
$coverCodecs = @("png","mjpeg","bmp","gif")    # carátulas incrustadas: se descartan

# ---------- procesar ----------
$report = @()
$n = 0
foreach ($f in $files) {
    $n++
    $tag = "[$n/$($files.Count)] $($f.Name)"
    $out = Join-Path $Output ([IO.Path]::ChangeExtension($f.Name, ".mkv"))

    if ((Test-Path -LiteralPath $out) -and -not $Force) { Write-Host "$tag -> ya hecho, salto" -ForegroundColor DarkGray; continue }
    if ($f.FullName -eq $out) { continue }
    if (Test-StillDownloading $f) { Write-Host "$tag -> descargando aún, salto" -ForegroundColor Yellow; continue }

    # Analizar pistas
    try {
        $probe = ffprobe -v error -show_entries "stream=index,codec_type,codec_name,height:stream_tags=language:format=bit_rate,duration" `
                         -of json -- $f.FullName | ConvertFrom-Json
    } catch { Write-Host "$tag -> no se puede leer, salto" -ForegroundColor Red; continue }

    $video = $probe.streams | Where-Object { $_.codec_type -eq 'video' -and $coverCodecs -notcontains $_.codec_name } | Select-Object -First 1
    if (-not $video) { Write-Host "$tag -> sin pista de vídeo, salto" -ForegroundColor Red; continue }

    # ¿Ya está comprimido?
    $kbps = if ($probe.format.bit_rate) { [int]($probe.format.bit_rate / 1000) } else { 0 }
    if (-not $Force -and $video.codec_name -in @("hevc","av1") -and $kbps -gt 0 -and $kbps -lt 2500) {
        Write-Host "$tag -> ya comprimido ($($video.codec_name), $kbps kbps), salto" -ForegroundColor DarkGray; continue
    }

    # Audio: preferido primero, luego el resto de idiomas a conservar
    $allAudio = @($probe.streams | Where-Object { $_.codec_type -eq 'audio' })
    if (-not $allAudio) { Write-Host "$tag -> sin audio, salto" -ForegroundColor Red; continue }
    $pref  = @($allAudio | Where-Object { $_.tags.language -eq $Lang })
    $other = @($allAudio | Where-Object { $_.tags.language -ne $Lang -and ($keepAll -or $KeepLangs -contains $_.tags.language) })
    $audio = @($pref) + @($other)
    if (-not $audio) { $audio = $allAudio }   # ningún idioma coincide: conservar todo antes que quedarnos sin sonido

    $subs = if ($NoSubs) { @() } else {
        @($probe.streams | Where-Object { $_.codec_type -eq 'subtitle' -and
            ($subsAll -or $SubLangs -contains $_.tags.language) })
    }

    # Construir argumentos
    $maps = @("-map","0:$($video.index)")
    foreach ($a in $audio) { $maps += @("-map","0:$($a.index)") }
    foreach ($s in $subs)  { $maps += @("-map","0:$($s.index)") }

    $audioArgs = @()
    for ($i = 0; $i -lt $audio.Count; $i++) {
        # AudioBitrate 0 = máxima calidad: copiar siempre el audio original
        if ($AudioBitrate -eq 0 -or $lossyAudio -contains $audio[$i].codec_name) { $audioArgs += @("-c:a:$i","copy") }
        else { $audioArgs += @("-c:a:$i","aac","-b:a:$i","${AudioBitrate}k") }
    }
    # Primera pista de audio = predeterminada, el resto no
    $dispArgs = @("-disposition:a:0","default")
    for ($i = 1; $i -lt $audio.Count; $i++) { $dispArgs += @("-disposition:a:$i","0") }

    $scaleArgs = @()
    if ($MaxHeight -gt 0 -and $video.height -gt $MaxHeight) {
        $scaleArgs = @("-vf","scale=-2:$MaxHeight")
    }

    $langs = ($audio | ForEach-Object { if ($_.tags.language) { $_.tags.language } else { "?" } }) -join "+"
    $dropped = $allAudio.Count - $audio.Count
    $info = "audio: $langs" + $(if ($dropped -gt 0) { " (descarto $dropped)" } else { "" }) +
            $(if ($subs) { ", $($subs.Count) sub" } else { "" }) +
            $(if ($scaleArgs) { ", reescalo a ${MaxHeight}p" } else { "" })

    if ($DryRun) { Write-Host "$tag -> $info" -ForegroundColor Magenta; continue }

    Write-Host "$tag" -ForegroundColor White
    Write-Host "    $info" -ForegroundColor DarkGray

    $tmp = "$out.tmp.mkv"
    $ff = @("-hide_banner","-loglevel","warning","-stats","-y","-i",$f.FullName) +
          $maps + $scaleArgs + $encArgs + $audioArgs + @("-c:s","copy") + $dispArgs + @("-map_metadata","0",$tmp)
    ffmpeg @ff

    # Reintento sin subtítulos: algunos formatos raros no se copian a MKV
    if ($LASTEXITCODE -ne 0 -and $subs) {
        Write-Host "    reintentando sin subtítulos..." -ForegroundColor Yellow
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        $maps2 = @("-map","0:$($video.index)"); foreach ($a in $audio) { $maps2 += @("-map","0:$($a.index)") }
        $ff = @("-hide_banner","-loglevel","warning","-stats","-y","-i",$f.FullName) +
              $maps2 + $scaleArgs + $encArgs + $audioArgs + $dispArgs + @("-map_metadata","0",$tmp)
        ffmpeg @ff
    }

    if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $tmp)) {
        Move-Item -LiteralPath $tmp -Destination $out -Force
        $mbIn  = [math]::Round($f.Length/1MB)
        $mbOut = [math]::Round((Get-Item -LiteralPath $out).Length/1MB)
        $pct   = [math]::Round(100 - ($mbOut / [math]::Max($mbIn,1) * 100))
        Write-Host "    OK  $mbIn MB -> $mbOut MB  (-$pct%)" -ForegroundColor Green
        $report += [pscustomobject]@{ Fichero=$f.Name; AntesMB=$mbIn; DespuesMB=$mbOut; Reduccion="$pct%" }
    } else {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        Write-Host "    ERROR al codificar (código $LASTEXITCODE)" -ForegroundColor Red
        $report += [pscustomobject]@{ Fichero=$f.Name; AntesMB=[math]::Round($f.Length/1MB); DespuesMB=$null; Reduccion="ERROR" }
    }
}

# ---------- resumen ----------
if ($report) {
    Write-Host "`n===== RESUMEN =====" -ForegroundColor Cyan
    $report | Format-Table -AutoSize
    $okRows = @($report | Where-Object { $_.DespuesMB })
    if ($okRows) {
        $tIn  = ($okRows | Measure-Object AntesMB   -Sum).Sum
        $tOut = ($okRows | Measure-Object DespuesMB -Sum).Sum
        $tPct = [math]::Round(100 - ($tOut / [math]::Max($tIn,1) * 100))
        Write-Host ("Total: {0} GB -> {1} GB  (-{2}%)  ·  {3} fichero(s)" -f `
            [math]::Round($tIn/1024,2), [math]::Round($tOut/1024,2), $tPct, $okRows.Count) -ForegroundColor Green
    }
    Write-Host "Los originales siguen intactos. Bórralos tú cuando compruebes el resultado." -ForegroundColor DarkGray
}
