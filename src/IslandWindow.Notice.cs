using System;
using System.Collections.Generic;
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
        const double CapsuleNoticeWidth = 208;
        const double CapsuleIdleWidth = 148;
        const int NoticeHoldMs = 0;   // 0 = 常驻，等点击/查看消息后消失

        Grid capsuleNotice;
        TextBlock capsuleNoticeText;
        Border capsuleNoticeDot;
        DispatcherTimer capsuleNoticeTimer;
        bool capsuleNoticeActive;
        string capsuleNoticeApp = "wechat";
        AudioWatcher audioWatcher;

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
            }
            catch (Exception ex)
            {
                MessengerLog("audio watcher failed: " + ex.Message);
            }

            Closed += delegate
            {
                if (audioWatcher != null) audioWatcher.Dispose();
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

        void ShowCapsuleNotice(string appId)
        {
            if (capsuleNotice == null || expanded) return;
            capsuleNoticeApp = appId == "qq" ? "qq" : "wechat";
            bool isQq = capsuleNoticeApp == "qq";

            capsuleNoticeDot.Background = new SolidColorBrush(isQq
                ? Color.FromRgb(18, 183, 245)
                : Color.FromRgb(7, 193, 96));
            capsuleNoticeText.Text = (isQq ? "QQ" : "微信") + " 有新消息";

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
