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
        /// 自动清理历史遗留的免常驻注册表文件夹关联，确保系统 100% 恢复原生纯净，且不误伤第三方扩展
        /// </summary>
        public static void CleanupLegacyFolderAssociation() {
            CleanupClassShellOpen(@"Software\Classes\Folder");
            CleanupClassShellOpen(@"Software\Classes\Directory");
        }

        private static void CleanupClassShellOpen(string classKeyPath) {
            try {
                string openCmdPath = classKeyPath + @"\shell\open\command";
                bool shouldRemoveOpen = false;
                using (RegistryKey cmdKey = Registry.CurrentUser.OpenSubKey(openCmdPath, false)) {
                    if (cmdKey != null) {
                        string defaultVal = cmdKey.GetValue(null) as string;
                        if (string.IsNullOrEmpty(defaultVal) ||
                            defaultVal.IndexOf("ExplorerTabRouter", StringComparison.OrdinalIgnoreCase) >= 0) {
                            shouldRemoveOpen = true;
                        }
                    }
                }

                if (shouldRemoveOpen) {
                    Registry.CurrentUser.DeleteSubKeyTree(classKeyPath + @"\shell\open", false);
                }

                // 若 shell 下已无任何子项与键值，则清理空的 shell 项
                using (RegistryKey shellKey = Registry.CurrentUser.OpenSubKey(classKeyPath + @"\shell", false)) {
                    if (shellKey != null && shellKey.SubKeyCount == 0 && shellKey.ValueCount == 0) {
                        Registry.CurrentUser.DeleteSubKeyTree(classKeyPath + @"\shell", false);
                    }
                }

                // 若 classKeyPath 下已无任何子项与键值，则清理空项
                using (RegistryKey classKey = Registry.CurrentUser.OpenSubKey(classKeyPath, false)) {
                    if (classKey != null && classKey.SubKeyCount == 0 && classKey.ValueCount == 0) {
                        Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, false);
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
