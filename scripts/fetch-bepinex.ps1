# Baixa o BepInEx 5 (Mono x64) fixo na versão e no hash para vendor\BepInEx. Ele vai no pacote do mod (LGPL-2.1).
$ErrorActionPreference = 'Stop'
$version = '5.4.23.6'
$sha256 = '6C57A714A892EFB4E9E1FEF63D608A3391F30EC984031C465404E4EE381A0755'
$url = "https://github.com/BepInEx/BepInEx/releases/download/v$version/BepInEx_win_x64_$version.zip"

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'vendor\BepInEx'
$stamp = Join-Path $dest '.version'
if ((Test-Path $stamp) -and ((Get-Content $stamp) -eq $version)) { Write-Host "BepInEx $version já está em vendor\BepInEx"; exit 0 }

$zip = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_win_x64_$version.zip"
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
$got = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($got -ne $sha256) { Remove-Item $zip; throw "Hash do BepInEx não confere ($got). Nada foi instalado." }

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Expand-Archive $zip -DestinationPath $dest
Remove-Item $zip
Set-Content $stamp $version
Write-Host "BepInEx $version em vendor\BepInEx"
