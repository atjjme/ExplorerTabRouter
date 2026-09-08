@echo off
chcp 65001 >nul
setlocal

for %%I in ("%~dp0..\bin\ExplorerTabRouter.exe") do set "EXE_PATH=%%~fI"

if not exist "%EXE_PATH%" (
    echo [提示] 尚未编译，正在自动编译 ExplorerTabRouter.exe ...
    call "%~dp0..\build.bat"
)

if not exist "%EXE_PATH%" (
    echo [错误] 未能找到可执行文件: %EXE_PATH%
    pause
    exit /b 1
)

echo ====================================================
echo   ExplorerTabRouter 后台守护与开机自启动配置
echo ====================================================
echo.
echo 正在添加当前用户开机启动项...

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" /t REG_SZ /d "\"%EXE_PATH%\" --daemon" /f >nul

echo [成功] 开机自启动项已添加！
echo 正在启动托盘守护进程...
start "" "%EXE_PATH%" --daemon

echo [成功] 托盘守护进程已启动！可在屏幕右下角托盘图标查看状态与进行快捷控制。
echo.
pause
