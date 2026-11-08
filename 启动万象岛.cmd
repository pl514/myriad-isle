@echo off
taskkill /IM "MyriadIsle.exe" /F >nul 2>&1
start "" "%~dp0build\MyriadIsle.exe"
