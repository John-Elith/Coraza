# Genera Coraza listo para instalar en PCs sin internet.
# Incluye el runtime de .NET, así que el equipo de la iglesia no necesita instalar nada más.
#   .\publicar.ps1             → carpeta publicar\Coraza y ZIP (datos en Documentos\Coraza)
#   .\publicar.ps1 -Portatil   → versión portátil para memoria USB (datos en la carpeta Datos)
param([switch]$Portatil)

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot
$salida = Join-Path $raiz "publicar\Coraza"
$zip = Join-Path $raiz ("publicar\Coraza-win-x64" + $(if ($Portatil) { "-portatil" } else { "" }) + ".zip")

if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
dotnet publish (Join-Path $raiz "src\Coraza.App\Coraza.App.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $salida
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }

if ($Portatil) {
    Set-Content (Join-Path $salida "portable.txt") "Versión portátil: los datos de Coraza se guardan en la carpeta Datos junto al programa." -Encoding utf8
}

Compress-Archive -Path (Join-Path $salida "*") -DestinationPath $zip -Force
Write-Host "Listo: $salida"
Write-Host "ZIP:   $zip"
