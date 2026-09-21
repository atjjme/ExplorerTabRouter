using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ExplorerTabRouter {
    public static class TabTracker {
        private const int MAX_HISTORY = 50;
        private static readonly Stack<string> _recentlyClosed = new Stack<string>();
        private static readonly object _lock = new object();

        // 记录每个 Cabinet 窗口上次检测到的全部标签路径，用于差量比对检测关闭
        private static readonly Dictionary<IntPtr, List<string>> _cabinetTabsSnapshot = new Dictionary<IntPtr, List<string>>();
        private static bool _snapshotInitialized = false;

        /// <summary>
        /// 记录已关闭标签页的路径入栈
        /// </summary>
        public static void PushClosed(string path) {
            if (string.IsNullOrEmpty(path)) return;
            string norm = ExplorerManager.NormalizePath(path);
            if (string.IsNullOrEmpty(norm)) {
                norm = path.Trim('\"', ' ');
            }
            if (string.IsNullOrEmpty(norm)) return;

            lock (_lock) {
                // 如果栈顶已经是该路径，不重复压入
                if (_recentlyClosed.Count > 0 && string.Equals(_recentlyClosed.Peek(), norm, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }

                _recentlyClosed.Push(norm);

                // 保持最大容量限制
                if (_recentlyClosed.Count > MAX_HISTORY) {
                    string[] items = _recentlyClosed.ToArray();
                    _recentlyClosed.Clear();
                    for (int i = MAX_HISTORY - 1; i >= 0; i--) {
                        _recentlyClosed.Push(items[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 取出最近关闭的标签路径出栈
        /// </summary>
        public static string PopClosed() {
            lock (_lock) {
                while (_recentlyClosed.Count > 0) {
                    string path = _recentlyClosed.Pop();
                    if (IsValidPath(path)) {
                        return path;
                    }
                }
                return null;
            }
        }

        public static bool IsValidPath(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            if (Directory.Exists(path) || File.Exists(path)) return true;
            if (path.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return false;
        }

        public static int Count {
            get {
                lock (_lock) { return _recentlyClosed.Count; }
            }
        }

        /// <summary>
        /// 恢复刚刚关闭的标签页（按 Ctrl + Shift + T 或托盘菜单触发）
        /// </summary>
        public static bool RestoreLastClosedTab() {
            string targetPath = PopClosed();
            if (string.IsNullOrEmpty(targetPath)) return false;

            // 优先恢复到当前活跃的 Explorer 窗口，若无则恢复到最顶层已有窗口，都没有则新开窗口
            List<IntPtr> cabs = Win32.GetAllCabinetWindows();
            IntPtr host = IntPtr.Zero;
            IntPtr fore = Win32.GetForegroundWindow();
            if (cabs.Contains(fore)) {
                host = fore;
            } else if (cabs.Count > 0) {
                host = cabs[0];
            }

            if (host != IntPtr.Zero) {
                ExplorerManager.RouteOpenIntoCabinet(host, targetPath);
                Win32.ForceForegroundWindow(host);
            } else {
                ExplorerManager.LaunchNewExplorerWindow(targetPath);
            }
            return true;
        }

        /// <summary>
        /// 全局差量比对：快照对比当前所有打开的标签，若发现某个标签消失，自动将其压入已关闭栈
        /// </summary>
        public static void SyncOpenTabs(dynamic shell) {
            if (shell == null) return;
            try {
                // 1. 获取当前存活的所有 Cabinet 窗口
                List<IntPtr> liveCabs = Win32.GetAllCabinetWindows();
                HashSet<IntPtr> liveCabSet = new HashSet<IntPtr>(liveCabs);

                // 2. 收集每个 Cabinet 窗口当前的全部标签路径
                Dictionary<IntPtr, List<string>> currentSnapshot = new Dictionary<IntPtr, List<string>>();
                foreach (IntPtr cab in liveCabs) {
                    currentSnapshot[cab] = new List<string>();
                }

                dynamic windows = shell.Windows();
                int count = windows.Count;
                for (int i = 0; i < count; i++) {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    try {
                        object hwndObj = w.HWND;
                        if (hwndObj != null) {
                            IntPtr hwnd = (IntPtr)Convert.ToInt64(hwndObj);
                            if (currentSnapshot.ContainsKey(hwnd)) {
                                string p = ExtractPathFromBrowser(w);
                                if (!string.IsNullOrEmpty(p)) {
                                    currentSnapshot[hwnd].Add(p);
                                }
                            }
                        }
                    } catch {}
                }

                lock (_lock) {
                    if (!_snapshotInitialized) {
                        // 首次运行只建立初始快照，不产生关闭事件
                        _cabinetTabsSnapshot.Clear();
                        foreach (var kvp in currentSnapshot) {
                            _cabinetTabsSnapshot[kvp.Key] = new List<string>(kvp.Value);
                        }
                        _snapshotInitialized = true;
                        return;
                    }

                    // 3. 对比存活窗口内部标签变化
                    foreach (IntPtr cab in liveCabs) {
                        List<string> currPaths = currentSnapshot[cab];
                        if (_cabinetTabsSnapshot.ContainsKey(cab)) {
                            List<string> prevPaths = _cabinetTabsSnapshot[cab];

                            // 寻找在 prevPaths 中存在但在 currPaths 中减少的路径
                            // 构造待比对计数
                            Dictionary<string, int> currBag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            foreach (string p in currPaths) {
                                int c;
                                currBag.TryGetValue(p, out c);
                                currBag[p] = c + 1;
                            }

                            foreach (string p in prevPaths) {
                                int c;
                                if (currBag.TryGetValue(p, out c) && c > 0) {
                                    currBag[p] = c - 1;
                                } else {
                                    // 消失了一个该路径的 Tab！记录关闭
                                    PushClosed(p);
                                }
                            }
                        }
                        _cabinetTabsSnapshot[cab] = new List<string>(currPaths);
                    }

                    // 4. 清理已经彻底关闭的 Cabinet 窗口并记录其最后关闭的标签
                    List<IntPtr> closedCabs = new List<IntPtr>();
                    foreach (var k in _cabinetTabsSnapshot.Keys) {
                        if (!liveCabSet.Contains(k)) {
                            closedCabs.Add(k);
                        }
                    }
                    foreach (IntPtr k in closedCabs) {
                        List<string> oldTabs = _cabinetTabsSnapshot[k];
                        if (oldTabs != null && oldTabs.Count > 0) {
                            // 将最后一个关闭的标签也记录下来
                            PushClosed(oldTabs[oldTabs.Count - 1]);
                        }
                        _cabinetTabsSnapshot.Remove(k);
                    }
                }
            } catch (Exception ex) {
                Trace.WriteLine("SyncOpenTabs error: " + ex.Message);
            }
        }

        /// <summary>
        /// 从指定的 Cabinet 窗口中按 Tab 索引或名称解析其路径
        /// </summary>
        public static string GetPathFromTab(IntPtr cabinet, int tabIndex, string tabName, dynamic shell = null) {
            try {
                dynamic currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = currentShell.Windows();
                int count = windows.Count;

                int currentIndex = 0;
                string matchedByName = null;

                for (int i = 0; i < count; i++) {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    try {
                        object hwndObj = w.HWND;
                        if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                            string p = ExtractPathFromBrowser(w);
                            if (!string.IsNullOrEmpty(tabName) && !string.IsNullOrEmpty(p)) {
                                string locName = (string)w.LocationName;
                                if (string.Equals(locName, tabName, StringComparison.OrdinalIgnoreCase)) {
                                    return p;
                                }
                                try {
                                    dynamic doc = w.Document;
                                    if (doc != null && doc.Folder != null && string.Equals((string)doc.Folder.Title, tabName, StringComparison.OrdinalIgnoreCase)) {
                                        return p;
                                    }
                                } catch {}
                            }
                            if (currentIndex == tabIndex && !string.IsNullOrEmpty(p)) {
                                matchedByName = p;
                            }
                            currentIndex++;
                        }
                    } catch {}
                }

                if (!string.IsNullOrEmpty(matchedByName)) return matchedByName;
            } catch (Exception ex) {
                Trace.WriteLine("GetPathFromTab error: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 获取前台或指定 Explorer 窗口当前激活的 Tab 路径
        /// </summary>
        public static string GetActiveCabinetPath(IntPtr cabinet, dynamic shell = null) {
            try {
                // Windows 11 Explorer 窗口标题格式通常为:
                // "{当前激活标签名} and X more tabs - File Explorer" 或 "{当前激活标签名} - 资源管理器" 或 "{当前激活标签名}"
                string winTitle = Win32.GetWindowTitle(cabinet);
                string activeName = ExtractTabNameFromTitle(winTitle);

                dynamic currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = currentShell.Windows();
                int count = windows.Count;

                string firstCabinetPath = null;

                for (int i = 0; i < count; i++) {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    try {
                        object hwndObj = w.HWND;
                        if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                            string p = ExtractPathFromBrowser(w);
                            if (string.IsNullOrEmpty(p)) continue;

                            if (firstCabinetPath == null) firstCabinetPath = p;

                            if (!string.IsNullOrEmpty(activeName)) {
                                string locName = (string)w.LocationName;
                                if (string.Equals(locName, activeName, StringComparison.OrdinalIgnoreCase)) {
                                    return p;
                                }
                                try {
                                    dynamic doc = w.Document;
                                    if (doc != null && doc.Folder != null && string.Equals((string)doc.Folder.Title, activeName, StringComparison.OrdinalIgnoreCase)) {
                                        return p;
                                    }
                                } catch {}
                            }
                        }
                    } catch {}
                }

                return firstCabinetPath;
            } catch (Exception ex) {
                Trace.WriteLine("GetActiveCabinetPath error: " + ex.Message);
            }
            return null;
        }

        private static string ExtractTabNameFromTitle(string title) {
            if (string.IsNullOrEmpty(title)) return null;

            // 截除常见的系统后缀
            string s = title.Trim();
            int idx = s.IndexOf(" and ", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return s.Substring(0, idx).Trim();

            idx = s.LastIndexOf(" - 资源管理器", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return s.Substring(0, idx).Trim();

            idx = s.LastIndexOf(" - File Explorer", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return s.Substring(0, idx).Trim();

            return s;
        }

        public static string ExtractPathFromBrowser(dynamic w) {
            try {
                dynamic doc = w.Document;
                if (doc != null) {
                    dynamic folder = doc.Folder;
                    if (folder != null) {
                        dynamic self = folder.Self;
                        if (self != null && !string.IsNullOrEmpty((string)self.Path)) {
                            return (string)self.Path;
                        }
                    }
                }
            } catch {}

            try {
                string url = (string)w.LocationURL;
                if (!string.IsNullOrEmpty(url)) {
                    if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) {
                        return new Uri(url).LocalPath;
                    }
                    return url;
                }
            } catch {}

            return null;
        }
    }
}
