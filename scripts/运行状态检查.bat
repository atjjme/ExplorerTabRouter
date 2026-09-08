@echo off
chcp 65001 >nul
setlocal

echo ====================================================
echo   ExplorerTabRouter 运行状态检查
echo ====================================================
echo.

:: 1. 检查可执行文件
if exist "%~dp0..\bin\ExplorerTabRouter.exe" (
    echo [可执行文件] 正常存在: %~dp0..\bin\ExplorerTabRouter.exe
) else (
    echo [可执行文件] 未找到，请先运行 build.bat 进行编译！
)
echo.

:: 2. 检查守护进程运行状态
tasklist /fi "imagename eq ExplorerTabRouter.exe" 2>nul | find /i "ExplorerTabRouter.exe" >nul
if %errorlevel% equ 0 (
    echo [守护进程状态] 正在后台运行中
) else (
    echo [守护进程状态] 未运行
)
echo.

:: 3. 检查文件夹关联注册表
reg query "HKCU\Software\Classes\Folder\shell\open\command" 2>nul | find /i "ExplorerTabRouter" >nul
if %errorlevel% equ 0 (
    echo [文件夹打开关联] 已启用 [HKCU Folder\shell\open\command -^> ExplorerTabRouter]
) else (
    echo [文件夹打开关联] 未启用 [使用系统默认]
)
echo.

:: 4. 检查开机自启
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" 2>nul | find /i "ExplorerTabRouter" >nul
if %errorlevel% equ 0 (
    reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" 2>nul | find /i "--silent" >nul
    if %errorlevel% equ 0 (
        echo [开机自启动] 已启用 [纯静默模式，无托盘图标]
    ) else (
        echo [开机自启动] 已启用 [托盘图标模式]
    )
) else (
    echo [开机自启动] 未启用
)
echo.
echo ====================================================
pause
