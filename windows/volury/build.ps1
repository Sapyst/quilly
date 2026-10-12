$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Caminhos
$project = Join-Path $root 'src\Volury.csproj'
$stage   = Join-Path $root 'src\publish'
$target  = Join-Path $root 'Volury.exe'

# 0. Versionamento automático
$versionFile = Join-Path $root 'version.json'
$vData = $null
if (Test-Path $versionFile) {
    try { $vData = Get-Content $versionFile -Raw | ConvertFrom-Json } catch { $vData = $null }
}
if (-not $vData) {
    $vData = [PSCustomObject]@{ major = 1; minor = 0; micro = 0 }
}
$vData.micro = [int]$vData.micro + 1
$appVersion  = "$($vData.major).$($vData.minor).$($vData.micro)"
$fileVersion = "$($vData.major).$($vData.minor).$($vData.micro).0"
$vData | ConvertTo-Json | Set-Content $versionFile -Encoding UTF8
Write-Host "=== Volury Build: Versão $appVersion ===" -ForegroundColor Magenta

# 1. Fecha instâncias ativas
Write-Host "1. Verificando instâncias ativas..." -ForegroundColor Cyan
$running = Get-Process -Name 'Volury' -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 2. Compila e publica
Write-Host "2. Compilando Volury.exe (v$appVersion)..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $stage `
    -p:Version=$appVersion `
    -p:FileVersion=$fileVersion `
    -p:AssemblyVersion=$fileVersion `
    -p:InformationalVersion=$appVersion `
    --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { Write-Host "Build falhou." -ForegroundColor Red; exit 1 }

if (Test-Path $target) { Remove-Item $target -Force }
Copy-Item (Join-Path $stage 'Volury.exe') $target -Force
(Get-Item $target).LastWriteTime = Get-Date
(Get-Item $target).CreationTime  = Get-Date
$mb = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "Desktop pronto: $target ($mb MB, v$appVersion)" -ForegroundColor Green

# 3. Sincroniza com Quilly Suite (source_apps)
$quillySourceDir = "D:\DATA\Trabalhos\Projeto\Apps\Quilly\source_apps\Volury"
if (Test-Path (Split-Path $quillySourceDir -Parent)) {
    if (-not (Test-Path $quillySourceDir)) {
        New-Item -ItemType Directory -Force -Path $quillySourceDir | Out-Null
    }
    $quillyExe = Join-Path $quillySourceDir 'Volury.exe'
    Copy-Item $target $quillyExe -Force
    (Get-Item $quillyExe).LastWriteTime = Get-Date
    (Get-Item $quillyExe).CreationTime  = Get-Date

    # Copia hyp.ico se existir
    $hypIcoSrc = Join-Path $root 'src\hyp.ico'
    if (Test-Path $hypIcoSrc) {
        Copy-Item $hypIcoSrc (Join-Path $quillySourceDir 'hyp.ico') -Force
    }

    Write-Host "Sincronizado com Quilly source_apps: $quillyExe (v$appVersion)" -ForegroundColor Green
}

Write-Host "`n>>> VOLURY BUILD CONCLUÍDO COM SUCESSO! <<<" -ForegroundColor Green
Write-Host "1. Aplicativo Desktop: $target (v$appVersion)"
if (Test-Path $quillySourceDir) {
    Write-Host "2. Quilly Suite Source: $(Join-Path $quillySourceDir 'Volury.exe') (v$appVersion)"
}
