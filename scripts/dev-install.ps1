# Para desenvolver: copia o BepInEx e o mod compilado para a pasta do jogo (sem passar pela Biblioteca).
# Nenhum arquivo original do jogo é substituído. Para tirar: scripts\dev-install.ps1 -Remove
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\MicroWorks',
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $GameDir 'MicroWorks.exe'))) { throw "MicroWorks.exe não está em '$GameDir'" }
# o jogo (e o relator de falhas dele) seguram os arquivos do BepInEx enquanto estão abertos
Wait-Process -Name MicroWorks, UnityCrashHandler64 -Timeout 30 -ErrorAction SilentlyContinue
if (Get-Process MicroWorks -ErrorAction SilentlyContinue) { throw 'Feche o MicroWorks antes de instalar.' }

# o que o mod põe na pasta do jogo (o BepInEx cria BepInEx\config, cache e LogOutput.log ao rodar)
$owned = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'BepInEx')

if ($Remove) {
    foreach ($item in $owned) {
        $p = Join-Path $GameDir $item
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Write-Host 'Mod removido da pasta do jogo.'
    exit 0
}

$vendor = Join-Path $root 'vendor\BepInEx'
$plugin = Join-Path $root 'build\BepInEx\plugins\MicroWorksCoop\MicroWorksCoop.dll'
if (-not (Test-Path $vendor)) { throw 'Rode antes: scripts\fetch-bepinex.ps1' }
if (-not (Test-Path $plugin)) { throw 'Compile antes: dotnet build src -c Release' }

foreach ($item in Get-ChildItem $vendor -Force | Where-Object { $_.Name -ne '.version' }) {
    if ((Test-Path (Join-Path $GameDir $item.Name)) -and $owned -notcontains $item.Name) { throw "'$($item.Name)' já existe no jogo e não é do mod" }
}
# copia só o que mudou: o BepInEx é o mesmo de uma instalação para outra
foreach ($file in Get-ChildItem $vendor -Recurse -File -Force | Where-Object { $_.Name -ne '.version' }) {
    $target = Join-Path $GameDir $file.FullName.Substring($vendor.Length + 1)
    $old = Get-Item $target -Force -ErrorAction SilentlyContinue
    if ($old -and $old.Length -eq $file.Length -and $old.LastWriteTimeUtc -eq $file.LastWriteTimeUtc) { continue }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $file.FullName $target -Force
}
$dest = Join-Path $GameDir 'BepInEx\plugins\MicroWorksCoop'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $plugin $dest -Force
Write-Host "Mod instalado em $GameDir. Log: BepInEx\LogOutput.log"
