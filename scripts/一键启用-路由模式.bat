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
echo   ExplorerTabRouter 文件夹路由模式一键启用
echo ====================================================
echo.
echo 正在写入当前用户注册表关联 (无需管理员权限，绿色安全)...

reg add "HKCU\Software\Classes\Folder\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Folder\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul

reg add "HKCU\Software\Classes\Directory\shell\open\command" /ve /t REG_SZ /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\Directory\shell\open\command" /v "DelegateExecute" /t REG_SZ /d "" /f >nul

echo [成功] 文件夹打开路由已启用！
echo 现在在任何外部双击文件夹或在程序中打开文件夹，都会自动合并至已有资源管理器窗口的标签页打开。
echo.
echo [提示]
echo - 若要强制以独立窗口打开，请按住 Ctrl + Shift 再双击文件夹。
echo - 原生支持将标签页向外拖拽（Tab Tear-off）拆分窗口，不受任何限制。
echo - 如需恢复默认，请运行同目录下的 [一键完全卸载与还原.bat]。
echo.
pause
