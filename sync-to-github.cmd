@echo off
chcp 65001 >nul
title Синхронизация Startup Monitor с GitHub (gms-cod/SM)

echo ============================================================
echo      Синхронизация Startup Monitor с GitHub: gms-cod/SM
echo ============================================================
echo.
echo Репозиторий: https://github.com/gms-cod/SM
echo.
echo Отправка ветки main и тегов в GitHub...
echo.

git push -u origin main --tags --force

echo.
if %ERRORLEVEL% EQU 0 (
    echo ============================================================
    echo [УСПЕХ] Синхронизация успешно завершена!
    echo.
    echo GitHub Actions сейчас соберет инсталлятор и создаст релиз:
    echo https://github.com/gms-cod/SM/actions
    echo https://github.com/gms-cod/SM/releases
    echo ============================================================
) else (
    echo ============================================================
    echo [ОШИБКА] Произошла ошибка при отправке.
    echo ============================================================
)

echo.
pause
