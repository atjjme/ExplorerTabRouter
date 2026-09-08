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
echo   ExplorerTabRouter 托盘版后台守护与开机自启配置
echo ====================================================
echo.
echo 正在停止可能正在运行的旧实例...
taskkill /f /im ExplorerTabRouter.exe >nul 2>&1

echo 正在写入文件夹打开关联 (免管理员权限)...
reg add "HKCU\Software\Classes\Folder\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Folder\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul
reg add "HKCU\Software\Classes\Directory\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Directory\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul

echo 正在添加当前用户开机自启动项 (托盘图标)...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" /t REG_SZ /d "\"%EXE_PATH%\" --daemon" /f >nul

echo 正在启动托盘后台守护进程...
start "" "%EXE_PATH%" --daemon

echo.
echo ====================================================
echo [成功] 托盘后台守护进程已启动！
echo 可在屏幕右下角托盘查看状态并右键快捷操作。
echo ====================================================
echo.
pause
