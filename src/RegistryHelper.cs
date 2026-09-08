using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ExplorerTabRouter {
    public static class RegistryHelper {
        private const string FOLDER_OPEN_CMD_KEY = @"Software\Classes\Folder\shell\open\command";
        private const string DIRECTORY_OPEN_CMD_KEY = @"Software\Classes\Directory\shell\open\command";
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "ExplorerTabRouter";

        public static string GetExecutablePath() {
            return Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        }

        /// <summary>
        /// 注册文件夹打开关联（仅在当前用户 HKCU 下修改，免管理员权限，安全可逆）
        /// </summary>
        public static bool InstallFolderAssociation() {
            try {
                string exePath = GetExecutablePath();
                string command = "\"" + exePath + "\" \"%1\"";

                // 1. Folder\shell\open\command
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(FOLDER_OPEN_CMD_KEY)) {
                    if (key != null) {
                        key.SetValue("", command, RegistryValueKind.String);
                        key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                    }
                }

                // 2. Directory\shell\open\command
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(DIRECTORY_OPEN_CMD_KEY)) {
                    if (key != null) {
                        key.SetValue("", command, RegistryValueKind.String);
                        key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                    }
                }

                return true;
            } catch (Exception ex) {
                Console.WriteLine("安装文件夹关联失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 移除文件夹关联，完全还原系统默认行为
        /// </summary>
        public static bool UninstallFolderAssociation() {
            try {
                // 删除 HKCU 下的自定义 key
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Folder\shell\open", false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\open", false);
                return true;
            } catch (Exception ex) {
                Console.WriteLine("卸载文件夹关联失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 检查是否已安装文件夹关联
        /// </summary>
        public static bool IsFolderAssociationInstalled() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(FOLDER_OPEN_CMD_KEY)) {
                    if (key == null) return false;
                    string val = key.GetValue("") as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    return val.IndexOf("ExplorerTabRouter", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 启用开机自启动
        /// </summary>
        public static bool InstallAutoStart() {
            try {
                string exePath = GetExecutablePath();
                string cmd = "\"" + exePath + "\" --daemon";
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
    }
}
