using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ExplorerTabRouter {
    public static class Program {
        private const string MUTEX_NAME = @"Local\ExplorerTabRouter_Daemon_SingleInstance";

        [STAThread]
        public static void Main(string[] args) {
            // 如果无参数启动
            if (args == null || args.Length == 0) {
                // 如果是从双击 exe 打开，且没有运行守护进程，则询问或启动守护进程
                bool createdNew;
                using (Mutex mutex = new Mutex(true, MUTEX_NAME, out createdNew)) {
                    if (createdNew) {
                        // 启动托盘守护模式
                        using (DaemonWatcher daemon = new DaemonWatcher()) {
                            daemon.Start();
                        }
                    } else {
                        // 已经有守护进程在跑，打开一个新标签页
                        ExplorerManager.RouteOpen("");
                    }
                }
                return;
            }

            string firstArg = args[0].Trim();

            // 1. 守护进程模式 (--daemon / -d)
            if (string.Equals(firstArg, "--daemon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstArg, "-d", StringComparison.OrdinalIgnoreCase)) {
                
                bool createdNew;
                using (Mutex mutex = new Mutex(true, MUTEX_NAME, out createdNew)) {
                    if (!createdNew) {
                        // 已有守护进程在运行，退出当前重复实例
                        return;
                    }

                    using (DaemonWatcher daemon = new DaemonWatcher()) {
                        daemon.Start();
                    }
                }
                return;
            }

            // 2. 命令行安装关联 (--install / -i)
            if (string.Equals(firstArg, "--install", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstArg, "-i", StringComparison.OrdinalIgnoreCase)) {
                bool ok1 = RegistryHelper.InstallFolderAssociation();
                bool ok2 = RegistryHelper.InstallAutoStart();
                string msg = string.Format("ExplorerTabRouter 安装完成：\n- 文件夹打开关联: {0}\n- 开机自启动守护: {1}", 
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
