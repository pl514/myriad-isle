using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 把微信/QQ 窗口叫到前台（普通版）。
    //
    // 实测结论（共享给 Pro 版）：
    //   · 不能强行 ShowWindow 托盘里的窗口 —— 客户端不会重绘，得到白屏/黑屏；
    //   · 正确做法是发客户端自己的"显示"热键（微信 Ctrl+Alt+W / QQ Ctrl+Alt+X），
    //     让客户端自己把窗口弹出来；
    //   · 热键可在 data\messenger.txt 里改。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        struct Win32Rect { public int Left, Top, Right, Bottom; }

        const int SwShowNormal = 1;
        const int SwRestore = 9;
        const uint KeyEventKeyUp = 0x0002;

        sealed class MessengerApp
        {
            public string Id;
            public string Display;
            public string[] ProcessNames;
            public string ClassHint;
        }

        static readonly MessengerApp WeChatApp = new MessengerApp
        {
            Id = "wechat",
            Display = "微信",
            ProcessNames = new string[] { "Weixin", "WeChat" },
            ClassHint = "QWindowIcon"
        };

        static readonly MessengerApp QqApp = new MessengerApp
        {
            Id = "qq",
            Display = "QQ",
            ProcessNames = new string[] { "QQ", "QQNT" },
            ClassHint = "Chrome_WidgetWin_1"
        };

        static readonly Dictionary<string, IntPtr> messengerKnownHwnd = new Dictionary<string, IntPtr>();
        DispatcherTimer messengerLearnTimer;

        static MessengerApp MessengerById(string id)
        {
            return id == "qq" ? QqApp : WeChatApp;
        }

        static string MessengerLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "messenger.log"); }
        }

        static void MessengerLog(string message)
        {
            try
            {
                string folder = Path.GetDirectoryName(MessengerLogPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(MessengerLogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        void StartMessengerLearning()
        {
            if (messengerLearnTimer != null) return;
            messengerLearnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            messengerLearnTimer.Tick += delegate { LearnMessengerWindow(); };
            messengerLearnTimer.Start();
        }

        void LearnMessengerWindow()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return;
                int pid;
                GetWindowThreadProcessId(foreground, out pid);
                string app = MessengerIdByPid(pid);
                if (app == null) return;

                Win32Rect rect;
                if (!GetWindowRect(foreground, out rect)) return;
                if (IsParkedRect(rect)) return;
                if (rect.Right - rect.Left < 400 || rect.Bottom - rect.Top < 300) return;

                IntPtr previous;
                if (messengerKnownHwnd.TryGetValue(app, out previous) && previous == foreground) return;
                messengerKnownHwnd[app] = foreground;
                MessengerLog("learned " + app + " hwnd=" + foreground);
            }
            catch (Exception) { }
        }

        static string MessengerIdByPid(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    string name = process.ProcessName;
                    foreach (string candidate in WeChatApp.ProcessNames)
                        if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return "wechat";
                    foreach (string candidate in QqApp.ProcessNames)
                        if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return "qq";
                }
            }
            catch (Exception) { }
            return null;
        }

        static bool IsParkedRect(Win32Rect rect)
        {
            if (rect.Left <= -30000 || rect.Top <= -30000) return true;
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            return w < 300 || h < 200;
        }

        IntPtr FindMessengerWindow(MessengerApp app)
        {
            var pids = new HashSet<int>();
            foreach (string name in app.ProcessNames)
            {
                try
                {
                    foreach (Process process in Process.GetProcessesByName(name)) pids.Add(process.Id);
                }
                catch (Exception) { }
            }
            if (pids.Count == 0) return IntPtr.Zero;

            IntPtr bestVisible = IntPtr.Zero;
            long bestVisibleArea = 0;
            IntPtr bestAny = IntPtr.Zero;
            long bestAnyArea = 0;

            EnumWindows(delegate(IntPtr hwnd, IntPtr param)
            {
                int pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (!pids.Contains(pid)) return true;

                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, 256);
                if (app.ClassHint != null &&
                    cls.ToString().IndexOf(app.ClassHint, StringComparison.OrdinalIgnoreCase) < 0) return true;

                Win32Rect rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > bestAnyArea) { bestAnyArea = area; bestAny = hwnd; }
                if (IsWindowVisible(hwnd) && !IsParkedRect(rect) && area > bestVisibleArea)
                {
                    bestVisibleArea = area;
                    bestVisible = hwnd;
                }
                return true;
            }, IntPtr.Zero);

            return bestVisible != IntPtr.Zero ? bestVisible : bestAny;
        }

        void ForceForeground(IntPtr hwnd)
        {
            ShowWindow(hwnd, SwShowNormal);

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = 0;
            if (foreground != IntPtr.Zero)
            {
                int ignored;
                foregroundThread = (uint)GetWindowThreadProcessId(foreground, out ignored);
            }
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);
                SetForegroundWindow(hwnd);
                if (GetForegroundWindow() != hwnd) SwitchToThisWindow(hwnd, true);
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        static byte VirtualKeyOf(string token)
        {
            if (token.Length == 1 && char.IsLetterOrDigit(token[0])) return (byte)char.ToUpperInvariant(token[0]);
            if (token.Length >= 2 && token.Length <= 3 && token[0] == 'f')
            {
                int n;
                if (int.TryParse(token.Substring(1), out n) && n >= 1 && n <= 12) return (byte)(0x70 + n - 1);
            }
            switch (token)
            {
                case "space": return 0x20;
                case "tab": return 0x09;
                case "enter": return 0x0D;
                case "esc": return 0x1B;
            }
            return 0;
        }

        static void SendHotkey(string combo)
        {
            if (string.IsNullOrEmpty(combo)) return;
            var modifiers = new List<byte>();
            byte main = 0;
            foreach (string raw in combo.ToLowerInvariant().Split('+'))
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;
                if (token == "ctrl" || token == "control") modifiers.Add(0x11);
                else if (token == "alt") modifiers.Add(0x12);
                else if (token == "shift") modifiers.Add(0x10);
                else if (token == "win") modifiers.Add(0x5B);
                else main = VirtualKeyOf(token);
            }
            if (main == 0) return;

            foreach (byte modifier in modifiers) keybd_event(modifier, 0, 0, UIntPtr.Zero);
            keybd_event(main, 0, 0, UIntPtr.Zero);
            keybd_event(main, 0, KeyEventKeyUp, UIntPtr.Zero);
            for (int i = modifiers.Count - 1; i >= 0; i--) keybd_event(modifiers[i], 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        static string ReadMessengerHotkey(string appId)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "messenger.txt");
                if (!File.Exists(path)) return null;
                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == '#') continue;
                    int equals = text.IndexOf('=');
                    if (equals <= 0) continue;
                    if (text.Substring(0, equals).Trim().ToLowerInvariant() != appId + "_hotkey") continue;
                    return text.Substring(equals + 1).Trim();
                }
            }
            catch (Exception) { }
            return null;
        }

        // 点提示：窗口已在桌面就切前台；在托盘就发客户端热键让它自己弹出来
        void OpenMessengerApp(string id)
        {
            MessengerApp app = MessengerById(id);

            IntPtr hwnd;
            if (!messengerKnownHwnd.TryGetValue(app.Id, out hwnd) || hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                hwnd = FindMessengerWindow(app);
            }

            Win32Rect rect;
            bool usable = hwnd != IntPtr.Zero && IsWindowVisible(hwnd) &&
                          GetWindowRect(hwnd, out rect) && !IsParkedRect(rect);
            if (usable)
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                ForceForeground(hwnd);
                MessengerLog("open " + id + " -> foreground hwnd=" + hwnd);
                return;
            }

            string hotkey = ReadMessengerHotkey(app.Id);
            if (!string.IsNullOrEmpty(hotkey))
            {
                MessengerLog("open " + id + " -> send hotkey " + hotkey);
                SendHotkey(hotkey);
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                retry.Tick += delegate
                {
                    retry.Stop();
                    IntPtr again = FindMessengerWindow(app);
                    Win32Rect againRect;
                    if (again != IntPtr.Zero && IsWindowVisible(again) &&
                        GetWindowRect(again, out againRect) && !IsParkedRect(againRect))
                    {
                        ForceForeground(again);
                        MessengerLog("hotkey opened " + id + " hwnd=" + again);
                    }
                };
                retry.Start();
                return;
            }

            MessengerLog("open " + id + " -> in tray, no hotkey");
        }
    }
}
