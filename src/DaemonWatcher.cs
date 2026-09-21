using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace ExplorerTabRouter {
    public class DaemonWatcher : IDisposable {
        private const uint WM_CLOSE = 0x0010;

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _contextMenu;
        private IntPtr _hookId = IntPtr.Zero;
        private Win32.WinEventDelegate _hookDelegate;
        private readonly HashSet<IntPtr> _knownCabinets = new HashSet<IntPtr>();
        private readonly object _lock = new object();
        private bool _isPaused = false;
        private dynamic _shell;

        // 双击标签关闭 & 快捷键恢复
        private Win32.HookProc _mouseHookProc;
        private Win32.HookProc _kbHookProc;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _kbHookId = IntPtr.Zero;
        private uint _lastLDownTime = 0;
        private Win32.POINT _lastLDownPt;
        private bool _enableDoubleClickClose = true;
        private bool _enableRestoreTabShortcut = true;
        private System.Threading.Timer _tabSyncTimer;

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

        public void Start(bool showTray = true) {
            // 初始化已知 Explorer 窗口
            lock (_lock) {
                foreach (IntPtr h in Win32.GetAllCabinetWindows()) {
                    _knownCabinets.Add(h);
                }
            }

            // 初始化托盘图标（如果开启）
            if (showTray) {
                InitTray();
            }

            // 注册系统级窗口创建与显示监听 (从 EVENT_OBJECT_CREATE 到 EVENT_OBJECT_SHOW，包含 EVENT_OBJECT_DESTROY)
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

            // 注册低级鼠标与键盘钩子
            _mouseHookProc = new Win32.HookProc(OnMouseHook);
            _kbHookProc = new Win32.HookProc(OnKeyboardHook);
            IntPtr hMod = Marshal.GetHINSTANCE(typeof(Program).Module);
            _mouseHookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
            _kbHookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _kbHookProc, hMod, 0);

            // 启动轻量级后台标签差量同步定时器 (300ms 间隔，无感捕获任何方式关闭的标签)
            _tabSyncTimer = new System.Threading.Timer(OnTabSyncTimer, null, 1000, 300);

            Application.Run();
        }

        private void OnTabSyncTimer(object state) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    dynamic shell = GetShell();
                    TabTracker.SyncOpenTabs(shell);
                }
            } catch {}
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

            // 双击标签关闭开关
            ToolStripMenuItem dblCloseItem = new ToolStripMenuItem("启用双击关闭标签页", null, (s, e) => {
                _enableDoubleClickClose = !_enableDoubleClickClose;
                ((ToolStripMenuItem)s).Checked = _enableDoubleClickClose;
            });
            dblCloseItem.Checked = _enableDoubleClickClose;
            _contextMenu.Items.Add(dblCloseItem);

            // 快捷键恢复标签页开关
            ToolStripMenuItem restoreTabItem = new ToolStripMenuItem("启用恢复标签页快捷键 (Ctrl+Shift+T)", null, (s, e) => {
                _enableRestoreTabShortcut = !_enableRestoreTabShortcut;
                ((ToolStripMenuItem)s).Checked = _enableRestoreTabShortcut;
            });
            restoreTabItem.Checked = _enableRestoreTabShortcut;
            _contextMenu.Items.Add(restoreTabItem);

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

            // 快捷操作：恢复刚刚关闭的标签
            _contextMenu.Items.Add("恢复刚刚关闭的标签 (Ctrl+Shift+T)", null, (s, e) => {
                ThreadPool.QueueUserWorkItem(_ => {
                    using (Win32.AttachToInteractiveDesktop()) {
                        TabTracker.RestoreLastClosedTab();
                    }
                });
            });

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
                             "功能特性：\n" +
                             "1. 外部打开文件夹时，自动在已有资源管理器窗口的标签页打开。\n" +
                             "2. 支持双击标签页快速关闭当前标签（双击空白处仍保留最大化/还原）。\n" +
                             "3. 支持快捷键 Ctrl + Shift + T 瞬间恢复刚刚关闭的标签页！\n" +
                             "4. 原生支持标签撕脱（Tab Tear-off）：手动从窗口拖出标签拆分完全不受影响！\n" +
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
            // 鼠标悬停只显示软件名称
            _trayIcon.Text = "ExplorerTabRouter";
            try {
                _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            } catch {
                _trayIcon.Icon = SystemIcons.Application;
            }
            _trayIcon.ContextMenuStrip = _contextMenu;
            _trayIcon.Visible = true;
        }

        private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && _enableDoubleClickClose) {
                try {
                    bool isLDown = wParam == (IntPtr)Win32.WM_LBUTTONDOWN;
                    bool isMDown = wParam == (IntPtr)Win32.WM_MBUTTONDOWN;

                    if (isLDown || isMDown) {
                        Win32.MSLLHOOKSTRUCT ms = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));

                        IntPtr wnd = Win32.WindowFromPoint(ms.pt);
                        IntPtr root = Win32.GetAncestor(wnd, Win32.GA_ROOT);
                        if (root == IntPtr.Zero) root = wnd;

                        string cls = root != IntPtr.Zero ? Win32.GetWindowClassName(root) : "";
                        if (cls == "CabinetWClass") {
                            Win32.RECT r;
                            Win32.GetWindowRect(root, out r);

                            // 检查 Y 是否在顶部标签栏垂直范围（窗口顶部向下 90 像素内，适配高DPI）
                            if (ms.pt.y >= r.Top && ms.pt.y <= r.Top + 90) {
                                // 1. 鼠标中键滚轮单击标签直接关闭
                                if (isMDown) {
                                    Win32.POINT clickPt = ms.pt;
                                    IntPtr cabinetHwnd = root;
                                    ThreadPool.QueueUserWorkItem(_ => {
                                        HandleTabDoubleClick(cabinetHwnd, clickPt);
                                    });
                                    return (IntPtr)1; // 拦截中键滚轮事件，避免产生滚轮滚动标志
                                }

                                // 2. 鼠标左键双击标签关闭检测
                                if (isLDown) {
                                    uint now = (uint)Environment.TickCount;
                                    uint dbTime = Win32.GetDoubleClickTime();
                                    int cx = Math.Max(Win32.GetSystemMetrics(Win32.SM_CXDOUBLECLK) * 3, 25);
                                    int cy = Math.Max(Win32.GetSystemMetrics(Win32.SM_CYDOUBLECLK) * 3, 20);

                                    bool isDbl = (_lastLDownTime != 0) &&
                                                 (now >= _lastLDownTime) &&
                                                 (now - _lastLDownTime <= dbTime) &&
                                                 Math.Abs(ms.pt.x - _lastLDownPt.x) <= cx &&
                                                 Math.Abs(ms.pt.y - _lastLDownPt.y) <= cy;

                                    _lastLDownTime = now;
                                    _lastLDownPt = ms.pt;

                                    if (isDbl) {
                                        _lastLDownTime = 0; // 防止三击再次触发
                                        Win32.POINT clickPt = ms.pt;
                                        IntPtr cabinetHwnd = root;
                                        ThreadPool.QueueUserWorkItem(_ => {
                                            HandleTabDoubleClick(cabinetHwnd, clickPt);
                                        });
                                    }
                                }
                            }
                        }
                    }
                } catch {}
            }
            return Win32.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void HandleTabDoubleClick(IntPtr cabinet, Win32.POINT pt) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    AutomationElement cabElem = AutomationElement.FromHandle(cabinet);
                    if (cabElem == null) return;

                    // 核心极速优化：Win11 资源管理器的标签栏位于直属子项 DesktopChildSiteBridge 容器内
                    // 仅在该轻量级容器内检索 TabItem，耗时从全树遍历的 600ms 剧降至 30~40ms！
                    Condition bridgeCond = new PropertyCondition(AutomationElement.ClassNameProperty, "Microsoft.UI.Content.DesktopChildSiteBridge");
                    AutomationElement bridge = cabElem.FindFirst(TreeScope.Children, bridgeCond);
                    AutomationElement searchRoot = bridge ?? cabElem;

                    Condition tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                    AutomationElementCollection tabs = searchRoot.FindAll(TreeScope.Descendants, tabCond);

                    AutomationElement targetTab = null;
                    System.Windows.Point p = new System.Windows.Point(pt.x, pt.y);
                    int tabIndex = -1;

                    for (int i = 0; i < tabs.Count; i++) {
                        AutomationElement t = tabs[i];
                        System.Windows.Rect r = t.Current.BoundingRectangle;
                        if (r.Contains(p)) {
                            targetTab = t;
                            tabIndex = i;
                            break;
                        }
                    }

                    if (targetTab != null) {
                        // 提取并记录该 Tab 的路径入栈
                        string tabName = targetTab.Current.Name;
                        dynamic shell = GetShell();
                        string path = TabTracker.GetPathFromTab(cabinet, tabIndex, tabName, shell);
                        if (!string.IsNullOrEmpty(path)) {
                            TabTracker.PushClosed(path);
                        }

                        // 查找该 TabItem 内部的关闭按钮并触发
                        Condition btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                        AutomationElement closeBtn = targetTab.FindFirst(TreeScope.Children, btnCond);
                        if (closeBtn != null) {
                            InvokePattern inv = closeBtn.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                            if (inv != null) {
                                inv.Invoke();
                                // 触发后台同步
                                ThreadPool.QueueUserWorkItem(_ => {
                                    Thread.Sleep(50);
                                    TabTracker.SyncOpenTabs(shell);
                                });
                                return;
                            }
                        }

                        // 兜底：如果未找到按钮，向窗口发送关闭当前标签指令
                        Win32.PostMessage(cabinet, Win32.WM_COMMAND, (IntPtr)Win32.CMD_CLOSE_TAB, IntPtr.Zero);
                    }
                }
            } catch (Exception ex) {
                Trace.WriteLine("HandleTabDoubleClick error: " + ex.Message);
            }
        }

        private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && (wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN)) {
                try {
                    Win32.KBDLLHOOKSTRUCT kb = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));

                    bool ctrl = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
                    bool shift = (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0;
                    bool alt = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;

                    // 1. 恢复刚关闭的标签页 (Ctrl + Shift + T)
                    if (kb.vkCode == Win32.VK_T && ctrl && shift && !alt) {
                        if (_enableRestoreTabShortcut) {
                            IntPtr fore = Win32.GetForegroundWindow();
                            string cls = Win32.GetWindowClassName(fore);
                            // 仅当焦点在 Explorer 窗口、桌面或无其他窗口时响应
                            if (cls == "CabinetWClass" || cls == "Progman" || cls == "WorkerW" || Win32.GetAllCabinetWindows().Count == 0) {
                                ThreadPool.QueueUserWorkItem(_ => {
                                    using (Win32.AttachToInteractiveDesktop()) {
                                        TabTracker.RestoreLastClosedTab();
                                    }
                                });
                                return (IntPtr)1; // 拦截按键，不干扰其他应用
                            }
                        }
                    }

                    // 2. 监听 Ctrl + W (记录被关闭的标签路径)
                    if (kb.vkCode == Win32.VK_W && ctrl && !shift && !alt) {
                        IntPtr fore = Win32.GetForegroundWindow();
                        if (Win32.GetWindowClassName(fore) == "CabinetWClass") {
                            ThreadPool.QueueUserWorkItem(_ => {
                                try {
                                    using (Win32.AttachToInteractiveDesktop()) {
                                        string activePath = TabTracker.GetActiveCabinetPath(fore, GetShell());
                                        if (!string.IsNullOrEmpty(activePath)) {
                                            TabTracker.PushClosed(activePath);
                                        }
                                    }
                                } catch {}
                            });
                        }
                    }
                } catch {}
            }
            return Win32.CallNextHookEx(_kbHookId, nCode, wParam, lParam);
        }

        private void OnWindowEvent(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
            if (_isPaused || idObject != 0 || idChild != 0 || hWnd == IntPtr.Zero) return;

            // 监听窗口或标签被销毁事件，即时同步标签差量捕获已关闭标签
            if (eventType == Win32.EVENT_OBJECT_DESTROY) {
                ThreadPool.QueueUserWorkItem(_ => OnTabSyncTimer(null));
                return;
            }

            // 只处理 CabinetWClass (Explorer 窗口)
            string className = Win32.GetWindowClassName(hWnd);
            if (className != "CabinetWClass") return;

            lock (_lock) {
                // 清理已失效的窗口句柄
                List<IntPtr> stale = null;
                foreach (IntPtr k in _knownCabinets) {
                    if (!Win32.IsWindow(k)) {
                        if (stale == null) stale = new List<IntPtr>();
                        stale.Add(k);
                    }
                }
                if (stale != null) {
                    foreach (IntPtr s in stale) _knownCabinets.Remove(s);
                }

                if (_knownCabinets.Contains(hWnd)) return;
            }

            // 关键判断 1：检查鼠标左键是否按下
            // 如果用户正在按住鼠标左键，说明这是用户把某个 Tab 拖出窗口拆分（Tab Tear-off）！
            // 此时必须直接放行，绝不抓回！
            if (Win32.IsMouseLButtonPressed()) {
                lock (_lock) { _knownCabinets.Add(hWnd); }
                return;
            }

            // 关键判断 2：用户按住 Ctrl + Shift 故意强制打开新窗口
            if (Win32.IsCtrlShiftPressed()) {
                lock (_lock) { _knownCabinets.Add(hWnd); }
                return;
            }

            // 关键判断 3：查找系统中是否存在其他已有的宿主 Explorer 窗口
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
                lock (_lock) { _knownCabinets.Add(hWnd); }
                return;
            }

            // 此时确定这是一个外部新弹出的多余 Explorer 窗口！
            lock (_lock) {
                _knownCabinets.Add(hWnd);
            }

            // 【核心消除任务栏双图标与闪烁】：
            // 瞬间将新窗口置为 ToolWindow 移除任务栏图标、100% 透明、SW_HIDE 并移出屏幕！
            // 耗时 < 0.1ms，系统任务栏绝不会出现第二个图标！
            Win32.MakeWindowInvisibleOffscreen(hWnd);

            // 立即将宿主窗口置前激活
            Win32.ForceForegroundWindow(hostCabinet);

            // 在后台线程中读取打开的路径并路由到已有窗口的 Tab 中
            ThreadPool.QueueUserWorkItem(state => {
                HandleNewExplorerWindow(hWnd, hostCabinet);
            });
        }

        private void HandleNewExplorerWindow(IntPtr newHwnd, IntPtr hostCabinet) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    // 双重保障：确保处于完全不可见、无任务栏按钮与屏幕外状态
                    Win32.MakeWindowInvisibleOffscreen(newHwnd);
                    Win32.ForceForegroundWindow(hostCabinet);

                    dynamic shell = GetShell();

                    // 1. 超低延时自旋检测新窗口路径 (每 10ms 轮询一次，最多等 800ms)
                    string openedPath = string.Empty;
                    for (int i = 0; i < 80; i++) {
                        openedPath = GetPathFromHwnd(newHwnd, shell);
                        if (!string.IsNullOrEmpty(openedPath)) break;
                        Thread.Sleep(10);
                    }

                    // 2. 【核心加速】：一旦捕获到路径，立即发送 WM_CLOSE 销毁多余新窗口！
                    // 绝不等待宿主新标签导航结束，提前 500ms 彻底关闭多余窗口，完全消除延迟！
                    Win32.PostMessage(newHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                    // 3. 在宿主窗口中打开该路径（新标签或切换至已打开标签）
                    ExplorerManager.RouteOpenIntoCabinet(hostCabinet, openedPath, shell);
                    Win32.ForceForegroundWindow(hostCabinet);
                }
            } catch (Exception ex) {
                Trace.WriteLine("HandleNewExplorerWindow error: " + ex.Message);
            }
        }

        private static string GetPathFromHwnd(IntPtr hWnd, dynamic shell) {
            if (shell == null) return string.Empty;
            try {
                foreach (dynamic w in (System.Collections.IEnumerable)shell.Windows()) {
                    try {
                        if (w != null && (long)w.HWND == (long)hWnd) {
                            string p = TabTracker.ExtractPathFromBrowser(w);
                            if (!string.IsNullOrEmpty(p)) return p;
                        }
                    } catch {}
                }
            } catch {}
            return string.Empty;
        }

        public void Dispose() {
            if (_tabSyncTimer != null) {
                _tabSyncTimer.Dispose();
                _tabSyncTimer = null;
            }
            if (_mouseHookId != IntPtr.Zero) {
                Win32.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
            if (_kbHookId != IntPtr.Zero) {
                Win32.UnhookWindowsHookEx(_kbHookId);
                _kbHookId = IntPtr.Zero;
            }
            if (_hookId != IntPtr.Zero) {
                Win32.UnhookWinEvent(_hookId);
                _hookId = IntPtr.Zero;
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
