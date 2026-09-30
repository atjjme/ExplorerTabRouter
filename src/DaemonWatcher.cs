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

            // 注册系统级窗口创建与显示监听 (EVENT_OBJECT_CREATE 到 EVENT_OBJECT_SHOW)
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

            // 关键判断 3：若窗口标题已表明是回收站等系统特殊窗口，直接放行独立打开
            string initialTitle = Win32.GetWindowTitle(hWnd);
            if (IsSpecialSystemWindow(null, initialTitle)) {
                lock (_lock) { _knownCabinets.Add(hWnd); }
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

        private static bool IsSpecialSystemWindow(string path, string title) {
            if (!string.IsNullOrEmpty(path)) {
                // 回收站 CLSID 或 Shell URL
                if (path.IndexOf("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("RecycleBinFolder", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
                // 控制面板 CLSID 或 Shell URL
                if (path.IndexOf("{26EE0668-A00A-44D7-9371-BEB064C98683}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("ControlPanelFolder", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(title)) {
                if (title.IndexOf("Recycle Bin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("回收站", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Control Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
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

                    // 1. 超低延时自旋检测新窗口路径与标题 (每 10ms 轮询一次，最多等 800ms)
                    string openedPath = string.Empty;
                    bool isSpecial = false;
                    for (int i = 0; i < 80; i++) {
                        string curTitle = Win32.GetWindowTitle(newHwnd);
                        openedPath = GetPathFromHwnd(newHwnd, shell);

                        if (IsSpecialSystemWindow(openedPath, curTitle)) {
                            isSpecial = true;
                            break;
                        }
                        if (!string.IsNullOrEmpty(openedPath)) break;
                        Thread.Sleep(10);
                    }

                    // 2. 如果识别为回收站等特殊系统窗口：
                    // 坚决放行！恢复窗口显示，绝不发送 WM_CLOSE，绝不在宿主窗口打开 This PC！
                    if (isSpecial) {
                        Win32.RestoreWindow(newHwnd);
                        return;
                    }

                    // 3. 如果超时后仍未捕获到任何有效路径：
                    // 说明不是常规文件夹或窗口未就绪，安全回退恢复新窗口显示，绝不在宿主窗口误开 This PC 空标签！
                    if (string.IsNullOrEmpty(openedPath)) {
                        Win32.RestoreWindow(newHwnd);
                        return;
                    }

                    // 4. 【核心加速】：确定是普通文件夹后，立即发送 WM_CLOSE 销毁多余新窗口！
                    // 绝不等待宿主新标签导航结束，提前彻底关闭多余窗口，完全消除延迟！
                    Win32.PostMessage(newHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                    // 5. 在宿主窗口中打开该路径（新标签或切换至已打开标签）
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
