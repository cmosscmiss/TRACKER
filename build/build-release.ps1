# Genera el paquete distribuible de TRACKER (Windows x64, autocontenido) y su ZIP.
#
#   powershell -ExecutionPolicy Bypass -File build\build-release.ps1 -Version "v1.0-rc1"
#
# Deja el ZIP (y, si luego compilas build\Tracker.iss, el instalador) en Releases\<version>\.
#
# Por qué hay dos compilaciones y una copia:
#   "dotnet publish" de una app WinUI 3 UNPACKAGED trae el runtime de .NET y del Windows App SDK, pero NO los
#   recursos de la app: la carpeta Assets, los XAML compilados (*.xbf) y el índice Tracker.pri los coloca el
#   pipeline de BUILD, no el de publish. Sin ellos el paquete arranca roto (o no arranca). Así que se compila
#   en Release para obtener esos recursos, se publica autocontenido para obtener los runtimes, y se copian los
#   recursos sobre la carpeta de publish.

param(
    [string]$Version = "v1.0-rc1",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root       = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $root "Tracker.csproj"
$tfm        = "net8.0-windows10.0.19041.0"
$rid        = "win-$($Platform.ToLowerInvariant())"
$buildDir   = Join-Path $root "bin\$Platform\Release\$tfm\$rid"
$publishDir = Join-Path $buildDir "publish"
# Este script y sus fuentes (notas, .iss) viven en build\ y SI se versionan; los paquetes generados van a
# Releases\, que esta en .gitignore.
$sources    = $PSScriptRoot
$releases   = Join-Path $root "Releases"
New-Item -ItemType Directory -Force -Path $releases | Out-Null

# El bootstrapper Evergreen de WebView2 lo empotra el instalador (build\Tracker.iss). No se versiona (binario de
# terceros que ademas se renueva), asi que se descarga a Releases\ la primera vez.
$webview2 = Join-Path $releases "MicrosoftEdgeWebview2Setup.exe"
if (-not (Test-Path $webview2)) {
    Write-Host "== Descargando el bootstrapper de WebView2" -ForegroundColor Cyan
    Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $webview2 -UseBasicParsing
}

Write-Host "== 1/4 Build (Release) para obtener Assets, *.xbf y Tracker.pri" -ForegroundColor Cyan
dotnet build $project -c Release -p:Platform=$Platform -v m
if ($LASTEXITCODE -ne 0) { throw "El build ha fallado." }

Write-Host "== 2/4 Publish autocontenido (runtime .NET + Windows App SDK)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -p:Platform=$Platform -r $rid --self-contained true -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $publishDir -v m
if ($LASTEXITCODE -ne 0) { throw "El publish ha fallado." }

Write-Host "== 3/4 Copiando los recursos que el publish no arrastra" -ForegroundColor Cyan
Copy-Item (Join-Path $buildDir "Assets") $publishDir -Recurse -Force
Copy-Item (Join-Path $buildDir "Tracker.pri") $publishDir -Force
# Los .xbf conservan la ruta relativa de su XAML (Controls\Views\...), asi que se copian preservando el arbol.
# OJO: la carpeta de publish cuelga del propio buildDir, asi que hay que EXCLUIRLA del recorrido; si no, se
# copia sobre si misma una y otra vez (publish\publish\publish...) y el paquete crece sin fin.
$xbfFiles = @(Get-ChildItem $buildDir -Recurse -Filter *.xbf -File |
    Where-Object { -not $_.FullName.StartsWith($publishDir, [StringComparison]::OrdinalIgnoreCase) })
foreach ($file in $xbfFiles) {
    $relative = $file.FullName.Substring($buildDir.Length + 1)
    $target   = Join-Path $publishDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item $file.FullName $target -Force
}

# Comprobacion: si falta alguno de los tres, el paquete NO sirve.
$assets = (Get-ChildItem (Join-Path $publishDir "Assets") -Recurse -File).Count
$xbf    = (Get-ChildItem $publishDir -Recurse -Filter *.xbf).Count
if ($assets -eq 0 -or $xbf -eq 0 -or -not (Test-Path (Join-Path $publishDir "Tracker.pri"))) {
    throw "El paquete esta incompleto (Assets=$assets, xbf=$xbf, pri=$(Test-Path (Join-Path $publishDir 'Tracker.pri')))."
}
Write-Host "   Assets=$assets  xbf=$xbf  Tracker.pri=OK" -ForegroundColor Green

Write-Host "== 4/4 ZIP" -ForegroundColor Cyan
# Las notas para el usuario final viajan dentro del ZIP, en los dos idiomas de la app.
Copy-Item (Join-Path $sources "LEEME.txt") $publishDir -Force
Copy-Item (Join-Path $sources "README.txt") $publishDir -Force

# Cada release en su propia subcarpeta (Releases\v1.0-rc1\...): ZIP, instalador y notas juntos.
$outDir = Join-Path $releases $Version
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$zip = Join-Path $outDir "Tracker-$Version-$rid.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -CompressionLevel Optimal

# Y tambien SUELTAS junto al ZIP: asi se leen los requisitos (WebView2, SmartScreen) sin descomprimir 125 MB.
Copy-Item (Join-Path $sources "LEEME.txt") $outDir -Force
Copy-Item (Join-Path $sources "README.txt") $outDir -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "`nListo: $zip ($size MB)" -ForegroundColor Green
Write-Host "Para el instalador: ISCC.exe build\Tracker.iss (deja el .exe en $outDir)" -ForegroundColor DarkGray
