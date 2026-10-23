using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ExplorerTabRouter {
    public class DaemonWatcher : IDisposable {
        private const uint WM_CLOSE = 0x0010;

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _contextMenu;
        private IntPtr _hookId = IntPtr.Zero;
        private Win32.WinEventDelegate _hookDelegate;
        private readonly HashSet<IntPtr> _knownCabinets = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, HashSet<IntPtr>> _cabinetTabs = new Dictionary<IntPtr, HashSet<IntPtr>>();
        private readonly Dictionary<IntPtr, IntPtr> _tabToCabinet = new Dictionary<IntPtr, IntPtr>();
        private readonly HashSet<IntPtr> _reparentedCabinets = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _pendingInterception = new HashSet<IntPtr>();
        private int _lastTabDestroyedTick = 0;
        private readonly object _lock = new object();
        private bool _isPaused = false;
        private dynamic _shell;

        private dynamic GetShell() {
            try {
                if (_shell != null) {
                    var count = _shell.Windows().Count;
                    return _shell;
                }
            } catch {}
            try {
                _shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            } catch {}
            return _shell;
        }

        private void SyncCabinetTabs(IntPtr cabinet) {
            if (cabinet == IntPtr.Zero || !Win32.IsWindow(cabinet)) return;
            lock (_lock) {
                List<IntPtr> tabs = Win32.GetTabsForCabinet(cabinet);
                HashSet<IntPtr> existingTabs;
                if (!_cabinetTabs.TryGetValue(cabinet, out existingTabs)) {
                    existingTabs = new HashSet<IntPtr>();
                    _cabinetTabs[cabinet] = existingTabs;
                } else {
                    List<IntPtr> toRemove = null;
                    foreach (IntPtr t in existingTabs) {
                        if (!tabs.Contains(t)) {
                            if (toRemove == null) toRemove = new List<IntPtr>();
                            toRemove.Add(t);
                        }
                    }
                    if (toRemove != null) {
                        foreach (IntPtr t in toRemove) {
                            existingTabs.Remove(t);
                            IntPtr cab;
                            if (_tabToCabinet.TryGetValue(t, out cab) && cab == cabinet) {
                                _tabToCabinet.Remove(t);
                            }
                        }
                    }
                }

                foreach (IntPtr t in tabs) {
                    existingTabs.Add(t);
                    _tabToCabinet[t] = cabinet;
                }
            }
        }

        private void RefreshAllKnownCabinetTabs() {
            lock (_lock) {
                List<IntPtr> stale = null;
                foreach (IntPtr cab in _knownCabinets) {
                    if (!Win32.IsWindow(cab)) {
                        if (stale == null) stale = new List<IntPtr>();
                        stale.Add(cab);
                    }
                }
                if (stale != null) {
                    foreach (IntPtr cab in stale) {
                        _knownCabinets.Remove(cab);
                        _pendingInterception.Remove(cab);
                        _reparentedCabinets.Remove(cab);
                        HashSet<IntPtr> oldTabs;
                        if (_cabinetTabs.TryGetValue(cab, out oldTabs)) {
                            foreach (IntPtr t in oldTabs) {
                                IntPtr owner;
                                if (_tabToCabinet.TryGetValue(t, out owner) && owner == cab) {
                                    _tabToCabinet.Remove(t);
                                }
                            }
                            _cabinetTabs.Remove(cab);
                        }
                    }
                }

                foreach (IntPtr cab in _knownCabinets) {
                    SyncCabinetTabs(cab);
                }
            }
        }

        public void Start(bool showTray = true) {
            // 初始化已知 Explorer 窗口与标签状态
            lock (_lock) {
                foreach (IntPtr h in Win32.GetAllCabinetWindows()) {
                    _knownCabinets.Add(h);
                    SyncCabinetTabs(h);
                }
            }

            // 初始化托盘图标（如果开启）
            if (showTray) {
                InitTray();
            }

            // 注册系统级窗口创建、显示与销毁监听 (EVENT_OBJECT_CREATE 到 EVENT_OBJECT_SHOW 涵盖 EVENT_OBJECT_DESTROY)
            _hookDelegate = new Win32.WinEventDelegate(OnWindowEvent);
            _hookId = Win32.SetWinEventHook(
                Win32.EVENT_OBJECT_CREATE,
                Win32.EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                _hookDelegate,
                0,
                0,
                Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS
            );

            Application.Run();
        }

        private void InitTray() {
            _contextMenu = new ContextMenuStrip();

            ToolStripMenuItem titleItem = new ToolStripMenuItem("ExplorerTabRouter 运行中");
            titleItem.Enabled = false;
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
            _contextMenu.Items.Add(titleItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 监听启用/暂停
            ToolStripMenuItem pauseItem = new ToolStripMenuItem("启用标签路由拦截", null, (s, e) => {
                _isPaused = !_isPaused;
                ((ToolStripMenuItem)s).Checked = !_isPaused;
            });
            pauseItem.Checked = !_isPaused;
            _contextMenu.Items.Add(pauseItem);

            // 自动清理可能存在的历史遗留注册表关联，确保内部浏览绝不被误伤
            RegistryHelper.CleanupLegacyFolderAssociation();

            // 开机启动选项
            ToolStripMenuItem autoStartItem = new ToolStripMenuItem("开机自动启动", null, (s, e) => {
                var mi = (ToolStripMenuItem)s;
                if (mi.Checked) {
                    RegistryHelper.UninstallAutoStart();
                    mi.Checked = false;
                } else {
                    RegistryHelper.InstallAutoStart();
                    mi.Checked = true;
                }
            });
            autoStartItem.Checked = RegistryHelper.IsAutoStartInstalled();
            _contextMenu.Items.Add(autoStartItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 快捷操作：当前窗口新建标签
            _contextMenu.Items.Add("在当前窗口新建标签 (Ctrl+T)", null, (s, e) => {
                List<IntPtr> cabs = Win32.GetAllCabinetWindows();
                if (cabs.Count > 0) {
                    ExplorerManager.RouteOpen("");
                } else {
                    ExplorerManager.LaunchNewExplorerWindow("");
                }
            });

            // 关于
            _contextMenu.Items.Add("关于与操作提示...", null, (s, e) => {
                string msg = "ExplorerTabRouter (资源管理器标签路由助手)\n\n" +
                             "核心功能：\n" +
                             "1. 外部打开文件夹时，自动在已有资源管理器窗口的标签页打开。\n" +
                             "2. 深度消除任务栏闪烁：采用底层 Shell 接口从任务栏实时隐形，绝不出现双图标融合。\n" +
                             "3. 极速路由：毫秒级响应并即时关闭多余窗口，保持系统 100% 原生流畅。\n" +
                             "4. 原生支持标签撕脱（Tab Tear-off）：手动从窗口拖出标签拆分完全不受影响。\n" +
                             "5. 强制新窗口技巧：按住 Ctrl + Shift 打开文件夹可强制以全新独立窗口打开。\n" +
                             "6. 智能去重：若目标文件夹已在某标签页打开，自动切换过去，不重复开启。\n\n" +
                             "绿色免安装，随时可在本菜单或脚本中一键还原。";
                MessageBox.Show(msg, "关于 ExplorerTabRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 退出
            _contextMenu.Items.Add("退出", null, (s, e) => {
                Dispose();
                Application.Exit();
            });

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "ExplorerTabRouter";
            try {
                _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            } catch {
                _trayIcon.Icon = SystemIcons.Application;
            }
            _trayIcon.ContextMenuStrip = _contextMenu;
            _trayIcon.Visible = true;
        }

        private void OnWindowEvent(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
            if (_isPaused || idObject != 0 || idChild != 0 || hWnd == IntPtr.Zero) return;

            // 1. 处理窗口/控件销毁事件 (EVENT_OBJECT_DESTROY = 0x8001)
            if (eventType == Win32.EVENT_OBJECT_DESTROY) {
                lock (_lock) {
                    IntPtr ownerCab;
                    if (_tabToCabinet.TryGetValue(hWnd, out ownerCab)) {
                        if (Win32.IsWindow(ownerCab) && Win32.IsWindowVisible(ownerCab)) {
                            _lastTabDestroyedTick = Environment.TickCount;
                        }
                        _tabToCabinet.Remove(hWnd);
                        HashSet<IntPtr> tabs;
                        if (_cabinetTabs.TryGetValue(ownerCab, out tabs)) {
                            tabs.Remove(hWnd);
                        }
                    }
                    if (_knownCabinets.Contains(hWnd)) {
                        _knownCabinets.Remove(hWnd);
                        _pendingInterception.Remove(hWnd);
                        _reparentedCabinets.Remove(hWnd);
                        HashSet<IntPtr> oldTabs;
                        if (_cabinetTabs.TryGetValue(hWnd, out oldTabs)) {
                            foreach (IntPtr t in oldTabs) {
                                IntPtr owner;
                                if (_tabToCabinet.TryGetValue(t, out owner) && owner == hWnd) {
                                    _tabToCabinet.Remove(t);
                                }
                            }
                            _cabinetTabs.Remove(hWnd);
                        }
                    }
                }
                return;
            }

            // 2. 获取窗口类名
            string className = Win32.GetWindowClassName(hWnd);

            // 3. 处理 ShellTabWindowClass (标签页)
            if (className == "ShellTabWindowClass") {
                IntPtr root = Win32.GetAncestor(hWnd, Win32.GA_ROOT);
                if (root != IntPtr.Zero) {
                    lock (_lock) {
                        IntPtr oldRoot;
                        if (_tabToCabinet.TryGetValue(hWnd, out oldRoot) && oldRoot != IntPtr.Zero && oldRoot != root) {
                            // 该 ShellTabWindowClass 从 oldRoot 迁移到了 root (Tab Tear-off)！
                            _reparentedCabinets.Add(root);
                            _tabToCabinet[hWnd] = root;
                            HashSet<IntPtr> oldTabs;
                            if (_cabinetTabs.TryGetValue(oldRoot, out oldTabs)) {
                                oldTabs.Remove(hWnd);
                            }
                            HashSet<IntPtr> newTabs;
                            if (!_cabinetTabs.TryGetValue(root, out newTabs)) {
                                newTabs = new HashSet<IntPtr>();
                                _cabinetTabs[root] = newTabs;
                            }
                            newTabs.Add(hWnd);
                        } else if (!_pendingInterception.Contains(root) && _knownCabinets.Contains(root)) {
                            HashSet<IntPtr> cabTabs;
                            if (!_cabinetTabs.TryGetValue(root, out cabTabs)) {
                                cabTabs = new HashSet<IntPtr>();
                                _cabinetTabs[root] = cabTabs;
                            }
                            cabTabs.Add(hWnd);
                            _tabToCabinet[hWnd] = root;
                        }
                    }
                }
                return;
            }

            // 4. 只处理 CabinetWClass (Explorer 窗口)
            if (className != "CabinetWClass") return;

            // 5. 检查是否正在拦截判断中
            lock (_lock) {
                if (_pendingInterception.Contains(hWnd)) {
                    // 例如 EVENT_OBJECT_CREATE 后紧接着触发 EVENT_OBJECT_SHOW，保持隐形
                    Win32.MakeWindowInvisibleOffscreen(hWnd);
                    return;
                }

                // 6. 清理已失效句柄并判断是否已是已知窗口
                List<IntPtr> stale = null;
                foreach (IntPtr k in _knownCabinets) {
                    if (!Win32.IsWindow(k)) {
                        if (stale == null) stale = new List<IntPtr>();
                        stale.Add(k);
                    }
                }
                if (stale != null) {
                    foreach (IntPtr s in stale) {
                        _knownCabinets.Remove(s);
                        _pendingInterception.Remove(s);
                        _reparentedCabinets.Remove(s);
                        HashSet<IntPtr> oldTabs;
                        if (_cabinetTabs.TryGetValue(s, out oldTabs)) {
                            foreach (IntPtr t in oldTabs) {
                                IntPtr owner;
                                if (_tabToCabinet.TryGetValue(t, out owner) && owner == s) {
                                    _tabToCabinet.Remove(t);
                                }
                            }
                            _cabinetTabs.Remove(s);
                        }
                    }
                }

                if (_knownCabinets.Contains(hWnd)) return;
            }

            // 关键判断 1：检查是否是标签拖出拆分 (Tab Tear-off)
            if (IsTabTearOff(hWnd)) {
                lock (_lock) {
                    _knownCabinets.Add(hWnd);
                    SyncCabinetTabs(hWnd);
                }
                return;
            }

            // 关键判断 2：用户按住 Ctrl + Shift 故意强制打开新窗口
            if (Win32.IsCtrlShiftPressed()) {
                lock (_lock) {
                    _knownCabinets.Add(hWnd);
                    SyncCabinetTabs(hWnd);
                }
                return;
            }

            // 关键判断 3：若窗口标题已表明是控制面板等特殊系统窗口，直接放行独立打开
            string initialTitle = Win32.GetWindowTitle(hWnd);
            if (IsSpecialSystemWindow(null, initialTitle)) {
                lock (_lock) {
                    _knownCabinets.Add(hWnd);
                    SyncCabinetTabs(hWnd);
                }
                return;
            }

            // 关键判断 4：查找系统中是否存在其他已有的宿主 Explorer 窗口
            List<IntPtr> allCabinets = Win32.GetAllCabinetWindows();
            IntPtr hostCabinet = IntPtr.Zero;
            foreach (IntPtr c in allCabinets) {
                if (c != hWnd && Win32.IsWindowVisible(c)) {
                    hostCabinet = c;
                    break;
                }
            }

            // 如果没有其他已有窗口，这本身就是第一个主窗口，直接放行
            if (hostCabinet == IntPtr.Zero) {
                lock (_lock) {
                    _knownCabinets.Add(hWnd);
                    SyncCabinetTabs(hWnd);
                }
                return;
            }

            // 此时确定这是一个外部新弹出的多余 Explorer 窗口，进入待拦截流程
            lock (_lock) {
                _knownCabinets.Add(hWnd);
                _pendingInterception.Add(hWnd);
            }

            // 瞬间将新窗口原地隐形与剥离任务栏图标
            // 注意：不要在此处调用 Win32.ForceForegroundWindow(hostCabinet)，避免发送 Alt 键打断 Explorer 正在进行的标签拆分初始化
            Win32.MakeWindowInvisibleOffscreen(hWnd);

            // 在后台线程中读取打开的路径并路由到已有窗口的 Tab 中
            ThreadPool.QueueUserWorkItem(state => {
                HandleNewExplorerWindow(hWnd, hostCabinet);
            });
        }

        private bool IsTabTearOff(IntPtr newHwnd) {
            // 1. 鼠标左键仍处于按下状态（用户正在拖拽 Tab 拆分）
            if (Win32.IsMouseLButtonPressed()) {
                return true;
            }

            lock (_lock) {
                // 2. 收到过标签重定向迁移到该窗口的 WinEvent
                if (_reparentedCabinets.Contains(newHwnd)) {
                    return true;
                }

                // 3. 检查该窗口当前持有的标签，是否原本属于其它已知窗口
                List<IntPtr> newTabs = Win32.GetTabsForCabinet(newHwnd);
                foreach (IntPtr tabHwnd in newTabs) {
                    IntPtr oldCab;
                    if (_tabToCabinet.TryGetValue(tabHwnd, out oldCab) && oldCab != IntPtr.Zero && oldCab != newHwnd) {
                        return true;
                    }
                }

                // 4. 最近 800ms 内有已知窗口的 Tab 被销毁/移出（拖拽拆分过程中原窗口 Tab 销毁）
                if (_lastTabDestroyedTick != 0 && Math.Abs(unchecked(Environment.TickCount - _lastTabDestroyedTick)) <= 800) {
                    return true;
                }

                // 5. 遍历所有已有窗口，检查是否有存活的旧标签从原窗口移出（Reparented）或实时标签数减少
                foreach (KeyValuePair<IntPtr, HashSet<IntPtr>> kvp in _cabinetTabs) {
                    IntPtr existCab = kvp.Key;
                    if (existCab == newHwnd || !Win32.IsWindow(existCab)) continue;

                    List<IntPtr> currentTabs = Win32.GetTabsForCabinet(existCab);
                    HashSet<IntPtr> knownTabs = kvp.Value;

                    List<IntPtr> deadTabs = null;
                    bool hasMigratedTab = false;
                    foreach (IntPtr oldTab in knownTabs) {
                        if (!Win32.IsWindow(oldTab)) {
                            if (deadTabs == null) deadTabs = new List<IntPtr>();
                            deadTabs.Add(oldTab);
                        } else if (!currentTabs.Contains(oldTab)) {
                            // 窗口句柄依然存活，但已不在原宿主窗口的子窗口列表中 -> 已被迁移拆分出去了！
                            hasMigratedTab = true;
                        }
                    }

                    if (deadTabs != null) {
                        foreach (IntPtr d in deadTabs) {
                            knownTabs.Remove(d);
                            IntPtr owner;
                            if (_tabToCabinet.TryGetValue(d, out owner) && owner == existCab) {
                                _tabToCabinet.Remove(d);
                            }
                        }
                    }

                    if (hasMigratedTab || currentTabs.Count < knownTabs.Count) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSpecialSystemWindow(string path, string title) {
            if (!string.IsNullOrEmpty(path)) {
                // 控制面板 CLSID 或 Shell URL
                if (path.IndexOf("{26EE0668-A00A-44D7-9371-BEB064C98683}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("ControlPanelFolder", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(title)) {
                if (title.IndexOf("Control Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("控制面板", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }
            return false;
        }

        private void HandleNewExplorerWindow(IntPtr newHwnd, IntPtr hostCabinet) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    dynamic shell = GetShell();

                    string openedPath = string.Empty;
                    bool isSpecial = false;

                    // 1. 超低延时自旋检测新窗口路径与标题 (每 10ms 轮询一次，最多等 800ms)
                    for (int i = 0; i < 80; i++) {
                        // 每次循环首先检查是否发生 Tab Tear-off
                        if (IsTabTearOff(newHwnd)) {
                            lock (_lock) {
                                _pendingInterception.Remove(newHwnd);
                            }
                            Win32.RestoreWindow(newHwnd);
                            RefreshAllKnownCabinetTabs();
                            return;
                        }

                        string curTitle = Win32.GetWindowTitle(newHwnd);
                        openedPath = GetPathFromHwnd(newHwnd, shell);

                        if (IsSpecialSystemWindow(openedPath, curTitle)) {
                            isSpecial = true;
                            break;
                        }

                        if (string.IsNullOrEmpty(openedPath)) {
                            if (string.Equals(curTitle, "回收站", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(curTitle, "Recycle Bin", StringComparison.OrdinalIgnoreCase)) {
                                openedPath = "shell:RecycleBinFolder";
                                break;
                            }
                        } else {
                            break;
                        }

                        Thread.Sleep(10);
                    }

                    // 2. 如果识别为控制面板等特殊系统窗口：放行并恢复显示
                    if (isSpecial) {
                        lock (_lock) {
                            _pendingInterception.Remove(newHwnd);
                        }
                        Win32.RestoreWindow(newHwnd);
                        RefreshAllKnownCabinetTabs();
                        return;
                    }

                    // 3. 如果拿到了 openedPath，再额外做最多 10 次 × 10ms (100ms) 的 IsTabTearOff 确认
                    // （以防 Explorer 先创建新窗口标签、几十毫秒后才从原窗口移除旧标签）
                    if (!string.IsNullOrEmpty(openedPath)) {
                        for (int j = 0; j < 10; j++) {
                            if (IsTabTearOff(newHwnd)) {
                                lock (_lock) {
                                    _pendingInterception.Remove(newHwnd);
                                }
                                Win32.RestoreWindow(newHwnd);
                                RefreshAllKnownCabinetTabs();
                                return;
                            }
                            Thread.Sleep(10);
                        }
                    }

                    // 4. 如果超时后仍未捕获到任何有效路径：安全回退恢复新窗口显示
                    if (string.IsNullOrEmpty(openedPath)) {
                        lock (_lock) {
                            _pendingInterception.Remove(newHwnd);
                        }
                        Win32.RestoreWindow(newHwnd);
                        RefreshAllKnownCabinetTabs();
                        return;
                    }

                    // 5. 确认是外部打开的文件夹（或回收站）：
                    lock (_lock) {
                        _pendingInterception.Remove(newHwnd);
                        _knownCabinets.Remove(newHwnd);
                    }

                    // 立即发送 WM_CLOSE 关闭多余新窗口
                    Win32.PostMessage(newHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                    // 在宿主窗口中打开该路径（新标签或切换至已打开标签）
                    ExplorerManager.RouteOpenIntoCabinet(hostCabinet, openedPath, shell);
                    Win32.ForceForegroundWindow(hostCabinet);

                    // 路由完成后同步宿主窗口标签状态
                    RefreshAllKnownCabinetTabs();
                }
            } catch (Exception ex) {
                Trace.WriteLine("HandleNewExplorerWindow error: " + ex.Message);
                lock (_lock) {
                    _pendingInterception.Remove(newHwnd);
                }
                Win32.RestoreWindow(newHwnd);
                RefreshAllKnownCabinetTabs();
            }
        }

        private static string GetPathFromHwnd(IntPtr hWnd, dynamic shell) {
            if (shell == null) return string.Empty;
            try {
                var windows = shell.Windows();
                if (windows == null) return string.Empty;
                foreach (dynamic w in (System.Collections.IEnumerable)windows) {
                    try {
                        if (w != null && (long)w.HWND == (long)hWnd) {
                            string p = ExtractPathFromBrowser(w);
                            if (!string.IsNullOrEmpty(p)) return p;
                        }
                    } catch {}
                }
            } catch {}
            return string.Empty;
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

        public void Dispose() {
            if (_hookId != IntPtr.Zero) {
                Win32.UnhookWinEvent(_hookId);
                _hookId = IntPtr.Zero;
            }
            lock (_lock) {
                _knownCabinets.Clear();
                _cabinetTabs.Clear();
                _tabToCabinet.Clear();
                _reparentedCabinets.Clear();
                _pendingInterception.Clear();
            }
            if (_trayIcon != null) {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_contextMenu != null) {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }
    }
}
