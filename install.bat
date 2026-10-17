@echo off
setlocal
pushd "%~dp0."
set "DEST=%LOCALAPPDATA%\CopyFeedback"

if not exist "dist\CopyFeedback.exe" (
  echo [ERROR] dist\CopyFeedback.exe not found. Run build.bat first.
  popd
  exit /b 1
)

echo [1/4] Stopping old instance ...
if exist "%DEST%\CopyFeedback.exe" start "" /wait "%DEST%\CopyFeedback.exe" --quit
start "" /wait "dist\CopyFeedback.exe" --quit
timeout /t 2 >nul

echo [2/4] Copying program to %DEST% ...
if not exist "%DEST%" mkdir "%DEST%"
set tries=0
:retry_copy
copy /y "dist\CopyFeedback.exe" "%DEST%\CopyFeedback.exe" >nul
if not errorlevel 1 goto copy_ok
set /a tries=%tries%+1
if %tries% geq 15 (
  echo [ERROR] copy failed, program may still be running.
  popd
  exit /b 1
)
timeout /t 1 >nul
goto retry_copy
:copy_ok

echo [3/4] Enabling autostart ...
"%DEST%\CopyFeedback.exe" --enable-autostart
if errorlevel 1 echo [WARN] could not enable autostart, use tray menu instead.

echo [4/4] Starting CopyFeedback ...
start "" "%DEST%\CopyFeedback.exe" --startup

echo.
echo Installed to: %DEST%
echo Autostart: enabled (turn off via tray menu: Autostart)
timeout /t 3 >nul
popd
endlocal
