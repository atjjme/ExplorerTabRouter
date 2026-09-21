using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ExplorerTabRouter {
    public static class TabTracker {
        private const int MAX_HISTORY = 50;
        private static readonly Stack<string> _recentlyClosed = new Stack<string>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 记录已关闭标签页的路径入栈
        /// </summary>
        public static void PushClosed(string path) {
            if (string.IsNullOrEmpty(path)) return;
            string norm = ExplorerManager.NormalizePath(path);
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
                    if (!string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path))) {
                        return path;
                    }
                }
                return null;
            }
        }

        public static int Count {
            get {
                lock (_lock) { return _recentlyClosed.Count; }
            }
        }

        /// <summary>
        /// 恢复刚刚关闭的标签页（按 Ctrl + Shift + T 触发）
        /// </summary>
        public static bool RestoreLastClosedTab() {
            string targetPath = PopClosed();
            if (string.IsNullOrEmpty(targetPath)) return false;

            // 优先恢复到当前活跃的 Explorer 窗口，若无则新开窗口
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
                            if (currentIndex == tabIndex && !string.IsNullOrEmpty(p)) {
                                return p;
                            }
                            if (!string.IsNullOrEmpty(tabName) && !string.IsNullOrEmpty(p)) {
                                string locName = (string)w.LocationName;
                                if (string.Equals(locName, tabName, StringComparison.OrdinalIgnoreCase)) {
                                    matchedByName = p;
                                }
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
                dynamic currentShell = shell ?? Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic windows = currentShell.Windows();
                int count = windows.Count;

                for (int i = 0; i < count; i++) {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    try {
                        object hwndObj = w.HWND;
                        if (hwndObj != null && Convert.ToInt64(hwndObj) == (long)cabinet) {
                            string p = ExtractPathFromBrowser(w);
                            if (!string.IsNullOrEmpty(p)) {
                                return p;
                            }
                        }
                    } catch {}
                }
            } catch (Exception ex) {
                Trace.WriteLine("GetActiveCabinetPath error: " + ex.Message);
            }
            return null;
        }

        private static string ExtractPathFromBrowser(dynamic w) {
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
