using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ExplorerTabRouter {
    public static class ExplorerManager {
        /// <summary>
        /// 核心路由方法：在已有 Explorer 窗口的新 Tab 中打开目标路径，若无窗口则新开
        /// </summary>
        public static bool RouteOpen(string targetPath, bool forceNewWindow = false) {
            using (Win32.AttachToInteractiveDesktop()) {
                string resolvedPath = NormalizePath(targetPath);

                // 1. 如果用户按住了 Ctrl + Shift，或者显式要求新窗口，直接启动独立窗口
                if (forceNewWindow || Win32.IsCtrlShiftPressed()) {
                    LaunchNewExplorerWindow(resolvedPath);
                    return true;
                }

                // 2. 获取当前桌面上所有存活的 CabinetWClass 窗口
                List<IntPtr> cabinets = Win32.GetAllCabinetWindows();
                if (cabinets.Count == 0) {
                    LaunchNewExplorerWindow(resolvedPath);
                    return true;
                }

                // 3. 选择最佳宿主窗口（优先选当前焦点 Explorer 窗口，否则选最顶层的）
                IntPtr targetCabinet = SelectBestCabinet(cabinets);
                if (targetCabinet == IntPtr.Zero) {
                    LaunchNewExplorerWindow(resolvedPath);
                    return true;
                }

                // 4. 检查是否已经有 Tab 打开了这个路径（智能去重）
                if (!string.IsNullOrEmpty(resolvedPath)) {
                    if (TrySwitchToExistingTab(targetCabinet, resolvedPath)) {
                        Win32.ForceForegroundWindow(targetCabinet);
                        return true;
                    }
                }

                // 5. 在该窗口中新建 Tab 并导航
                return OpenNewTabAndNavigate(targetCabinet, resolvedPath);
            }
        }

        public static void LaunchNewExplorerWindow(string path) {
            try {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                if (!string.IsNullOrEmpty(path)) {
                    psi.Arguments = "\"" + path + "\"";
                }
                psi.UseShellExecute = true;
                Process.Start(psi);
            } catch (Exception ex) {
                Trace.WriteLine("LaunchNewExplorerWindow error: " + ex.Message);
            }
        }

        private static bool OpenNewTabAndNavigate(IntPtr cabinet, string targetPath) {
            try {
                // 1. 记录新建前的 COM Windows 总数
                int initialCount = 0;
                try {
                    dynamic initialShell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    initialCount = initialShell.Windows().Count;
                } catch {}

                // 2. 发送新建标签命令 (0xA21B / Ctrl+T)
                IntPtr tabWindow = Win32.FindWindowEx(cabinet, IntPtr.Zero, "ShellTabWindowClass", null);
                if (tabWindow != IntPtr.Zero) {
                    Win32.SendMessage(tabWindow, Win32.WM_COMMAND, (IntPtr)Win32.CMD_NEW_TAB, IntPtr.Zero);
                } else {
                    Win32.SendMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_NEW_TAB, IntPtr.Zero);
                }

                // 3. 如果需要导航到具体路径，等待新 Tab 的 COM 对象就绪并导航
                if (!string.IsNullOrEmpty(targetPath)) {
                    NavigateNewTab(cabinet, targetPath, initialCount);
                }

                // 4. 激活窗口并置前
                Win32.ForceForegroundWindow(cabinet);
                return true;
            } catch (Exception ex) {
                Trace.WriteLine("OpenNewTabAndNavigate error: " + ex.Message);
                return false;
            }
        }

        private static bool NavigateNewTab(IntPtr cabinet, string targetPath, int initialCount) {
            for (int retry = 0; retry < 35; retry++) {
                Thread.Sleep(80);
                try {
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    dynamic windows = shell.Windows();
                    int curCount = windows.Count;

                    if (curCount > initialCount) {
                        for (int i = initialCount; i < curCount; i++) {
                            try {
                                dynamic w = windows.Item(i);
                                if (w != null) {
                                    object hwndObj = w.HWND;
                                    if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                                        w.Navigate2(targetPath);
                                        Thread.Sleep(250);
                                        return true;
                                    }
                                }
                            } catch {
                                // 窗口可能正在初始化，继续等待
                            }
                        }
                    }
                } catch {
                    // COM 重试
                }
            }
            return false;
        }

        private static bool TrySwitchToExistingTab(IntPtr cabinet, string targetPath) {
            try {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = shell.Windows();
                int count = windows.Count;

                int tabIndex = 0;
                for (int i = 0; i < count; i++) {
                    try {
                        dynamic w = windows.Item(i);
                        if (w == null) continue;

                        object hwndObj = w.HWND;
                        if (hwndObj == null) continue;
                        long hwnd = Convert.ToInt64(hwndObj);

                        if (hwnd == (long)cabinet) {
                            string url = (string)w.LocationURL;
                            if (IsPathMatch(targetPath, url)) {
                                Win32.SendMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_SWITCH_TAB, (IntPtr)(tabIndex + 1));
                                return true;
                            }
                            tabIndex++;
                        }
                    } catch {
                    }
                }
            } catch {
            }
            return false;
        }

        private static bool IsPathMatch(string targetPath, string url) {
            if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(url)) return false;

            string localFromUrl = UrlToLocalPath(url);
            if (!string.IsNullOrEmpty(localFromUrl)) {
                string normTarget = NormalizePath(targetPath).TrimEnd('\\');
                string normLocal = NormalizePath(localFromUrl).TrimEnd('\\');
                if (string.Equals(normTarget, normLocal, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static string UrlToLocalPath(string url) {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            try {
                if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) {
                    Uri uri = new Uri(url);
                    return uri.LocalPath;
                }
            } catch {}
            return url;
        }

        private static IntPtr SelectBestCabinet(List<IntPtr> cabinets) {
            if (cabinets.Count == 1) return cabinets[0];

            IntPtr fore = Win32.GetForegroundWindow();
            if (cabinets.Contains(fore)) {
                return fore;
            }

            IntPtr best = cabinets[0];
            int maxTabs = -1;
            foreach (IntPtr c in cabinets) {
                if (Win32.IsWindowVisible(c)) {
                    int tabCount = Win32.GetTabsForCabinet(c).Count;
                    if (tabCount > maxTabs) {
                        maxTabs = tabCount;
                        best = c;
                    }
                }
            }
            return best;
        }

        public static string NormalizePath(string raw) {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            string s = raw.Trim('\"', '\'', ' ');
            if (string.IsNullOrEmpty(s)) return string.Empty;

            try {
                s = Environment.ExpandEnvironmentVariables(s);
                if (Directory.Exists(s) || File.Exists(s)) {
                    return Path.GetFullPath(s);
                }
            } catch {}

            return s;
        }
    }
}
