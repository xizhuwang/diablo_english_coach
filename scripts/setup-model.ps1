param(
    [string]$Model = 'qwen3.5:2b-q4_K_M'
)

$ErrorActionPreference = 'Stop'

function Find-Ollama {
    $command = Get-Command 'ollama.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $knownPath = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path -LiteralPath $knownPath) { return $knownPath }
    return $null
}

Write-Host 'Diablo English Coach - local model setup' -ForegroundColor Yellow
Write-Host 'This installs Ollama and downloads about 2 GB for the coach model.'
Write-Host ''

$ollamaPath = Find-Ollama
if (-not $ollamaPath) {
    $installerPath = Join-Path $env:TEMP 'OllamaSetup-DiabloEnglishCoach.exe'
    Write-Host 'Downloading the official Ollama installer...'
    Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
    $curl = Get-Command 'curl.exe' -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source --fail --location --retry 3 --output $installerPath 'https://ollama.com/download/OllamaSetup.exe'
        if ($LASTEXITCODE -ne 0) { throw "Ollama download failed with exit code $LASTEXITCODE." }
    }
    else {
        Invoke-WebRequest -UseBasicParsing 'https://ollama.com/download/OllamaSetup.exe' -OutFile $installerPath
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    if ($signature.Status -ne 'Valid') {
        Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
        throw "The downloaded Ollama installer did not have a valid digital signature: $($signature.Status)"
    }
    Write-Host 'Starting the Ollama installer...'
    Start-Process -FilePath $installerPath -Wait
    $ollamaPath = Find-Ollama
}

if (-not $ollamaPath) {
    throw 'Ollama was not found after setup. Restart Windows, then run this file again.'
}

try {
    Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 3 | Out-Null
}
catch {
    Write-Host 'Starting Ollama in the background...'
    Start-Process -FilePath $ollamaPath -ArgumentList 'serve' -WindowStyle Hidden
    Start-Sleep -Seconds 3
}

Write-Host "Downloading model: $Model" -ForegroundColor Cyan
& $ollamaPath pull $Model
if ($LASTEXITCODE -ne 0) { throw "Model download failed with exit code $LASTEXITCODE." }

Write-Host ''
Write-Host 'Setup complete. You can now open DiabloEnglishCoach.exe.' -ForegroundColor Green
Read-Host 'Press Enter to close'
