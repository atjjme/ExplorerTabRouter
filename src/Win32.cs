using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ExplorerTabRouter {
    public static class Win32 {
        public const uint WM_COMMAND = 0x0111;
        public const uint WM_SYSCOMMAND = 0x0112;
        public const int SC_RESTORE = 0xF120;

        // Windows 11 Explorer Tab Commands
        public const int CMD_NEW_TAB = 0xA21B;       // Ctrl + T (新建标签)
        public const int CMD_CLOSE_TAB = 0xA021;     // Ctrl + W (关闭当前标签)
        public const int CMD_SWITCH_TAB = 0xA221;    // Ctrl + 1..n (切换标签，lParam为索引+1)

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;
        public const int SW_SHOWNOACTIVATE = 4;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const uint LWA_ALPHA = 0x2;

        public const uint EVENT_OBJECT_CREATE = 0x8000;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint WINEVENT_OUTOFCONTEXT = 0;
        public const uint WINEVENT_SKIPOWNPROCESS = 2;

        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_LBUTTON = 0x01;

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        public const int DWMWA_NCRENDERING_POLICY = 2;
        public const int DWMNCRP_USEWINDOWSTYLE = 0;
        public const int DWMNCRP_DISABLED = 1;
        public const int DWMNCRP_ENABLED = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hModWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);


        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        public static int GetWindowLong(IntPtr hWnd, int nIndex) {
            if (IntPtr.Size == 8)
                return unchecked((int)GetWindowLongPtr64(hWnd, nIndex).ToInt64());
            return GetWindowLong32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong) {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong));
        }

        [ComImport]
        [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ITaskbarList {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hWnd);
            [PreserveSig] int DeleteTab(IntPtr hWnd);
            [PreserveSig] int ActivateTab(IntPtr hWnd);
            [PreserveSig] int SetActiveAlt(IntPtr hWnd);
        }

        private static ITaskbarList _taskbarList;
        private static readonly object _taskbarLock = new object();

        public static void RemoveFromTaskbar(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return;
            try {
                if (_taskbarList == null) {
                    lock (_taskbarLock) {
                        if (_taskbarList == null) {
                            Type t = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"));
                            if (t != null) {
                                _taskbarList = (ITaskbarList)Activator.CreateInstance(t);
                                _taskbarList.HrInit();
                            }
                        }
                    }
                }
                if (_taskbarList != null) {
                    _taskbarList.DeleteTab(hWnd);
                }
            } catch {}
        }

        public static void AddToTaskbar(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return;
            try {
                if (_taskbarList != null) {
                    _taskbarList.AddTab(hWnd);
                }
            } catch {}
        }

        /// <summary>
        /// 原地隐形、完全透明并从任务栏剥离图标，彻底消除新窗口弹出与任务栏图标闪烁/融合感
        /// 不移动坐标与不调用 SW_HIDE，以保护 Tab 拖出（Tear-off）定位与 Explorer 内部标签拆分流程
        /// </summary>
        public static void MakeWindowInvisibleOffscreen(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return;
            try {
                // 1. 设置 ToolWindow 并剥离 AppWindow 样式（通知系统绝不为其分配任务栏按钮）
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                SetWindowLong(hWnd, GWL_EXSTYLE, (exStyle | WS_EX_LAYERED | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

                // 2. 从任务栏直接删除该窗口项
                RemoveFromTaskbar(hWnd);

                // 3. 置为 0% 透明度 (完全不可见)
                SetLayeredWindowAttributes(hWnd, 0, 0, LWA_ALPHA);

                // 4. 关闭 DWM 边框与阴影渲染以消除透明窗口阴影轮廓
                int policy = DWMNCRP_DISABLED;
                DwmSetWindowAttribute(hWnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            } catch {}
        }

        /// <summary>
        /// 将被临时原地隐形的窗口恢复原貌并置前显示
        /// 保持窗口原位尺寸与坐标（确保拖拽拆分的标签窗口精准停留在用户松手位置）
        /// </summary>
        public static void RestoreWindow(IntPtr hWnd, RECT? originalRect = null) {
            if (hWnd == IntPtr.Zero) return;
            try {
                // 1. 还原扩展样式：移除 WS_EX_TOOLWINDOW，恢复 WS_EX_APPWINDOW
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                SetWindowLong(hWnd, GWL_EXSTYLE, (exStyle | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW);

                // 2. 恢复 DWM 边框/阴影渲染
                int policy = DWMNCRP_USEWINDOWSTYLE;
                DwmSetWindowAttribute(hWnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                // 3. 恢复 100% 不透明度
                SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);

                // 4. 重新加回任务栏
                AddToTaskbar(hWnd);

                // 5. 还原窗口几何位置：仅当窗口坐标真正处于屏幕外时才调整
                RECT curRect;
                GetWindowRect(hWnd, out curRect);
                if (curRect.Left < -10000) {
                    if (originalRect.HasValue && originalRect.Value.Left > -10000 && originalRect.Value.Width > 100 && originalRect.Value.Height > 100) {
                        RECT r = originalRect.Value;
                        SetWindowPos(hWnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_SHOWWINDOW);
                    } else {
                        // 默认居中主屏幕显示
                        int screenW = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Width;
                        int screenH = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Height;
                        int w = Math.Min(1024, (int)(screenW * 0.8));
                        int h = Math.Min(700, (int)(screenH * 0.8));
                        int x = (screenW - w) / 2;
                        int y = (screenH - h) / 2;
                        SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_SHOWWINDOW);
                    }
                } else {
                    SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
                }

                // 6. 显示并置前激活
                ShowWindow(hWnd, SW_SHOWNORMAL);
                ForceForegroundWindow(hWnd);
            } catch {}
        }

        /// <summary>
        /// 确保当前线程附加到交互式桌面上，以便在后台任务中枚举和操作窗口
        /// </summary>
        public static IDisposable AttachToInteractiveDesktop() {
            IntPtr hDesk = OpenDesktop("Default", 0, false, 0x01FF);
            if (hDesk != IntPtr.Zero) {
                SetThreadDesktop(hDesk);
                return new DesktopScope(hDesk);
            }
            return new DummyScope();
        }

        private class DesktopScope : IDisposable {
            private IntPtr _hDesk;
            public DesktopScope(IntPtr hDesk) {
                _hDesk = hDesk;
            }
            public void Dispose() {
                if (_hDesk != IntPtr.Zero) {
                    CloseDesktop(_hDesk);
                    _hDesk = IntPtr.Zero;
                }
            }
        }

        private class DummyScope : IDisposable {
            public void Dispose() {}
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private static readonly IntPtr HWND_TOP = new IntPtr(0);

        /// <summary>
        /// 强行将窗口置前激活（突破 Windows 限制，彻底消除任务栏呼吸灯拦截）
        /// </summary>
        public static void ForceForegroundWindow(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

            if (IsIconic(hWnd)) {
                ShowWindow(hWnd, SW_RESTORE);
            } else {
                ShowWindow(hWnd, SW_SHOW);
            }

            IntPtr foreHwnd = GetForegroundWindow();
            if (foreHwnd == hWnd) {
                BringWindowToTop(hWnd);
                return;
            }

            uint currentThread = GetCurrentThreadId();
            uint foreThread = 0;
            if (foreHwnd != IntPtr.Zero) {
                GetWindowThreadProcessId(foreHwnd, out foreThread);
            }
            uint targetThread = 0;
            GetWindowThreadProcessId(hWnd, out targetThread);

            // 模拟触发 Alt 键按键弹起，重置系统前台锁定计时器（打破呼吸灯限制）
            keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            bool attachedFore = false;
            bool attachedTarget = false;
            try {
                if (foreThread != 0 && foreThread != currentThread) {
                    attachedFore = AttachThreadInput(currentThread, foreThread, true);
                }
                if (targetThread != 0 && targetThread != currentThread && targetThread != foreThread) {
                    attachedTarget = AttachThreadInput(currentThread, targetThread, true);
                }

                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);

                // 通过快速 TOPMOST -> NOTOPMOST 刷新窗口 Z-Order，确保必居最前
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                SetForegroundWindow(hWnd);
            } finally {
                if (attachedFore) AttachThreadInput(currentThread, foreThread, false);
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
            }
        }

        /// <summary>
        /// 检查是否按下了 Ctrl + Shift（用户故意要打开新窗口）
        /// </summary>
        public static bool IsCtrlShiftPressed() {
            return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        }

        /// <summary>
        /// 检查鼠标左键是否处于按下状态（用于检测用户拖拽 Tab 拆分窗口）
        /// </summary>
        public static bool IsMouseLButtonPressed() {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }

        /// <summary>
        /// 获取当前所有的 CabinetWClass (Explorer 窗口)
        /// </summary>
        public static List<IntPtr> GetAllCabinetWindows() {
            using (AttachToInteractiveDesktop()) {
                List<IntPtr> list = new List<IntPtr>();
                IntPtr current = IntPtr.Zero;
                while (true) {
                    current = FindWindowEx(IntPtr.Zero, current, "CabinetWClass", null);
                    if (current == IntPtr.Zero) break;
                    list.Add(current);
                }
                return list;
            }
        }

        /// <summary>
        /// 获取指定 Explorer 窗口内的所有 Tab (ShellTabWindowClass)
        /// </summary>
        public static List<IntPtr> GetTabsForCabinet(IntPtr cabinet) {
            using (AttachToInteractiveDesktop()) {
                List<IntPtr> list = new List<IntPtr>();
                if (cabinet == IntPtr.Zero) return list;
                IntPtr current = IntPtr.Zero;
                while (true) {
                    current = FindWindowEx(cabinet, current, "ShellTabWindowClass", null);
                    if (current == IntPtr.Zero) break;
                    list.Add(current);
                }
                return list;
            }
        }

        /// <summary>
        /// 获取窗口的类名
        /// </summary>
        public static string GetWindowClassName(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// 获取窗口标题
        /// </summary>
        public static string GetWindowTitle(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, 512);
            return sb.ToString();
        }
    }
}
