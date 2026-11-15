@echo off
setlocal
cd /d "%~dp0"

if not exist "bin\OpenIsland\OpenIsland.exe" (
    echo OpenIsland is not built yet. Building...
    powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\build.ps1"
    if errorlevel 1 (
        echo.
        echo Build failed. See the output above.
        pause
        exit /b 1
    )
)

start "" "bin\OpenIsland\OpenIsland.exe"
exit /b 0
