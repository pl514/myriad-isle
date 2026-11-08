using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 胶囊「来消息提示」（普通版）。
    //   微信/QQ 收到消息 → 胶囊变宽、眼睛淡出、显示「微信 有新消息」→ 2 秒后复原
    //   提示期间点胶囊 → 打开对应客户端（见 IslandWindow.Messenger.cs）
    //
    // 触发信号：客户端毫秒提示音的音频会话峰值（系统通知读不到，已实测）。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        string pendingNoticeApp = "";
        bool unreadWechat;
        DateTime suppressWechatUntil;
        DateTime suppressQqUntil;
        bool unreadQq;

        Border panelNoticeChip;
        Border panelNoticeDot;
        TextBlock panelNoticeText;
        string panelNoticeApp;

        FrameworkElement BuildPanelNoticeChip()
        {
            panelNoticeDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(7, 193, 96)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            panelNoticeText = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 236, 246)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(panelNoticeDot);
            row.Children.Add(panelNoticeText);

            panelNoticeChip = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(230, 34, 38, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 12, 5),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = row,
                ToolTip = "左键：微信　右键：QQ"
            };
            // 两家都有未读：左键微信、右键 QQ；只有一家未读：左右键都打开那一家
            panelNoticeChip.MouseLeftButtonUp += delegate
            {
                if (unreadWechat) { OpenMessengerApp("wechat"); unreadWechat = false; }
                else if (unreadQq) { OpenMessengerApp("qq"); unreadQq = false; }
                SyncPendingNotice();
                RefreshPanelNoticeChip();
            };
            panelNoticeChip.MouseRightButtonUp += delegate
            {
                if (unreadQq) { OpenMessengerApp("qq"); unreadQq = false; }
                else if (unreadWechat) { OpenMessengerApp("wechat"); unreadWechat = false; }
                SyncPendingNotice();
                RefreshPanelNoticeChip();
            };
            return panelNoticeChip;
        }

        void ShowPanelNoticeChip(string appId)
        {
            if (panelNoticeChip == null) return;
            if (appId == "qq") unreadQq = true; else unreadWechat = true;
            pendingNoticeApp = appId == "qq" ? "qq" : "wechat";
            RefreshPanelNoticeChip();
        }

        // 按未读集合刷新：单个显示名字，两个都显示"微信/QQ"
        void SyncPendingNotice()
        {
            if (unreadQq) pendingNoticeApp = "qq";
            else if (unreadWechat) pendingNoticeApp = "wechat";
            else pendingNoticeApp = null;
        }

        void RefreshPanelNoticeChip()
        {
            if (panelNoticeChip == null) return;
            if (!unreadWechat && !unreadQq)
            {
                panelNoticeChip.Visibility = Visibility.Collapsed;
                return;
            }

            if (unreadWechat && unreadQq)
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(7, 193, 96));
                panelNoticeText.Text = "微信/QQ 未读消息";
            }
            else if (unreadQq)
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(18, 183, 245));
                panelNoticeText.Text = "QQ 未读消息";
            }
            else
            {
                panelNoticeDot.Background = new SolidColorBrush(Color.FromRgb(7, 193, 96));
                panelNoticeText.Text = "微信 未读消息";
            }

            panelNoticeChip.Visibility = Visibility.Visible;
            panelNoticeChip.IsHitTestVisible = true;
        }

        // 只是把胶囊收起来（比如收起面板、展开功能球），未读状态保留
        void HidePanelNoticeChip()
        {
            if (panelNoticeChip == null) return;
            panelNoticeChip.Visibility = Visibility.Collapsed;
            panelNoticeApp = null;
        }

        const double CapsuleNoticeWidth = 208;
        const double CapsuleIdleWidth = 148;
        const int NoticeHoldMs = 2000;

        Grid capsuleNotice;
        TextBlock capsuleNoticeText;
        Border capsuleNoticeDot;
        DispatcherTimer capsuleNoticeTimer;
        bool capsuleNoticeActive;
        string capsuleNoticeApp = "wechat";
        AudioWatcher audioWatcher;
        DshWatcher dshWatcher;

        void AttachCapsuleNotice()
        {
            if (capsule == null || capsuleNotice != null) return;
            Grid root = capsule.Child as Grid;
            if (root == null) return;

            var grid = new Grid
            {
                Visibility = Visibility.Collapsed,
                Opacity = 0,
                IsHitTestVisible = true,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            capsuleNoticeDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(7, 193, 96))
            };
            row.Children.Add(capsuleNoticeDot);

            capsuleNoticeText = new TextBlock
            {
                Text = "有新消息",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(236, 240, 248)),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(capsuleNoticeText);
            grid.Children.Add(row);

            grid.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenMessengerApp(capsuleNoticeApp);
                HideCapsuleNotice();
            };

            capsuleNotice = grid;
            root.Children.Add(capsuleNotice);

            StartMessengerLearning();

            try
            {
                audioWatcher = new AudioWatcher(delegate(string appId)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        MessengerLog("audio trigger " + appId);
                        if (!capsuleNoticeActive) ShowCapsuleNotice(appId);
                    }), DispatcherPriority.Background);
                });
                audioWatcher.Start();

            // DSH 干活结束 → 胶囊提醒
            try
            {
                dshWatcher = new DshWatcher(delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        MessengerLog("dsh finished");
                        ShowCapsuleNotice("dsh");
                    }), DispatcherPriority.Background);
                });
                dshWatcher.Start();
            }
            catch (Exception) { }
            }
            catch (Exception ex)
            {
                MessengerLog("audio watcher failed: " + ex.Message);
            }

            Closed += delegate
            {
                if (audioWatcher != null) audioWatcher.Dispose();
        if (dshWatcher != null) dshWatcher.Dispose();
            };
        }

        bool IsCapsuleNoticeSource(object source)
        {
            if (!capsuleNoticeActive || capsuleNotice == null) return false;
            DependencyObject node = source as DependencyObject;
            int guard = 0;
            while (node != null && guard++ < 32)
            {
                if (node == capsuleNotice) return true;
                if (node == capsule) return false;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        void AnimateCapsuleWidth(double to)
        {
            if (capsule == null) return;
            Motion.AnimateDouble(capsule, FrameworkElement.WidthProperty, capsule.Width, to, 220, Motion.Enter, 0, null);
        }

        // 该客户端是否在前台：我们自己切过去时的提示音不算新消息
        bool IsMessengerForeground(string appId)
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                int pid;
                GetWindowThreadProcessId(foreground, out pid);
                return MessengerIdByPid(pid) == appId;
            }
            catch (Exception) { return false; }
        }

        // 跳转后的几秒内忽略该客户端的音效（切窗口/登录音会被误判成新消息）
        void SuppressApp(string appId, int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            if (appId == "qq") suppressQqUntil = until; else suppressWechatUntil = until;
        }

        bool IsSuppressed(string appId)
        {
            return appId == "qq" ? DateTime.UtcNow < suppressQqUntil : DateTime.UtcNow < suppressWechatUntil;
        }

        void ShowCapsuleNotice(string appId)
        {
            if (capsuleNotice == null) return;
            if (appId != "dsh")
            {
                if (IsSuppressed(appId == "qq" ? "qq" : "wechat")) return;
                if (IsMessengerForeground(appId == "qq" ? "qq" : "wechat")) return;
            }
            if (expanded)
            {
                if (appId == "qq") unreadQq = true; else unreadWechat = true;
                pendingNoticeApp = appId == "qq" ? "qq" : "wechat";
                RefreshPanelNoticeChip();
                return;
            }
            capsuleNoticeApp = appId == "qq" ? "qq" : (appId == "dsh" ? "dsh" : "wechat");
            pendingNoticeApp = capsuleNoticeApp;   // 记成未读，打开面板后还能看到
            if (capsuleNoticeApp != "dsh") { if (capsuleNoticeApp == "qq") unreadQq = true; else unreadWechat = true; }
            bool isQq = capsuleNoticeApp == "qq";
            bool isDsh = capsuleNoticeApp == "dsh";

            capsuleNoticeDot.Background = new SolidColorBrush(isDsh
                ? Color.FromRgb(124, 108, 242)
                : isQq ? Color.FromRgb(18, 183, 245) : Color.FromRgb(7, 193, 96));
            capsuleNoticeText.Text = isDsh ? "DSH 任务完成" : (isQq ? "QQ" : "微信") + " 有新消息";

            capsuleNoticeActive = true;
            capsuleNotice.Visibility = Visibility.Visible;
            capsuleNotice.IsHitTestVisible = true;
            Motion.AnimateDouble(capsuleNotice, UIElement.OpacityProperty, 0, 1, 140, Motion.Enter, 0, null);

            if (capsuleEyes != null && capsuleEyes.Visibility == Visibility.Visible)
            {
                UIElement eyes = capsuleEyes;
                Motion.AnimateDouble(eyes, UIElement.OpacityProperty, 1, 0, 110, Motion.Exit, 0, delegate
                {
                    if (capsuleNoticeActive) eyes.Visibility = Visibility.Collapsed;
                });
            }

            AnimateCapsuleWidth(CapsuleNoticeWidth);

            if (capsuleNoticeTimer == null)
            {
                capsuleNoticeTimer = new DispatcherTimer();
                capsuleNoticeTimer.Tick += delegate
                {
                    capsuleNoticeTimer.Stop();
                    HideCapsuleNotice();
                };
            }
            capsuleNoticeTimer.Stop();
            capsuleNoticeTimer.Interval = TimeSpan.FromMilliseconds(NoticeHoldMs);
            capsuleNoticeTimer.Start();
        }


        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        // 低权限进程读不到高权限窗口的标题（GetWindowText 返回空），所以改成
        // 类名 Chrome_WidgetWin_1 + 进程可执行文件路径来识别 DSH。
        static string ProcessImagePath(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return "";
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) return "";
                return buffer.ToString(0, size);
            }
            finally { CloseHandle(handle); }
        }

        bool OpenDshWindow()
        {
            IntPtr found = IntPtr.Zero;
            string why = "";
            try
            {
                EnumWindows(delegate(IntPtr hwnd, IntPtr param)
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    var cls = new StringBuilder(256);
                    if (GetClassName(hwnd, cls, 256) <= 0) return true;
                    string clsName = cls.ToString();
                    if (clsName.IndexOf("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) < 0) return true;

                    int pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    string image = ProcessImagePath(pid);
                    why += " | " + clsName + "/" + pid + "/" + System.IO.Path.GetFileName(image);

                    var title = new StringBuilder(256);
                    GetWindowText(hwnd, title, 256);
                    string text = title.ToString();

                    bool byImage = image.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0
                                   || image.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool byTitle = text.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!byImage && !byTitle) return true;

                    found = hwnd;
                    return false;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { MessengerLog("dsh enum failed: " + ex.Message); return false; }

            if (found == IntPtr.Zero)
            {
                MessengerLog("dsh window not found. candidates:" + why);
                return false;
            }
            try
            {
                if (IsIconic(found)) ShowWindow(found, 9);
                SetForegroundWindow(found);
                SwitchToThisWindow(found, true);
                MessengerLog("dsh window activated");
                return true;
            }
            catch (Exception ex) { MessengerLog("dsh activate failed: " + ex.Message); return false; }
        }

        void ClearUnreadNotice()
        {
            pendingNoticeApp = null;
            unreadWechat = false;
            unreadQq = false;
            HidePanelNoticeChip();
            if (capsuleNoticeActive) HideCapsuleNotice();
        }

        void HideCapsuleNotice()
        {
            if (capsuleNotice == null) return;
            capsuleNoticeActive = false;
            if (capsuleNoticeTimer != null) capsuleNoticeTimer.Stop();

            Motion.AnimateDouble(capsuleNotice, UIElement.OpacityProperty, 1, 0, 120, Motion.Exit, 0, delegate
            {
                if (!capsuleNoticeActive && capsuleNotice != null) capsuleNotice.Visibility = Visibility.Collapsed;
            });

            AnimateCapsuleWidth(CapsuleIdleWidth);

            if (capsuleEyes != null)
            {
                capsuleEyes.Visibility = Visibility.Visible;
                Motion.AnimateDouble(capsuleEyes, UIElement.OpacityProperty, 0, 1, 170, Motion.Enter, 80, null);
            }
        }
    }
}
