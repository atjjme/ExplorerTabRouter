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
echo   ExplorerTabRouter 纯静默后台守护与开机自启配置
echo ====================================================
echo.
echo 正在停止可能正在运行的旧实例...
taskkill /f /im ExplorerTabRouter.exe >nul 2>&1

echo 正在写入文件夹打开关联 (免管理员权限)...
reg add "HKCU\Software\Classes\Folder\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Folder\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul
reg add "HKCU\Software\Classes\Directory\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Directory\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul

echo 正在添加当前用户开机自启动项 (静默模式)...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ExplorerTabRouter" /t REG_SZ /d "\"%EXE_PATH%\" --daemon --silent" /f >nul

echo 正在启动【纯静默】后台守护进程...
start "" "%EXE_PATH%" --daemon --silent

echo.
echo ====================================================
echo [成功] 纯静默后台守护已成功启动！
echo.
echo 特性说明：
echo 1. 【无托盘图标】：完全在后台静默运行，托盘区无任何图标打扰。
echo 2. 【零闪烁合并】：新文件夹秒级透明移出屏幕，平滑合并为新标签页。
echo 3. 【支持标签拖出】：需要独立窗口时，直接用鼠标把 Tab 拖出窗口拆分即可！
echo 4. 【开机自动运行】：已加入当前用户自启项，重启电脑后仍自动静默生效。
echo.
echo 提示：
echo - 如需检查状态，可双击 [运行状态检查.bat]；
echo - 如需停止运行，可双击 [停止后台守护.bat]；
echo - 如需彻底还原，可双击 [一键完全卸载与还原.bat]。
echo ====================================================
echo.
pause
