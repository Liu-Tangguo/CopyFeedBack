@echo off
setlocal
set "DEST=%LOCALAPPDATA%\CopyFeedback"

echo [1/3] Disabling autostart ...
if exist "%DEST%\CopyFeedback.exe" start "" /wait "%DEST%\CopyFeedback.exe" --disable-autostart

echo [2/3] Quitting running instance ...
if exist "%DEST%\CopyFeedback.exe" start "" /wait "%DEST%\CopyFeedback.exe" --quit
timeout /t 1 >nul

echo [3/3] Removing %DEST% ...
if exist "%DEST%" rmdir /s /q "%DEST%"
if exist "%DEST%" (
  echo [WARN] some files were not removed, exit the program from the tray and retry.
  exit /b 1
)
echo Uninstalled.
timeout /t 3 >nul
endlocal
