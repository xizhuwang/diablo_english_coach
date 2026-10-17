# Privacy

Last reviewed: 2026-09-05

Game English Coach is a local Windows overlay. It has no analytics, advertising SDK, account system, crash uploader, or telemetry endpoint.

## What stays on the computer

- Screenshots of the user-selected dialogue and quest rectangles are processed in memory by Windows OCR. The application does not save or upload screenshots.
- The default CPU translator sends text through standard input to a child process in `translation-runtime`; no network port is opened. Background OCR repair and coaching use local Ollama. OCR repair checks require a loopback endpoint.
- Translation cache, lesson cache, build-reference cache, and settings are stored below `%LOCALAPPDATA%\DiabloEnglishCoach`.
- Keyboard polling retains only the time since a gameplay key or Space was pressed. It does not record typed text, key sequences, passwords, mouse input, or controller input.
- Automatic speaking is off by default. When enabled, microphone audio is kept in memory, recognized locally with Vosk, cleared after recognition, and not written to disk or uploaded.

## Optional network features

- Online natural voice sends only the paragraph to be spoken to Microsoft's Edge speech service through `EdgeTTS.DotNet`. Turn off **設定 → 使用自然女聲** to use an installed Windows voice instead.
- Azure Translator is optional. When selected, filtered OCR text is sent to Microsoft Translator. Its key is stored in Windows Credential Manager and is not written to the project configuration or repository.
- At startup, the build-reference component may request one public Dexerto page. It sends a normal HTTP request and does not include screenshots, OCR text, microphone audio, character name, or gameplay history.
- Installation downloads Ollama, two Qwen models, the Vosk English model, portable Python from python.org, fixed Python wheels from files.pythonhosted.org and an Argos English-Chinese model from argos-net.com. Translation downloads are checked against pinned SHA256 values. The optional English OCR installer uses Windows Update.

Online services and websites necessarily receive normal network metadata such as the user's public IP address and request time, even when no game text is included in a build-reference request.

The trade/chat filter is a convenience filter, not a security boundary. Users should keep the dialogue capture rectangle away from private chat and should not select Azure translation for sensitive text.

## Remove local data

Close the coach, then delete `%LOCALAPPDATA%\DiabloEnglishCoach`. Remove any optional Azure key with **設定 → 翻譯方式 → 移除 Azure 金鑰**. Ollama models are managed separately by Ollama.

## Scope of this review

This document describes the repository code and packaged dependencies reviewed on the date above. Operating-system components and optional online services remain subject to their providers' privacy terms. Future dependency or service changes require another review.
