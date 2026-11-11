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
        public static bool IsRecycleBinPath(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("RecycleBinFolder", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 核心路由方法：在已有 Explorer 窗口的新 Tab 中打开目标路径，若无窗口则新开
        /// </summary>
        public static bool RouteOpen(string targetPath, bool forceNewWindow = false) {
            using (Win32.AttachToInteractiveDesktop()) {
                List<string> selectedItems = null;
                string raw = (targetPath ?? string.Empty).Trim();

                // 支持命令行 /select,"C:\path\file.ext" 参数解析
                if (raw.StartsWith("/select,", StringComparison.OrdinalIgnoreCase)) {
                    raw = raw.Substring("/select,".Length).Trim();
                    string selPath = NormalizePath(raw);
                    if (!string.IsNullOrEmpty(selPath) && (File.Exists(selPath) || Directory.Exists(selPath))) {
                        selectedItems = new List<string> { selPath };
                        targetPath = Path.GetDirectoryName(selPath) ?? selPath;
                    }
                }

                string resolvedPath = NormalizePath(targetPath);

                // 若传入的是具体文件路径，自动转换为打开其所在父目录并高亮选中该文件
                if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath) && !Directory.Exists(resolvedPath)) {
                    string parentDir = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir)) {
                        selectedItems = new List<string> { resolvedPath };
                        resolvedPath = parentDir;
                    }
                }

                // 1. 如果用户按住了 Ctrl + Shift，或者显式要求新窗口，直接启动独立窗口
                if (forceNewWindow || Win32.IsCtrlShiftPressed()) {
                    LaunchNewExplorerWindow(resolvedPath, selectedItems);
                    return true;
                }

                // 2. 获取当前桌面上所有存活的 CabinetWClass 窗口并选择最佳宿主窗口
                List<IntPtr> cabinets = Win32.GetAllCabinetWindows();
                IntPtr targetCabinet = SelectBestCabinet(cabinets);
                if (targetCabinet == IntPtr.Zero) {
                    LaunchNewExplorerWindow(resolvedPath, selectedItems);
                    return true;
                }

                // 3. 检查是否已经有 Tab 打开了这个路径（智能去重）
                if (!string.IsNullOrEmpty(resolvedPath)) {
                    if (TrySwitchToExistingTab(targetCabinet, resolvedPath, null, selectedItems)) {
                        Win32.ForceForegroundWindow(targetCabinet);
                        return true;
                    }
                }

                // 4. 在该窗口中新建 Tab 并导航
                return OpenNewTabAndNavigate(targetCabinet, resolvedPath, null, selectedItems);
            }
        }

        /// <summary>
        /// 将路径路由至指定的已知宿主 Explorer 窗口
        /// </summary>
        public static bool RouteOpenIntoCabinet(IntPtr targetCabinet, string targetPath, dynamic sharedShell = null, IList<string> selectedItems = null) {
            using (Win32.AttachToInteractiveDesktop()) {
                string resolvedPath = NormalizePath(targetPath);
                if (string.IsNullOrEmpty(resolvedPath)) {
                    // 保护：如果目标路径为空，绝不在宿主窗口凭空打开 This PC 标签页
                    return false;
                }

                bool ownShell = (sharedShell == null);
                dynamic shell = sharedShell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                try {
                    if (TrySwitchToExistingTab(targetCabinet, resolvedPath, shell, selectedItems)) {
                        Win32.ForceForegroundWindow(targetCabinet);
                        return true;
                    }

                    return OpenNewTabAndNavigate(targetCabinet, resolvedPath, shell, selectedItems);
                } finally {
                    if (ownShell && shell != null && Marshal.IsComObject(shell)) {
                        try { Marshal.FinalReleaseComObject(shell); } catch {}
                    }
                }
            }
        }

        public static void LaunchNewExplorerWindow(string path, IList<string> selectedItems = null) {
            try {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                if (selectedItems != null && selectedItems.Count > 0 && !string.IsNullOrEmpty(selectedItems[0])) {
                    psi.Arguments = "/select,\"" + selectedItems[0] + "\"";
                } else if (!string.IsNullOrEmpty(path)) {
                    psi.Arguments = "\"" + path + "\"";
                }
                psi.UseShellExecute = true;
                Process.Start(psi);
            } catch (Exception ex) {
                Trace.WriteLine("LaunchNewExplorerWindow error: " + ex.Message);
            }
        }

        private static int CountCabinetBrowsers(dynamic shell, IntPtr cabinet) {
            int count = 0;
            try {
                dynamic windows = shell.Windows();
                if (windows == null) return 0;
                int total = windows.Count;
                for (int i = 0; i < total; i++) {
                    try {
                        dynamic w = windows.Item(i);
                        if (w != null && Convert.ToInt64(w.HWND) == (long)cabinet) {
                            count++;
                        }
                    } catch {}
                }
            } catch {}
            return count;
        }

        private static bool OpenNewTabAndNavigate(IntPtr cabinet, string targetPath, dynamic shell = null, IList<string> selectedItems = null) {
            bool ownShell = (shell == null);
            dynamic currentShell = shell;
            try {
                currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));

                // 1. 仅统计目标宿主窗口自身的当前 Tab 数，避免被关闭拦截窗口注销时影响全局 windows.Count
                int initialCabinetTabCount = CountCabinetBrowsers(currentShell, cabinet);

                // 2. 异步快速发送新建标签命令 (0xA21B / Ctrl+T)，仅发送一次
                Win32.PostMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_NEW_TAB, IntPtr.Zero);

                // 立即将宿主窗口置前并激活，给予用户即时反馈
                Win32.ForceForegroundWindow(cabinet);

                // 3. 如果需要导航到具体路径，极速检测该宿主窗口新增的 Tab 并导航 (10ms 间隔)
                if (!string.IsNullOrEmpty(targetPath)) {
                    NavigateNewTab(cabinet, targetPath, initialCabinetTabCount, currentShell, selectedItems);
                }

                return true;
            } catch (Exception ex) {
                Trace.WriteLine("OpenNewTabAndNavigate error: " + ex.Message);
                return false;
            } finally {
                if (ownShell && currentShell != null && Marshal.IsComObject(currentShell)) {
                    try { Marshal.FinalReleaseComObject(currentShell); } catch {}
                }
            }
        }

        private static bool NavigateNewTab(IntPtr cabinet, string targetPath, int initialCabinetTabCount, dynamic shell, IList<string> selectedItems = null) {
            for (int retry = 0; retry < 60; retry++) {
                if (retry > 0) Thread.Sleep(10);
                try {
                    dynamic windows = shell.Windows();
                    if (windows == null) continue;
                    int curCount = windows.Count;

                    int cabTabCount = 0;
                    dynamic lastCabBrowser = null;

                    for (int i = 0; i < curCount; i++) {
                        try {
                            dynamic w = windows.Item(i);
                            if (w != null) {
                                object hwndObj = w.HWND;
                                if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                                    cabTabCount++;
                                    lastCabBrowser = w;
                                }
                            }
                        } catch {}
                    }

                    // 当目标宿主窗口的 Tab 数量增加时，最后一个属于该宿主窗口的 Browser 即为新建的 Tab
                    if (cabTabCount > initialCabinetTabCount && lastCabBrowser != null) {
                        bool navigated = false;
                        if (IsRecycleBinPath(targetPath)) {
                            try {
                                lastCabBrowser.Navigate2(10);
                                navigated = true;
                            } catch {}
                        }
                        if (!navigated) {
                            try {
                                lastCabBrowser.Navigate2(targetPath);
                                navigated = true;
                            } catch {
                                try {
                                    dynamic folder = shell.NameSpace(targetPath);
                                    if (folder != null) {
                                        lastCabBrowser.Navigate2(folder.Self);
                                        navigated = true;
                                    }
                                } catch {}
                            }
                        }

                        if (navigated) {
                            if (selectedItems != null && selectedItems.Count > 0) {
                                WaitAndSelectItemsInBrowser(lastCabBrowser, targetPath, selectedItems);
                            }
                            return true;
                        }
                    }
                } catch {
                }
            }
            return false;
        }

        private static void WaitAndSelectItemsInBrowser(dynamic browser, string targetPath, IList<string> selectedItems) {
            if (browser == null || selectedItems == null || selectedItems.Count == 0) return;
            for (int i = 0; i < 40; i++) {
                Thread.Sleep(15);
                try {
                    string curPath = DaemonWatcher.ExtractPathFromBrowser(browser);
                    if (IsPathMatch(targetPath, curPath)) {
                        if (SelectItemsInBrowser(browser, selectedItems)) {
                            return;
                        }
                    }
                } catch {}
            }
        }

        public static bool SelectItemsInBrowser(dynamic browser, IList<string> selectedItems) {
            if (browser == null || selectedItems == null || selectedItems.Count == 0) return false;
            try {
                dynamic doc = browser.Document;
                if (doc == null) return false;
                dynamic folder = doc.Folder;
                if (folder == null) return false;

                bool anySelected = false;
                for (int i = 0; i < selectedItems.Count; i++) {
                    string fullPath = selectedItems[i];
                    if (string.IsNullOrEmpty(fullPath)) continue;
                    string itemName = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(itemName)) continue;

                    dynamic folderItem = folder.ParseName(itemName);
                    if (folderItem != null) {
                        // SVSI_SELECT(1) | SVSI_DESELECTOTHERS(4) | SVSI_ENSUREVISIBLE(8) | SVSI_FOCUSED(16) = 29
                        int flags = (i == 0) ? 29 : 9;
                        doc.SelectItem(folderItem, flags);
                        anySelected = true;
                    }
                }
                return anySelected;
            } catch {
                return false;
            }
        }

        private static bool TrySwitchToExistingTab(IntPtr cabinet, string targetPath, dynamic shell = null, IList<string> selectedItems = null) {
            bool ownShell = (shell == null);
            dynamic currentShell = shell;
            try {
                currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
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
                            string tabPath = DaemonWatcher.ExtractPathFromBrowser(w);
                            if (string.IsNullOrEmpty(tabPath)) {
                                try { tabPath = (string)w.LocationURL; } catch {}
                            }

                            if (IsPathMatch(targetPath, tabPath)) {
                                string locName = null;
                                try { locName = (string)w.LocationName; } catch {}

                                // 若宿主窗口处于最小化状态，先恢复窗口以确保 UI Automation 能正常访问并切换 TabItem
                                if (Win32.IsIconic(cabinet)) {
                                    Win32.ShowWindow(cabinet, Win32.SW_RESTORE);
                                }

                                // 通过 UI Automation 真正选中并激活该 Tab 页面
                                bool switched = SwitchTabViaUIAutomation(cabinet, tabIndex, targetPath, locName);
                                if (selectedItems != null && selectedItems.Count > 0) {
                                    SelectItemsInBrowser(w, selectedItems);
                                }
                                return switched;
                            }
                            tabIndex++;
                        }
                    } catch {
                    }
                }
            } catch {
            } finally {
                if (ownShell && currentShell != null && Marshal.IsComObject(currentShell)) {
                    try { Marshal.FinalReleaseComObject(currentShell); } catch {}
                }
            }
            return false;
        }

        /// <summary>
        /// 通过 UI Automation 查找并激活目标 Tab 标签（严格区分全等匹配与包含匹配，避免前缀子串误判）
        /// </summary>
        public static bool SwitchTabViaUIAutomation(IntPtr cabinet, int tabIndex, string targetPath, string locationName = null) {
            try {
                AutomationElement cabinetEl = AutomationElement.FromHandle(cabinet);
                if (cabinetEl == null) return false;

                Condition tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                AutomationElementCollection tabItems = cabinetEl.FindAll(TreeScope.Descendants, tabCondition);
                if (tabItems == null || tabItems.Count == 0) return false;

                AutomationElement targetTab = null;

                string folderName = string.Empty;
                if (!string.IsNullOrEmpty(targetPath)) {
                    try {
                        string norm = NormalizePath(targetPath).TrimEnd('\\', '/');
                        folderName = Path.GetFileName(norm);
                        if (string.IsNullOrEmpty(folderName)) folderName = norm;
                    } catch {}
                }

                // 0. 若 tabIndex 处的 Tab 名称刚好等于 locationName 或 folderName，优先精准命中该索引（解决同名不同路径 Tab 冲突）
                if (tabIndex >= 0 && tabIndex < tabItems.Count) {
                    try {
                        string idxName = tabItems[tabIndex].Current.Name;
                        if (!string.IsNullOrEmpty(idxName)) {
                            if ((!string.IsNullOrEmpty(locationName) && idxName.Equals(locationName, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(folderName) && idxName.Equals(folderName, StringComparison.OrdinalIgnoreCase))) {
                                targetTab = tabItems[tabIndex];
                            }
                        }
                    } catch {}
                }

                // 1. 优先通过 LocationName 全等匹配 (Exact Match)
                if (targetTab == null && !string.IsNullOrEmpty(locationName)) {
                    for (int i = 0; i < tabItems.Count; i++) {
                        try {
                            string name = tabItems[i].Current.Name;
                            if (!string.IsNullOrEmpty(name) && name.Equals(locationName, StringComparison.OrdinalIgnoreCase)) {
                                targetTab = tabItems[i];
                                break;
                            }
                        } catch {}
                    }
                }

                // 1.5 若是回收站，额外匹配 "回收站" 或 "Recycle Bin"
                if (targetTab == null && IsRecycleBinPath(targetPath)) {
                    for (int i = 0; i < tabItems.Count; i++) {
                        try {
                            string name = tabItems[i].Current.Name;
                            if (!string.IsNullOrEmpty(name) &&
                                (name.Equals("回收站", StringComparison.OrdinalIgnoreCase) ||
                                 name.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) ||
                                 name.IndexOf("回收站", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 name.IndexOf("Recycle Bin", StringComparison.OrdinalIgnoreCase) >= 0)) {
                                targetTab = tabItems[i];
                                break;
                            }
                        } catch {}
                    }
                }

                // 2. 通过文件夹名称全等匹配 (Exact Match)
                if (targetTab == null && !string.IsNullOrEmpty(folderName)) {
                    for (int i = 0; i < tabItems.Count; i++) {
                        try {
                            string name = tabItems[i].Current.Name;
                            if (!string.IsNullOrEmpty(name) && name.Equals(folderName, StringComparison.OrdinalIgnoreCase)) {
                                targetTab = tabItems[i];
                                break;
                            }
                        } catch {}
                    }
                }

                // 3. 回退：LocationName 或 folderName 包含匹配 (Substring Match)
                if (targetTab == null) {
                    string matchKey = !string.IsNullOrEmpty(locationName) ? locationName : folderName;
                    if (!string.IsNullOrEmpty(matchKey)) {
                        for (int i = 0; i < tabItems.Count; i++) {
                            try {
                                string name = tabItems[i].Current.Name;
                                if (!string.IsNullOrEmpty(name) && name.IndexOf(matchKey, StringComparison.OrdinalIgnoreCase) >= 0) {
                                    targetTab = tabItems[i];
                                    break;
                                }
                            } catch {}
                        }
                    }
                }

                // 4. 兜底使用在 Cabinet 中的相对序号索引
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
            if (IsRecycleBinPath(targetPath) && IsRecycleBinPath(url)) {
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

        /// <summary>
        /// 选择最佳宿主 Explorer 窗口（严格过滤被拦截/隐形窗口，优先选前台窗口 > Z-Order 最前的非最小化窗口 > 最小化窗口）
        /// </summary>
        public static IntPtr SelectBestCabinet(List<IntPtr> cabinets, IntPtr excludeHwnd = default(IntPtr), HashSet<IntPtr> pendingInterception = null) {
            if (cabinets == null || cabinets.Count == 0) return IntPtr.Zero;

            List<IntPtr> valid = new List<IntPtr>(cabinets.Count);
            foreach (IntPtr c in cabinets) {
                if (c == IntPtr.Zero || c == excludeHwnd) continue;
                if (pendingInterception != null && pendingInterception.Contains(c)) continue;
                if (!Win32.IsWindow(c) || !Win32.IsWindowVisible(c)) continue;
                if (Win32.IsWindowInterceptedHidden(c)) continue;
                valid.Add(c);
            }

            if (valid.Count == 0) return IntPtr.Zero;
            if (valid.Count == 1) return valid[0];

            // 1. 优先选择当前焦点 Explorer 窗口（若未最小化）
            IntPtr fore = Win32.GetForegroundWindow();
            if (fore != IntPtr.Zero && valid.Contains(fore) && !Win32.IsIconic(fore)) {
                return fore;
            }

            // 2. 选择 Z-Order 最顶层的非最小化窗口（FindWindowEx 默认按 Z-Order 从前到后返回）
            foreach (IntPtr c in valid) {
                if (!Win32.IsIconic(c)) {
                    return c;
                }
            }

            // 3. 若全部处于最小化状态，选择 Z-Order 最前的一个
            return valid[0];
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
