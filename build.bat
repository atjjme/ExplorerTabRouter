@echo off
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if not exist "%~dp0bin" (
    mkdir "%~dp0bin"
)

echo [1/2] Compiling ExplorerTabRouter.exe ...
"%CSC%" /nologo /target:winexe /win32icon:"%~dp0src\app.ico" /optimize+ /platform:anycpu /out:"%~dp0bin\ExplorerTabRouter.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll "%~dp0src\*.cs"

if %ERRORLEVEL% equ 0 (
    echo [2/2] Build SUCCESS: %~dp0bin\ExplorerTabRouter.exe
) else (
    echo [ERROR] Build FAILED!
)

echo %cmdcmdline% | find /i "%~0" >nul
if %ERRORLEVEL% equ 0 pause