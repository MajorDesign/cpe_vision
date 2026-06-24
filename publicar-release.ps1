# ============================================================================
#  Publica uma RELEASE no GitHub (MajorDesign/cpe_vision) com os instaladores
#  e o executável do terminal, para o auto-update do pré-load (splash) baixar.
#
#  Pré-requisito: os binários já gerados (rode publicar.bat antes), ou seja:
#    - instaladores\saida\setup-controlador.exe
#    - instaladores\saida\setup-terminal.exe
#    - dist\Terminal\VideoWall.Viewer.exe
#
#  Token: gere em GitHub > Settings > Developer settings > Personal access
#  tokens (classic) com escopo "repo" e informe via -Token ou $env:GITHUB_TOKEN.
#
#  Uso:
#    $env:GITHUB_TOKEN = "ghp_xxx"; .\publicar-release.ps1
#    .\publicar-release.ps1 -Token ghp_xxx -Version 1.2.0
# ============================================================================
param(
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$owner = "MajorDesign"
$repo  = "cpe_vision"
$root  = $PSScriptRoot

if (-not $Token) {
    Write-Error "Token nao informado. Use -Token ou defina `$env:GITHUB_TOKEN."
    exit 1
}

# Descobre a versao pelo executavel do terminal, se nao for informada.
if (-not $Version) {
    $exe = Join-Path $root "dist\Terminal\VideoWall.Viewer.exe"
    if (-not (Test-Path $exe)) { Write-Error "Rode publicar.bat antes (falta $exe)."; exit 1 }
    $fv = (Get-Item $exe).VersionInfo.FileVersion   # ex.: 1.1.0.0
    $Version = ($fv -replace '\.0$', '')            # ex.: 1.1.0
}
$tag = "v$Version"

$assets = @(
    (Join-Path $root "instaladores\saida\setup-controlador.exe"),
    (Join-Path $root "instaladores\saida\setup-terminal.exe"),
    (Join-Path $root "dist\Terminal\VideoWall.Viewer.exe")
)
foreach ($a in $assets) {
    if (-not (Test-Path $a)) { Write-Error "Arquivo nao encontrado: $a (rode publicar.bat antes)."; exit 1 }
}

$headers = @{
    Authorization = "Bearer $Token"
    "User-Agent"  = "cpe-release"
    Accept        = "application/vnd.github+json"
}

Write-Host "Criando release $tag em $owner/$repo..."
$body = @{ tag_name = $tag; name = $tag; body = "VideoWall CPE $tag"; draft = $false; prerelease = $false } | ConvertTo-Json
$rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$owner/$repo/releases" -Headers $headers -Body $body -ContentType "application/json"

$uploadBase = $rel.upload_url -replace '\{.*\}', ''
foreach ($a in $assets) {
    $name = Split-Path $a -Leaf
    $sizeMb = [math]::Round((Get-Item $a).Length / 1MB, 1)
    Write-Host ("Enviando {0} ({1} MB)..." -f $name, $sizeMb)
    Invoke-RestMethod -Method Post -Uri "$uploadBase`?name=$name" -Headers $headers -InFile $a -ContentType "application/octet-stream" | Out-Null
}

Write-Host ""
Write-Host "Release publicada: https://github.com/$owner/$repo/releases/tag/$tag" -ForegroundColor Green
