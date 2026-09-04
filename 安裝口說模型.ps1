param([string]$Destination = (Join-Path $PSScriptRoot 'speech-model'))
$ErrorActionPreference = 'Stop'
$modelName = 'vosk-model-small-en-us-0.15'
$expectedHash = '30F26242C4EB449F948E42CB302DD7A686CB29A3423A8367F99FF41780942498'
$destinationPath = [IO.Path]::GetFullPath($Destination)
$targetPath = Join-Path $destinationPath $modelName
if (Test-Path -LiteralPath $targetPath) {
    if ((Test-Path -LiteralPath (Join-Path $targetPath 'am/final.mdl')) -and
        (Test-Path -LiteralPath (Join-Path $targetPath 'conf/model.conf'))) {
        Write-Host "口說模型已存在：$targetPath"
        exit 0
    }
    throw "模型目錄不完整。請先將此資料夾重新命名備份，再重試：$targetPath"
}
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$archivePath = Join-Path $destinationPath ('download-' + [Guid]::NewGuid().ToString('N') + '.zip')
Write-Host '下載 Vosk 官方小型英文模型（約 40 MB）。此步驟不使用麥克風。'
Invoke-WebRequest -Uri 'https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip' -OutFile $archivePath
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "模型雜湊不符，未解壓縮。請檢查官方來源。下載檔保留於：$archivePath"
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $destinationPath
# Remove only this freshly created, hash-verified download (never a directory).
Remove-Item -LiteralPath $archivePath
Write-Host "安裝完成：$targetPath"
Write-Host '開發版請重新 publish；直接使用版請把 speech-model 放在 EXE 旁。'
