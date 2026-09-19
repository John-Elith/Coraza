# Genera CorazaInstalador.exe: publica la aplicacion y la empaqueta con Inno Setup.
# El usuario final descarga UN solo archivo, hace doble clic y sigue el asistente.
#
#   .\instalador.ps1
#
# Requiere Inno Setup 6:  winget install JRSoftware.InnoSetup
# La version del instalador se define en instalador.iss (#define Version).

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot
$salida = Join-Path $raiz "publicar\Coraza"
$guion = Join-Path $raiz "instalador.iss"

# 1. Localizar el compilador de Inno Setup.
#    Ojo: winget lo instala en el perfil del usuario cuando corre sin permisos de
#    administrador, asi que no basta con mirar en Program Files.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

# Si no aparece, preguntarle al registro donde quedo instalado.
if (-not $iscc) {
    $claves = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($clave in $claves) {
        try {
            $hallado = Get-ItemProperty $clave -ErrorAction Stop |
                Where-Object { $_.DisplayName -like "*Inno Setup*" -and $_.InstallLocation } |
                ForEach-Object { Join-Path $_.InstallLocation "ISCC.exe" } |
                Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($hallado) { $iscc = $hallado; break }
        } catch { }
    }
}

if (-not $iscc) {
    throw "No encuentro ISCC.exe. Instala Inno Setup con: winget install JRSoftware.InnoSetup"
}
Write-Host "Inno Setup: $iscc"

# 2. Publicar la aplicacion autocontenida (sin portable.txt: esta es la version instalable).
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
dotnet publish (Join-Path $raiz "src\Coraza.App\Coraza.App.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $salida
if ($LASTEXITCODE -ne 0) { throw "La publicacion fallo." }

# 3. Empaquetar esa carpeta en un unico instalador.
& $iscc $guion
if ($LASTEXITCODE -ne 0) { throw "La compilacion del instalador fallo." }

$exe = Join-Path $raiz "publicar\CorazaInstalador.exe"
Write-Host ""
Write-Host "Listo:   $exe"
Write-Host ("Tamano:  {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
