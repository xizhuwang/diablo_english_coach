@echo off
if exist "%~dp0publish-v012\DiabloEnglishCoach.exe" (
    start "" "%~dp0publish-v012\DiabloEnglishCoach.exe"
) else if exist "%~dp0publish-latest\DiabloEnglishCoach.exe" (
    start "" "%~dp0publish-latest\DiabloEnglishCoach.exe"
) else if exist "%~dp0publish-responsive\DiabloEnglishCoach.exe" (
    start "" "%~dp0publish-responsive\DiabloEnglishCoach.exe"
) else if exist "%~dp0publish-readable\DiabloEnglishCoach.exe" (
    start "" "%~dp0publish-readable\DiabloEnglishCoach.exe"
) else if exist "%~dp0app\DiabloEnglishCoach.exe" (
    start "" "%~dp0app\DiabloEnglishCoach.exe"
) else (
    echo New coach executable not found. Build or download the latest package first.
    pause
)
