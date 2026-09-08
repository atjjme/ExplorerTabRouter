@echo off
chcp 65001 >nul
setlocal

echo ====================================================
echo   停止 ExplorerTabRouter 后台守护进程
echo ====================================================
echo.
echo 正在停止守护进程...
taskkill /f /im ExplorerTabRouter.exe >nul 2>&1
if %errorlevel% equ 0 (
    echo [成功] ExplorerTabRouter 后台守护进程已停止。
) else (
    echo [提示] 守护进程当前并未在运行。
)
echo.
pause
