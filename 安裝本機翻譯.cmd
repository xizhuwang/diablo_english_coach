@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-model.ps1" -Model "qwen3.5:0.8b"
if errorlevel 1 pause
