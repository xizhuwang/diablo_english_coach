@echo off
chcp 65001 >nul
title Game English Coach 安裝
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-all.ps1" -PackageRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo 安裝未完成，請保留上方錯誤訊息並到 GitHub 回報。
  pause
)
