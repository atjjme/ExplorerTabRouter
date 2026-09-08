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

        public void Start() {
            // 初始化已知 Explorer 窗口
            lock (_lock) {
                foreach (IntPtr h in Win32.GetAllCabinetWindows()) {
                    _knownCabinets.Add(h);
                }
            }

            // 初始化托盘图标
            InitTray();

            // 注册系统级窗口显示监听
            _hookDelegate = new Win32.WinEventDelegate(OnWindowShown);
            _hookId = Win32.SetWinEventHook(
                Win32.EVENT_OBJECT_SHOW,
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

            // 文件夹关联选项
            ToolStripMenuItem assocItem = new ToolStripMenuItem("注册文件夹右键/双击路由", null, (s, e) => {
                var mi = (ToolStripMenuItem)s;
                if (mi.Checked) {
                    RegistryHelper.UninstallFolderAssociation();
                    mi.Checked = false;
                    MessageBox.Show("已取消文件夹打开关联，还原为系统默认。", "ExplorerTabRouter");
                } else {
                    RegistryHelper.InstallFolderAssociation();
                    mi.Checked = true;
                    MessageBox.Show("已成功注册文件夹打开关联！外部打开文件夹将自动路由到已有窗口新标签。", "ExplorerTabRouter");
                }
            });
            assocItem.Checked = RegistryHelper.IsFolderAssociationInstalled();
            _contextMenu.Items.Add(assocItem);

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
                             "功能特性：\n" +
                             "1. 外部打开文件夹时，自动在已有资源管理器窗口的标签页打开。\n" +
                             "2. 原生支持标签撕脱（Tab Tear-off）：手动从窗口拖出标签拆分完全不受影响！\n" +
                             "3. 强制新窗口技巧：按住 Ctrl + Shift 打开文件夹可强制以全新独立窗口打开。\n" +
                             "4. 智能去重：若目标文件夹已在某标签页打开，自动切换过去，不重复开启。\n\n" +
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
            _trayIcon.Text = "ExplorerTabRouter (标签路由助手)";
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.ContextMenuStrip = _contextMenu;
            _trayIcon.Visible = true;
        }

        private void OnWindowShown(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
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
                _knownCabinets.Add(hWnd);
            }

            // 关键判断 1：检查鼠标左键是否按下
            // 如果用户正在按住鼠标左键，说明这是用户把某个 Tab 拖出窗口拆分（Tab Tear-off）！
            // 此时必须直接放行，绝不抓回！
            if (Win32.IsMouseLButtonPressed()) {
                return;
            }

            // 关键判断 2：用户按住 Ctrl + Shift 故意强制打开新窗口
            if (Win32.IsCtrlShiftPressed()) {
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
                return;
            }

            // 此时确定这是一个外部新弹出的多余 Explorer 窗口！
            // 在后台线程中进行优雅拦截并合并
            ThreadPool.QueueUserWorkItem(state => {
                HandleNewExplorerWindow(hWnd, hostCabinet);
            });
        }

        private void HandleNewExplorerWindow(IntPtr newHwnd, IntPtr hostCabinet) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    // 立即将新窗口移出屏幕外部，防止在屏幕上弹窗晃眼，但保持可见状态以便 Shell 正常加载
                    Win32.SetWindowPos(newHwnd, IntPtr.Zero, -32000, -32000, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

                    // 等待新窗口的路径信息就绪 (最多等 1000ms)
                    string openedPath = string.Empty;
                    for (int i = 0; i < 20; i++) {
                        openedPath = GetPathFromHwnd(newHwnd);
                        if (!string.IsNullOrEmpty(openedPath)) break;
                        Thread.Sleep(50);
                    }

                    // 在宿主窗口中打开该路径（新标签或切换至已打开标签）
                    ExplorerManager.RouteOpenIntoCabinet(hostCabinet, openedPath);

                    // 关闭多余的新窗口
                    Thread.Sleep(100);
                    Win32.PostMessage(newHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            } catch (Exception ex) {
                Trace.WriteLine("HandleNewExplorerWindow error: " + ex.Message);
            }
        }

        private string GetPathFromHwnd(IntPtr hWnd) {
            try {
                using (Win32.AttachToInteractiveDesktop()) {
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    foreach (dynamic w in (System.Collections.IEnumerable)shell.Windows()) {
                        try {
                            if (w != null && (long)w.HWND == (long)hWnd) {
                                string url = (string)w.LocationURL;
                                if (!string.IsNullOrEmpty(url)) {
                                    if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) {
                                        return new Uri(url).LocalPath;
                                    }
                                    return url;
                                }
                            }
                        } catch {}
                    }
                }
            } catch {}
            return string.Empty;
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
