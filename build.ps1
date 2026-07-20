<#
    Compila el instalador de "Comprimir vídeos" de punta a punta:
    1) regenera el icono, 2) publica el .exe self-contained, 3) compila el instalador Inno.
    Uso:  pwsh -File build.ps1            (versión leída del .csproj)
          pwsh -File build.ps1 0.2.0      (forzar versión)
    Salida: installer\Output\ShrinkVideo-Setup-<version>.exe
#>
param([string]$Version)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "src\ShrinkVideo\ShrinkVideo.csproj"

if (-not $Version) {
    $Version = (Select-Xml -Path $csproj -XPath "//Version").Node.InnerText
}
Write-Host "== Comprimir vídeos · versión $Version ==" -ForegroundColor Cyan

# 1) icono
Write-Host "`n[1/3] Icono..." -ForegroundColor Yellow
pwsh -NoProfile -File (Join-Path $root "make-icon.ps1")

# 2) publish self-contained (un solo .exe, sin dependencias del runtime)
Write-Host "`n[2/3] Publicando el ejecutable..." -ForegroundColor Yellow
$publish = Join-Path $root "publish"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Fallo en dotnet publish" }

# 3) instalador Inno Setup
Write-Host "`n[3/3] Compilando el instalador..." -ForegroundColor Yellow
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = Get-ChildItem `
        "$env:LOCALAPPDATA\Programs\Inno Setup*","C:\Program Files (x86)\Inno Setup*","C:\Program Files\Inno Setup*" `
        -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
} else { $iscc = $iscc.Source }
if (-not $iscc) { throw "No se encuentra ISCC.exe (Inno Setup). Instálalo: winget install JRSoftware.InnoSetup" }

& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\shrink-video.iss")
if ($LASTEXITCODE -ne 0) { throw "Fallo en Inno Setup" }

$out = Join-Path $root "installer\Output\ShrinkVideo-Setup-$Version.exe"
Write-Host "`nInstalador listo: $out ($([math]::Round((Get-Item $out).Length/1MB,1)) MB)" -ForegroundColor Green
