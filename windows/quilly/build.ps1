$ErrorActionPreference = 'Stop'

$root         = $PSScriptRoot
$proj         = Join-Path $root 'src\Quilly.csproj'
$staging      = Join-Path $root 'src\publish'
$target       = Join-Path $root 'Quilly.exe'

$installerProj    = Join-Path $root 'src\installer\QuillyInstaller.csproj'
$installerStaging = Join-Path $root 'src\installer\publish'
$installerTarget  = Join-Path $root 'Quilly-Installer.exe'

# 1. Fecha instâncias abertas se houver
$running = Get-Process -Name 'Quilly*' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Fechando instâncias do Quilly para recompilação..." -ForegroundColor Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
}

# 2. Compila Quilly.exe (App Home)
Write-Host "1. Compilando Quilly.exe (Home)..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o $staging --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { Write-Host "Build do Quilly falhou." -ForegroundColor Red; exit 1 }

Copy-Item (Join-Path $staging 'Quilly.exe') $target -Force

# Copia a pasta UI para a raiz
$uiTarget = Join-Path $root 'ui'
if (Test-Path $uiTarget) { Remove-Item $uiTarget -Recurse -Force }
Copy-Item (Join-Path $root 'src\ui') $uiTarget -Recurse -Force

# 3. Empacota o Payload Completo (Quilly + UI + source_apps com Noty e Artry)
Write-Host "2. Empacotando Suite completa em payload.zip..." -ForegroundColor Cyan
$payloadTemp = Join-Path $staging 'payload_temp'
if (Test-Path $payloadTemp) { Remove-Item $payloadTemp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payloadTemp | Out-Null

Copy-Item $target (Join-Path $payloadTemp 'Quilly.exe') -Force
Copy-Item $uiTarget (Join-Path $payloadTemp 'ui') -Recurse -Force
Copy-Item (Join-Path $root 'source_apps') (Join-Path $payloadTemp 'source_apps') -Recurse -Force
if (Test-Path (Join-Path $root 'hyp.ico')) { Copy-Item (Join-Path $root 'hyp.ico') (Join-Path $payloadTemp 'hyp.ico') -Force }

$zipPayload = Join-Path $root 'src\installer\payload.zip'
if (Test-Path $zipPayload) { Remove-Item $zipPayload -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadTemp, $zipPayload, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipMb = [math]::Round((Get-Item $zipPayload).Length / 1MB, 1)
Write-Host "Payload compactado com sucesso: $zipMb MB" -ForegroundColor Green

# 4. Compila Quilly-Installer.exe (Mega Instalador Autocontido com o payload embutido!)
Write-Host "3. Compilando Mega-Instalador Quilly-Installer.exe..." -ForegroundColor Cyan
dotnet publish $installerProj -c Release -o $installerStaging --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) {
    Remove-Item $zipPayload -Force -ErrorAction SilentlyContinue
    Remove-Item $payloadTemp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Build do Instalador falhou." -ForegroundColor Red; exit 1
}

Copy-Item (Join-Path $installerStaging 'Quilly-Installer.exe') $installerTarget -Force

# Limpeza de arquivos temporários do build
Remove-Item $zipPayload -Force -ErrorAction SilentlyContinue
Remove-Item $payloadTemp -Recurse -Force -ErrorAction SilentlyContinue

$mbQuilly = [math]::Round((Get-Item $target).Length / 1MB, 1)
$mbInstaller = [math]::Round((Get-Item $installerTarget).Length / 1MB, 1)

Write-Host "`n>>> TUDO CONCLUÍDO COM SUCESSO! <<<" -ForegroundColor Green
Write-Host "• Quilly Home:      $target ($mbQuilly MB)" -ForegroundColor Green
Write-Host "• Quilly Mega-Instalador: $installerTarget ($mbInstaller MB)" -ForegroundColor Green
