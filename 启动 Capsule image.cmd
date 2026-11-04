@echo off
taskkill /IM "Capsule image.exe" /F >nul 2>&1
start "" "%~dp0build\Capsule image.exe"
