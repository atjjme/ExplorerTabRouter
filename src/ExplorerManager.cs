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

        /// <summary>
        /// 将路径路由至指定的已知宿主 Explorer 窗口
        /// </summary>
        public static bool RouteOpenIntoCabinet(IntPtr targetCabinet, string targetPath, dynamic sharedShell = null) {
            using (Win32.AttachToInteractiveDesktop()) {
                string resolvedPath = NormalizePath(targetPath);
                dynamic shell = sharedShell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));

                if (!string.IsNullOrEmpty(resolvedPath)) {
                    if (TrySwitchToExistingTab(targetCabinet, resolvedPath, shell)) {
                        Win32.ForceForegroundWindow(targetCabinet);
                        return true;
                    }
                }

                return OpenNewTabAndNavigate(targetCabinet, resolvedPath, shell);
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

        private static bool OpenNewTabAndNavigate(IntPtr cabinet, string targetPath, dynamic shell = null) {
            try {
                dynamic currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = currentShell.Windows();
                int initialCount = windows.Count;

                // 2. 异步快速发送新建标签命令 (0xA21B / Ctrl+T)，不阻塞当前线程
                IntPtr tabWindow = Win32.FindWindowEx(cabinet, IntPtr.Zero, "ShellTabWindowClass", null);
                if (tabWindow != IntPtr.Zero) {
                    Win32.PostMessage(tabWindow, Win32.WM_COMMAND, (IntPtr)Win32.CMD_NEW_TAB, IntPtr.Zero);
                }
                Win32.PostMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_NEW_TAB, IntPtr.Zero);

                // 立即将宿主窗口置前并激活，给予用户即时反馈
                Win32.ForceForegroundWindow(cabinet);

                // 3. 如果需要导航到具体路径，极速检测新 Tab 并导航 (10ms 间隔)
                if (!string.IsNullOrEmpty(targetPath)) {
                    NavigateNewTab(cabinet, targetPath, initialCount, currentShell);
                }

                return true;
            } catch (Exception ex) {
                Trace.WriteLine("OpenNewTabAndNavigate error: " + ex.Message);
                return false;
            }
        }

        private static bool NavigateNewTab(IntPtr cabinet, string targetPath, int initialCount, dynamic shell) {
            for (int retry = 0; retry < 50; retry++) {
                if (retry > 0) Thread.Sleep(10);
                try {
                    dynamic windows = shell.Windows();
                    int curCount = windows.Count;

                    if (curCount > initialCount) {
                        for (int i = curCount - 1; i >= initialCount; i--) {
                            try {
                                dynamic w = windows.Item(i);
                                if (w != null) {
                                    object hwndObj = w.HWND;
                                    if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                                        w.Navigate2(targetPath);
                                        return true;
                                    }
                                }
                            } catch {
                            }
                        }
                    }
                } catch {
                }
            }
            return false;
        }

        private static bool TrySwitchToExistingTab(IntPtr cabinet, string targetPath, dynamic shell = null) {
            try {
                dynamic currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = currentShell.Windows();
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
                                Win32.PostMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_SWITCH_TAB, (IntPtr)(tabIndex + 1));
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

            if (string.Equals(targetPath.Trim(), url.Trim(), StringComparison.OrdinalIgnoreCase)) {
                return true;
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

            if (s.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) {
                return s;
            }

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
