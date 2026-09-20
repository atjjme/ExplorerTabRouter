using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ExplorerTabRouter {
    public static class Program {
        private const string MUTEX_NAME = @"Local\ExplorerTabRouter_Daemon_SingleInstance";

        [STAThread]
        public static void Main(string[] args) {
            // 如果无参数启动（直接双击 exe）
            if (args == null || args.Length == 0) {
                bool createdNew;
                using (Mutex mutex = new Mutex(true, MUTEX_NAME, out createdNew)) {
                    if (createdNew) {
                        // 默认开机自启：若尚未配置自启动，自动注册当前程序为开机自启
                        if (!RegistryHelper.IsAutoStartInstalled()) {
                            RegistryHelper.InstallAutoStart(silent: false);
                        }
                        // 启动后台托盘守护模式
                        using (DaemonWatcher daemon = new DaemonWatcher()) {
                            daemon.Start(showTray: true);
                        }
                    } else {
                        // 已经有守护进程在跑，在当前窗口新建一个标签页
                        ExplorerManager.RouteOpen("");
                    }
                }
                return;
            }

            string firstArg = args[0].Trim();

            // 检查是否包含守护模式标志或静默标志
            bool isDaemon = false;
            bool isSilent = false;
            for (int i = 0; i < args.Length; i++) {
                string a = args[i].Trim();
                if (string.Equals(a, "--daemon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-d", StringComparison.OrdinalIgnoreCase)) {
                    isDaemon = true;
                } else if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(a, "--no-tray", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase)) {
                    isSilent = true;
                }
            }

            // 1. 守护进程模式 (--daemon, 包含可选的 --silent)
            if (isDaemon || (args.Length == 1 && isSilent)) {
                bool createdNew;
                using (Mutex mutex = new Mutex(true, MUTEX_NAME, out createdNew)) {
                    if (!createdNew) {
                        // 已有守护进程在运行，退出当前重复实例
                        return;
                    }

                    using (DaemonWatcher daemon = new DaemonWatcher()) {
                        daemon.Start(showTray: !isSilent);
                    }
                }
                return;
            }

            // 2. 命令行安装关联 (--install-silent / --install / -i)
            if (string.Equals(firstArg, "--install-silent", StringComparison.OrdinalIgnoreCase)) {
                bool ok1 = RegistryHelper.InstallFolderAssociation();
                bool ok2 = RegistryHelper.InstallAutoStart(silent: true);
                string msg = string.Format("ExplorerTabRouter 纯静默后台模式配置完成：\n- 文件夹打开关联: {0}\n- 开机自启(无托盘图标): {1}", 
                    ok1 ? "成功" : "失败", ok2 ? "成功" : "失败");
                MessageBox.Show(msg, "ExplorerTabRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.Equals(firstArg, "--install", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstArg, "-i", StringComparison.OrdinalIgnoreCase)) {
                bool ok1 = RegistryHelper.InstallFolderAssociation();
                bool ok2 = RegistryHelper.InstallAutoStart(silent: false);
                string msg = string.Format("ExplorerTabRouter 托盘后台模式配置完成：\n- 文件夹打开关联: {0}\n- 开机自启(托盘图标): {1}", 
                    ok1 ? "成功" : "失败", ok2 ? "成功" : "失败");
                MessageBox.Show(msg, "ExplorerTabRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 3. 命令行卸载还原 (--uninstall / -u)
            if (string.Equals(firstArg, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstArg, "-u", StringComparison.OrdinalIgnoreCase)) {
                bool ok1 = RegistryHelper.UninstallFolderAssociation();
                bool ok2 = RegistryHelper.UninstallAutoStart();
                string msg = string.Format("ExplorerTabRouter 已卸载并还原：\n- 文件夹关联: {0}\n- 开机自启动: {1}", 
                    ok1 ? "已还原系统默认" : "失败", ok2 ? "已移除" : "失败");
                MessageBox.Show(msg, "ExplorerTabRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 4. 路由模式：将传入的路径在现有窗口 Tab 打开
            string targetPath = string.Join(" ", args).Trim('\"');
            ExplorerManager.RouteOpen(targetPath);
        }
    }
}
