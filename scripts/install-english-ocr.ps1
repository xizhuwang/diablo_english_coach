$ErrorActionPreference = 'Stop'
$capability = 'Language.OCR~~~en-US~0.0.1.0'
if ((Get-WindowsCapability -Online -Name $capability).State -ne 'Installed') {
    Add-WindowsCapability -Online -Name $capability | Out-Null
}
if ((Get-WindowsCapability -Online -Name $capability).State -ne 'Installed') { throw '英文 OCR 尚未安裝完成' }
