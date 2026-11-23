@echo off
setlocal EnableExtensions

if "%~1"=="-h" goto :usage
if "%~1"=="--help" goto :usage
if "%~1"=="/?" goto :usage

set "TARGET_SERIAL=%~1"
if /i "%~1"=="-s" set "TARGET_SERIAL=%~2"

title Disconnect ScrcpyDeX (Samsung DeX Compatible Client)
echo =================================================
echo        Disconnecting ScrcpyDeX Session           
echo =================================================
echo [1/2] Terminating ScrcpyDeX client processes (scrcpy)...
REM Scope process termination strictly to scrcpy.exe instances launched by ScrcpyDeX.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_Process -Filter \"Name='scrcpy.exe'\" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match '--window-title=ScrcpyDeX' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>&1

REM Resolve adb only from the portable ScrcpyDeX directory.
set "ADB_EXE=%~dp0scrcpy\adb.exe"
if not exist "%ADB_EXE%" set "ADB_EXE=%~dp0adb.exe"

if not exist "%ADB_EXE%" (
    echo [WARN] Portable adb.exe not found next to ScrcpyDeX.
    echo        Skipping device-side DeX disconnect.
    goto :done
)

set "ANDROID_USER_HOME=%~dp0.android"
set "ANDROID_SDK_HOME=%~dp0"
set "HOME=%~dp0"

set "ADB_TARGET="
if defined TARGET_SERIAL set ADB_TARGET=-s "%TARGET_SERIAL%"

echo [2/2] Sending disconnect signal to Android device...
"%ADB_EXE%" %ADB_TARGET% shell "CLASSPATH=/data/local/tmp/scrcpydex-server.jar app_process /data/local/tmp com.scrcpydex.server.Server disconnect" >nul 2>&1
"%ADB_EXE%" %ADB_TARGET% shell "am broadcast -a com.sec.android.app.desktoplauncher.ACTION_STOP_DESKTOP_MODE ; am broadcast -a com.samsung.android.desktopmode.action.DESKTOP_MODE_UPDATE --ez enabled false ; pkill -f com.scrcpydex.server.Server" >nul 2>&1

:done
echo.
echo DeX session disconnected. Device display restored.
endlocal
exit /b 0

:usage
echo Usage: stop-dex.bat [serial ^| -s ^<serial^>]
echo Terminates ScrcpyDeX-managed scrcpy.exe instances and sends DeX disconnect signals via ADB.
endlocal
exit /b 0
