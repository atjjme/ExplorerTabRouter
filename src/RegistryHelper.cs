using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ExplorerTabRouter {
    public static class RegistryHelper {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "ExplorerTabRouter";

        public static string GetExecutablePath() {
            return Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        }

        /// <summary>
        /// 自动清理历史遗留的免常驻注册表文件夹关联，确保系统 100% 恢复原生纯净
        /// </summary>
        public static void CleanupLegacyFolderAssociation() {
            try {
                // 清理 HKCU 下残留的 Folder\shell 项，避免其覆盖系统 HKLM 默认关联导致回收站等双击失效
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Folder", false)) {
                    if (key != null) {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Folder", false);
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell", false)) {
                    if (key != null) {
                        // 如果 shell 下没有任何其他自定义命令，则清理掉以还原默认
                        if (key.SubKeyCount == 0) {
                            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell", false);
                        }
                    }
                }
            } catch {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 启用开机自启动
        /// </summary>
        public static bool InstallAutoStart(bool silent = false) {
            try {
                string exePath = GetExecutablePath();
                string cmd = "\"" + exePath + "\" --daemon" + (silent ? " --silent" : "");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true)) {
                    if (key != null) {
                        key.SetValue(APP_NAME, cmd, RegistryValueKind.String);
                        return true;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("设置自启动失败: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// 关闭开机自启动
        /// </summary>
        public static bool UninstallAutoStart() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true)) {
                    if (key != null) {
                        key.DeleteValue(APP_NAME, false);
                        return true;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("移除自启动失败: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// 检查是否已设置开机自启动
        /// </summary>
        public static bool IsAutoStartInstalled() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY)) {
                    if (key == null) return false;
                    string val = key.GetValue(APP_NAME) as string;
                    return !string.IsNullOrEmpty(val);
                }
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 检查是否为纯静默开机自启动（无托盘）
        /// </summary>
        public static bool IsAutoStartSilent() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY)) {
                    if (key == null) return false;
                    string val = key.GetValue(APP_NAME) as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    return val.IndexOf("--silent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           val.IndexOf("-s", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            } catch {
                return false;
            }
        }
    }
}
