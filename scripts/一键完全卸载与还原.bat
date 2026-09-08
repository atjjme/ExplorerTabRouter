@echo off
chcp 65001 >nul
setlocal

echo ====================================================
echo   ExplorerTabRouter 一键完全卸载与还原出厂默认
echo ====================================================
echo.
echo 正在还原系统默认设置...

:: 1. 结束可能正在运行的守护进程
taskkill /f /im ExplorerTabRouter.exe >nul 2>&1

:: 2. 删除当前用户的文件夹关联 (删除后系统自动回退到 Windows 默认 explorer.exe)
reg delete "HKCU\Software\Classes\Folder\shell\open" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Directory\shell\open" /f >nul 2>&1

:: 3. 移除自启动项
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" /f >nul 2>&1

echo.
echo [成功] 已完全还原 Windows 原生默认设置！
echo - 文件夹打开方式已恢复系统默认
echo - 守护进程已停止并移出开机启动
echo - 绿色单文件无残留，您可以安全删除本工具文件夹
echo.
pause
