using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

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
                if (string.IsNullOrEmpty(resolvedPath)) {
                    // 保护：如果目标路径为空，绝不在宿主窗口凭空打开 This PC 标签页
                    return false;
                }

                dynamic shell = sharedShell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));

                if (TrySwitchToExistingTab(targetCabinet, resolvedPath, shell)) {
                    Win32.ForceForegroundWindow(targetCabinet);
                    return true;
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

                // 2. 异步快速发送新建标签命令 (0xA21B / Ctrl+T)，仅发送一次
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
                                        try {
                                            w.Navigate2(targetPath);
                                            return true;
                                        } catch {
                                            try {
                                                dynamic folder = shell.NameSpace(targetPath);
                                                if (folder != null) {
                                                    w.Navigate2(folder.Self);
                                                    return true;
                                                }
                                            } catch {}
                                        }
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
                                string locName = null;
                                try { locName = (string)w.LocationName; } catch {}

                                // 通过 UI Automation 真正选中并激活该 Tab 页面
                                SwitchTabViaUIAutomation(cabinet, tabIndex, targetPath, locName);
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

        /// <summary>
        /// 通过 UI Automation 查找并激活目标 Tab 标签
        /// </summary>
        public static bool SwitchTabViaUIAutomation(IntPtr cabinet, int tabIndex, string targetPath, string locationName = null) {
            try {
                AutomationElement cabinetEl = AutomationElement.FromHandle(cabinet);
                if (cabinetEl == null) return false;

                Condition tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                AutomationElementCollection tabItems = cabinetEl.FindAll(TreeScope.Descendants, tabCondition);
                if (tabItems == null || tabItems.Count == 0) return false;

                AutomationElement targetTab = null;

                // 1. 优先通过 Shell 对象的 LocationName 精准比对 TabItem.Current.Name
                if (!string.IsNullOrEmpty(locationName)) {
                    for (int i = 0; i < tabItems.Count; i++) {
                        try {
                            string name = tabItems[i].Current.Name;
                            if (!string.IsNullOrEmpty(name) &&
                                (name.Equals(locationName, StringComparison.OrdinalIgnoreCase) ||
                                 name.IndexOf(locationName, StringComparison.OrdinalIgnoreCase) >= 0)) {
                                targetTab = tabItems[i];
                                break;
                            }
                        } catch {}
                    }
                }

                // 2. 其次通过路径的文件夹名称比对
                if (targetTab == null && !string.IsNullOrEmpty(targetPath)) {
                    string folderName = string.Empty;
                    try {
                        string norm = NormalizePath(targetPath).TrimEnd('\\', '/');
                        folderName = Path.GetFileName(norm);
                        if (string.IsNullOrEmpty(folderName)) folderName = norm;
                    } catch {}

                    if (!string.IsNullOrEmpty(folderName)) {
                        for (int i = 0; i < tabItems.Count; i++) {
                            try {
                                string name = tabItems[i].Current.Name;
                                if (!string.IsNullOrEmpty(name) &&
                                    (name.Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
                                     name.IndexOf(folderName, StringComparison.OrdinalIgnoreCase) >= 0)) {
                                    targetTab = tabItems[i];
                                    break;
                                }
                            } catch {}
                        }
                    }
                }

                // 3. 兜底使用在 Cabinet 中的相对序号索引
                if (targetTab == null && tabIndex >= 0 && tabIndex < tabItems.Count) {
                    targetTab = tabItems[tabIndex];
                }

                if (targetTab != null) {
                    object patternObj;
                    if (targetTab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out patternObj)) {
                        ((SelectionItemPattern)patternObj).Select();
                        return true;
                    }
                    if (targetTab.TryGetCurrentPattern(InvokePattern.Pattern, out patternObj)) {
                        ((InvokePattern)patternObj).Invoke();
                        return true;
                    }
                }
            } catch (Exception ex) {
                Trace.WriteLine("SwitchTabViaUIAutomation error: " + ex.Message);
            }
            return false;
        }

        private static bool IsPathMatch(string targetPath, string url) {
            if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(url)) return false;

            // 针对回收站等特殊虚拟文件夹的去重匹配
            if ((targetPath.IndexOf("RecycleBinFolder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 targetPath.IndexOf("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase) >= 0) &&
                (url.IndexOf("RecycleBinFolder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 url.IndexOf("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase) >= 0)) {
                return true;
            }

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

            // 特殊系统 CLSID 规范化为 Shell URL Moniker（确保 COM Navigate2 能成功识别导航，防止 E_INVALIDARG）
            if (s.IndexOf("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase) >= 0) {
                return "shell:RecycleBinFolder";
            }
            if (s.IndexOf("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase) >= 0) {
                return "shell:MyComputerFolder";
            }
            if (s.IndexOf("{26EE0668-A00A-44D7-9371-BEB064C98683}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", StringComparison.OrdinalIgnoreCase) >= 0) {
                return "shell:ControlPanelFolder";
            }

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
