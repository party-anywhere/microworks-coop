# Monta uma versão do mod no seu PC e, com -Publicar, cria o release no GitHub.
# O build precisa das DLLs do MicroWorks instalado (que não podem ir para o GitHub), por isso não roda no Actions.
#
#   powershell -File scripts\release.ps1             # compila, confere e empacota em release\
#   powershell -File scripts\release.ps1 -Publicar   # e cria o release vX.Y.Z com o pacote (precisa do gh logado)
param(
    [switch]$Publicar,
    # a ferramenta do Party Anywhere: a compilada ao lado (..\party-anywhere, a mais nova) ou a do PATH
    [string]$Pa = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Pa) {
    $Pa = Join-Path $root "..\party-anywhere\target\release\pa.exe"
    $cmd = Get-Command pa -ErrorAction SilentlyContinue
    if (-not (Test-Path $Pa) -and $cmd) { $Pa = $cmd.Source }
}
if (-not (Test-Path $Pa)) { throw "Não achei o pa.exe. Passe -Pa <caminho> ou compile o Party Anywhere (cargo build --release -p pa-cli)." }

$version = (Get-Content party-anywhere.json -Raw | ConvertFrom-Json).version
$tag = "v$version"

Write-Host "1/3 Compilando o mod $tag"
if (-not (Test-Path vendor\BepInEx\BepInEx\core\BepInEx.dll)) { & powershell -File scripts\fetch-bepinex.ps1 }
dotnet build src -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "O build falhou." }

Write-Host "2/3 Conferindo e empacotando"
& $Pa validate
if ($LASTEXITCODE -ne 0) { throw "O manifesto tem problemas (veja acima)." }
& $Pa pack --from build --tag $tag
if ($LASTEXITCODE -ne 0) { throw "O empacotamento falhou." }

if (-not $Publicar) {
    Write-Host "Pronto em release\. Para publicar: powershell -File scripts\release.ps1 -Publicar"
    exit 0
}

Write-Host "3/3 Publicando o release $tag"
$zip = Get-ChildItem release -Filter "*-$version.zip" | Select-Object -First 1
gh release create $tag $zip.FullName release\party-anywhere.release.json --title "MicroWorks Co-op Local $version" --notes "Instale pelo Party Anywhere: Biblioteca > Adicionar jogo > Por link, com o link deste repositório."
if ($LASTEXITCODE -ne 0) { throw "O gh não criou o release (está logado? a tag já existe?)." }
Write-Host "Publicado: $tag"
