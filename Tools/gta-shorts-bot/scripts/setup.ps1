param([switch]$InstallTools, [switch]$DownloadModels)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
if ($InstallTools) {
    foreach ($package in @('Python.Python.3.12', 'Gyan.FFmpeg', 'Ollama.Ollama')) {
        winget install --id $package --exact --silent --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw "winget failed for $package" }
    }
}
$pythonExe = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
if (-not (Test-Path -LiteralPath $pythonExe)) { throw 'Install Python 3.12 for this user first (see README).' }
if (-not (Test-Path '.venv\Scripts\python.exe')) { & $pythonExe -m venv .venv }
if ($LASTEXITCODE -ne 0) { throw 'Virtual environment creation failed' }
& .\.venv\Scripts\python.exe -m pip install -e .
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed' }
if (-not (Test-Path config.json)) { Copy-Item config.example.json config.json }
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
$config = Get-Content config.json -Raw | ConvertFrom-Json
foreach ($name in @('ffmpeg', 'ffprobe')) {
    $exe = Get-Command $name -ErrorAction SilentlyContinue
    if ($exe) { $config.$name = $exe.Source }
    else {
        $packageRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
        $found = Get-ChildItem -LiteralPath $packageRoot -Filter "$name.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $config.$name = $found.FullName }
    }
}
$config | ConvertTo-Json -Depth 10 | Set-Content config.json -Encoding utf8
if ($DownloadModels) {
    $ollamaExe = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    & $ollamaExe pull $config.model
    if ($LASTEXITCODE -ne 0) { throw 'Start Ollama then rerun with -DownloadModels' }
    New-Item -ItemType Directory -Path models -Force | Out-Null
    Push-Location models
    try {
        & ..\.venv\Scripts\python.exe -m piper.download_voices en_US-ljspeech-high
        if ($LASTEXITCODE -ne 0) { throw 'Voice download failed' }
    }
    finally { Pop-Location }
}
& .\.venv\Scripts\python.exe -m shorts.cli doctor
