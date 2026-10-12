# Recompila o Noty depois de mexer em src\.
#
# A publicacao vai para src\publish e so depois o exe e copiado pra ca.
# Nao publique direto na raiz do app: o SDK trata a pasta de saida como area de
# build e exclui os .cs de dentro dela, o que quebra a compilacao com um
# "CS5001: no entry point" bem enganoso.

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$proj    = Join-Path $root 'src\Noty.csproj'
$staging = Join-Path $root 'src\publish'
$target  = Join-Path $root 'Noty.exe'

$running = Get-Process -Name 'Noty' -ErrorAction SilentlyContinue |
           Where-Object { $_.Path -eq $target }
if ($running) {
    Write-Host "O Noty esta aberto. Fecha ele antes de recompilar." -ForegroundColor Yellow
    exit 1
}

Write-Host "Publicando..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o $staging --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { Write-Host "Build falhou." -ForegroundColor Red; exit 1 }

Copy-Item (Join-Path $staging 'Noty.exe') $target -Force

# Sincronização automática para a suite Quilly (source_apps)
$quillySourceDir = "D:\DATA\Trabalhos\Projeto\Apps\Quilly\source_apps\Noty"
if (Test-Path (Split-Path $quillySourceDir -Parent)) {
    if (-not (Test-Path $quillySourceDir)) {
        New-Item -ItemType Directory -Force -Path $quillySourceDir | Out-Null
    }
    $quillyExe = Join-Path $quillySourceDir 'Noty.exe'
    Copy-Item $target $quillyExe -Force
    (Get-Item $quillyExe).LastWriteTime = Get-Date
    (Get-Item $quillyExe).CreationTime = Get-Date
    $quillyHypIco = Join-Path $quillySourceDir 'hyp.ico'
    if (Test-Path (Join-Path $root 'hyp.ico')) { Copy-Item (Join-Path $root 'hyp.ico') $quillyHypIco -Force }
    Write-Host "Sincronizado com Quilly source_apps: $quillyExe" -ForegroundColor Green
}

$mb = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "OK: $target ($mb MB)" -ForegroundColor Green
