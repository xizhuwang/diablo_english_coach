param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackageRoot)
$app = Join-Path $root 'app\DiabloEnglishCoach.exe'
$modelSetup = Join-Path $root 'scripts\setup-model.ps1'
$speechSetup = Join-Path $root '安裝口說模型.ps1'

if (-not (Test-Path -LiteralPath $app)) {
    throw "找不到程式：$app。請先解壓縮完整的 Windows x64 發布包，不要只複製 EXE。"
}
if (-not (Test-Path -LiteralPath $modelSetup) -or -not (Test-Path -LiteralPath $speechSetup)) {
    throw '安裝檔不完整，請重新下載並完整解壓縮。'
}
if ($VerifyOnly) {
    Write-Host '一鍵安裝包結構檢查通過。' -ForegroundColor Green
    return
}

Write-Host 'Game English Coach 一鍵安裝' -ForegroundColor Yellow
Write-Host '將安裝 Ollama、本機翻譯／教練模型，以及本機英文口說模型。'
Write-Host '預設推論只使用 CPU，不要求特定 Intel、AMD 或 NVIDIA 顯示卡。'
Write-Host '模型下載約 3 GB；自然語音若保持啟用，朗讀文字會送至 Microsoft。'
Write-Host ''

& $speechSetup -Destination (Join-Path $root 'app\speech-model')
& $modelSetup -Model 'qwen3.5:0.8b' -NonInteractive
& $modelSetup -Model 'qwen3.5:2b-q4_K_M' -NonInteractive

Write-Host ''
Write-Host '安裝完成，正在啟動。' -ForegroundColor Green
Start-Process -FilePath $app
