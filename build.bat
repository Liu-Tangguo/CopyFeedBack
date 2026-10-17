@echo off
setlocal
pushd "%~dp0."

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. Please install .NET Framework 4.8.
  popd
  exit /b 1
)

if not exist "resources\app.ico" (
  echo [1/3] Generating icon ...
  powershell -NoProfile -ExecutionPolicy Bypass -File "tools\make-icon.ps1"
  if errorlevel 1 (
    echo [ERROR] icon generation failed
    popd
    exit /b 1
  )
)

if not exist "dist" mkdir "dist"

echo [2/3] Compiling dist\CopyFeedback.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /warn:4 /out:"dist\CopyFeedback.exe" /win32icon:resources\app.ico /win32manifest:src\app.manifest /resource:resources\app.ico,CopyFeedback.app.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\*.cs
if errorlevel 1 (
  echo [ERROR] compile failed
  popd
  exit /b 1
)

echo [3/3] Done: %CD%\dist\CopyFeedback.exe
popd
endlocal
