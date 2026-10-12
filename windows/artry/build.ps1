$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Caminhos dos projetos e alvos
$project = Join-Path $root 'src\Artry.csproj'
$stage = Join-Path $root 'src\publish'
$target = Join-Path $root 'Artry.exe'

$setupProject = Join-Path $root 'src\installer\ArtryInstaller.csproj'
$setupStage = Join-Path $root 'src\installer\publish'
$setupTarget = Join-Path $root 'Artry-Installer.exe'

$mobileProject = Join-Path $root 'src\mobile\Artry.Mobile.csproj'
$mobileRes = Join-Path $root 'src\mobile\Resources'
$mobileApkTarget = Join-Path $root 'Artry.apk'

# Variáveis do Android SDK / Java
$env:JAVA_HOME = "C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot"
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:Path = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:Path"

# 0. Gerenciamento e incremento automático de micro-versão
$versionFile = Join-Path $root 'version.json'
$vData = $null
if (Test-Path $versionFile) {
    try {
        $vData = Get-Content $versionFile -Raw | ConvertFrom-Json
    } catch {
        $vData = $null
    }
}
if (-not $vData) {
    $vData = [PSCustomObject]@{ major = 1; minor = 0; micro = 0 }
}
$vData.micro = [int]$vData.micro + 1
$appVersion = "$($vData.major).$($vData.minor).$($vData.micro)"
$fileVersion = "$($vData.major).$($vData.minor).$($vData.micro).0"
$vData | ConvertTo-Json | Set-Content $versionFile -Encoding UTF8
Write-Host "=== Artry Build: Versão $appVersion (Micro-versão interna: $fileVersion) ===" -ForegroundColor Magenta

# 1. Gera o ícone multi-resolução transparente e mipmaps mobile antes da publicação
Write-Host "1. Gerando ícone Artry e mipmaps Android com transparência total..." -ForegroundColor Cyan
$maker = Join-Path $root 'src\iconmaker\IconMaker.csproj'
$icon = Join-Path $root 'src\icon.ico'
$preview = Join-Path $root 'icon-preview.png'
dotnet run --project $maker -c Release -- $icon $preview $mobileRes
if ($LASTEXITCODE -ne 0) { Write-Host 'Não foi possível gerar os ícones.' -ForegroundColor Red; exit 1 }

# 2. Fecha instâncias ativas do Artry e Escriba se houver
Write-Host "2. Verificando e encerrando instâncias ativas..." -ForegroundColor Cyan
$running = Get-Process -Name 'Artry*', 'Escriba*' -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 3. Compila e publica o Artry.exe standalone
Write-Host "3. Compilando Artry.exe (Desktop v$appVersion)..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $stage -p:Version=$appVersion -p:FileVersion=$fileVersion -p:AssemblyVersion=$fileVersion -p:InformationalVersion=$appVersion --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { exit 1 }
if (Test-Path $target) { Remove-Item $target -Force }
Copy-Item (Join-Path $stage 'Artry.exe') $target -Force
(Get-Item $target).LastWriteTime = Get-Date
(Get-Item $target).CreationTime = Get-Date
Write-Host "Desktop pronto: $target (v$appVersion)" -ForegroundColor Green

# 3.1 Sincronização automática para a suite Quilly (source_apps)
$quillySourceDir = "D:\DATA\Trabalhos\Projeto\Apps\Quilly\source_apps\Artry"
if (-not (Test-Path (Split-Path $quillySourceDir -Parent))) {
    $fallbackQuilly = Join-Path (Split-Path $root -Parent) 'Quilly\source_apps\Artry'
    if (Test-Path (Split-Path (Split-Path $fallbackQuilly -Parent) -Parent)) {
        $quillySourceDir = $fallbackQuilly
    }
}
if (Test-Path (Split-Path $quillySourceDir -Parent)) {
    if (-not (Test-Path $quillySourceDir)) {
        New-Item -ItemType Directory -Force -Path $quillySourceDir | Out-Null
    }
    $quillyExe = Join-Path $quillySourceDir 'Artry.exe'
    Copy-Item $target $quillyExe -Force
    (Get-Item $quillyExe).LastWriteTime = Get-Date
    (Get-Item $quillyExe).CreationTime = Get-Date
    $quillyHypIco = Join-Path $quillySourceDir 'hyp.ico'
    if (Test-Path (Join-Path $root 'hyp.ico')) { Copy-Item (Join-Path $root 'hyp.ico') $quillyHypIco -Force }
    Write-Host "Sincronizado com Quilly source_apps: $quillyExe (v$appVersion)" -ForegroundColor Green
}

# 4. Compila e publica o Instalador em arquivo único
Write-Host "4. Compilando Instalador Artry-Installer.exe (v$appVersion)..." -ForegroundColor Cyan
dotnet publish $setupProject -c Release -o $setupStage -p:Version=$appVersion -p:FileVersion=$fileVersion -p:AssemblyVersion=$fileVersion -p:InformationalVersion=$appVersion --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { exit 1 }
if (Test-Path $setupTarget) { Remove-Item $setupTarget -Force }
Copy-Item (Join-Path $setupStage 'Artry-Installer.exe') $setupTarget -Force
(Get-Item $setupTarget).LastWriteTime = Get-Date
(Get-Item $setupTarget).CreationTime = Get-Date
Write-Host "Instalador pronto: $setupTarget" -ForegroundColor Green

# 5. Compila e gera o pacote APK para Android
Write-Host "5. Compilando Artry.apk (Android v$appVersion)..." -ForegroundColor Cyan
dotnet build $mobileProject -c Release -p:ApplicationVersion=$($vData.micro) -p:ApplicationDisplayVersion=$appVersion --nologo -v:m -tl:off
if ($LASTEXITCODE -ne 0) { exit 1 }

# Localiza o APK assinado gerado na pasta bin/Release/net10.0-android
$apkFound = Get-ChildItem -Path (Join-Path $root 'src\mobile\bin\Release\net10.0-android') -Filter '*-Signed.apk' -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $apkFound) {
    $apkFound = Get-ChildItem -Path (Join-Path $root 'src\mobile\bin\Release\net10.0-android') -Filter '*.apk' -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if ($apkFound) {
    Copy-Item $apkFound.FullName $mobileApkTarget -Force
    (Get-Item $mobileApkTarget).LastWriteTime = Get-Date
    (Get-Item $mobileApkTarget).CreationTime = Get-Date
    Write-Host "Android APK pronto: $mobileApkTarget ($([math]::Round((Get-Item $mobileApkTarget).Length / 1MB, 2)) MB)" -ForegroundColor Green
} else {
    Write-Host "Aviso: APK gerado não encontrado para cópia." -ForegroundColor Yellow
}

# 6. Limpa profundamente o cache de ícones do Windows Explorer
Write-Host "6. Limpando cache de ícones do Windows Explorer..." -ForegroundColor Cyan
try {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    $expDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
    Get-ChildItem -Path $expDir -Filter 'iconcache*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $expDir -Filter 'thumbcache*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $env:LOCALAPPDATA 'IconCache.db') -Force -ErrorAction SilentlyContinue

    Start-Process explorer
    Start-Sleep -Milliseconds 500
    & ie4uinit.exe -show
} catch {}

Write-Host "`n>>> TUDO CONCLUÍDO COM SUCESSO! <<<" -ForegroundColor Green
Write-Host "1. Aplicativo Desktop: $target (v$appVersion)"
Write-Host "2. Instalador Windows: $setupTarget (v$appVersion)"
Write-Host "3. Pacote Android APK: $mobileApkTarget (v$appVersion)"
if (Test-Path $quillySourceDir) {
    Write-Host "4. Quilly Suite Source: $(Join-Path $quillySourceDir 'Artry.exe') (v$appVersion)"
}
