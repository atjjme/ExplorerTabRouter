@echo off
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if not exist "%~dp0bin" (
    mkdir "%~dp0bin"
)

echo [1/3] Compiling ExplorerTabRouter.exe to root directory ...
"%CSC%" /nologo /target:winexe /win32icon:"%~dp0src\app.ico" /optimize+ /platform:anycpu /out:"%~dp0ExplorerTabRouter.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll "%~dp0src\*.cs"

if %ERRORLEVEL% equ 0 (
    copy /y "%~dp0ExplorerTabRouter.exe" "%~dp0bin\ExplorerTabRouter.exe" >nul
    echo [2/3] Build SUCCESS: %~dp0ExplorerTabRouter.exe (Root)
    echo [3/3] Sync copy: %~dp0bin\ExplorerTabRouter.exe
) else (
    echo [ERROR] Build FAILED!
)

echo %cmdcmdline% | find /i "%~0" >nul
if %ERRORLEVEL% equ 0 pause