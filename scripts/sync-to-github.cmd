@echo off
chcp 65001 >nul
title Синхронизация Startup Monitor с GitHub (origin: SM ^| startup-monitor: Startup-Monitor)

echo ============================================================
echo      Синхронизация Startup Monitor с репозиториями GitHub
echo ============================================================
echo.
echo 1. Публичный репозиторий:  https://github.com/gms-cod/SM
echo 2. Приватный репозиторий: https://github.com/gms-cod/Startup-Monitor
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-repositories.ps1"

echo.
pause
