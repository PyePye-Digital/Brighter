using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Brighter
{
    /// <summary>Small "name + level" toast shown when a shortcut changes the brightness.</summary>
    internal sealed class Osd
    {
        private const double BarWidth = 220;

        private readonly Window _window;
        private readonly TextBlock _name;
        private readonly TextBlock _value;
        private readonly Border _fill;
        private readonly DispatcherTimer _timer = new DispatcherTimer();

        public Osd()
        {
            _name = new TextBlock();
            _name.FontSize = 13;
            _name.Foreground = Theme.Text;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.MaxWidth = 160;
            _name.HorizontalAlignment = HorizontalAlignment.Left;

            _value = new TextBlock();
            _value.FontSize = 13;
            _value.FontWeight = FontWeights.SemiBold;
            _value.Foreground = Theme.Accent;
            _value.HorizontalAlignment = HorizontalAlignment.Right;

            Grid top = new Grid();
            top.Children.Add(_name);
            top.Children.Add(_value);

            Border track = new Border();
            track.Width = BarWidth;
            track.Height = 6;
            track.CornerRadius = new CornerRadius(3);
            track.Background = Theme.Track;
            track.Margin = new Thickness(0, 9, 0, 0);

            _fill = new Border();
            _fill.Height = 6;
            _fill.CornerRadius = new CornerRadius(3);
            _fill.Background = Theme.Accent;
            _fill.HorizontalAlignment = HorizontalAlignment.Left;
            _fill.Margin = new Thickness(0, 9, 0, 0);

            Grid bar = new Grid();
            bar.Width = BarWidth;
            bar.Children.Add(track);
            bar.Children.Add(_fill);

            StackPanel content = new StackPanel();
            content.Children.Add(top);
            content.Children.Add(bar);

            Border box = new Border();
            box.Background = Theme.Card;
            box.BorderBrush = Theme.CardBorder;
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(14);
            box.Padding = new Thickness(18, 13, 18, 15);
            box.Child = content;

            _window = new Window();
            _window.WindowStyle = WindowStyle.None;
            _window.AllowsTransparency = true;
            _window.Background = Brushes.Transparent;
            _window.ShowInTaskbar = false;
            _window.ShowActivated = false;
            _window.Topmost = true;
            _window.Focusable = false;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.SizeToContent = SizeToContent.WidthAndHeight;
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.FontFamily = Theme.Ui;
            _window.Content = box;
            _window.SourceInitialized += delegate { OverlayStyles.MakeClickThrough(_window); };

            _timer.Interval = TimeSpan.FromSeconds(1.4);
            _timer.Tick += delegate
            {
                _timer.Stop();
                _window.Hide();
            };
        }

        public void Show(string name, int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            _name.Text = name;
            _value.Text = percent + "%";
            _fill.Width = BarWidth * percent / 100.0;

            if (!_window.IsVisible) _window.Show();
            _window.UpdateLayout();
            Rect area = SystemParameters.WorkArea;
            _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
            _window.Top = area.Bottom - _window.ActualHeight - 48;

            _timer.Stop();
            _timer.Start();
        }

        public void Close()
        {
            _timer.Stop();
            _window.Close();
        }
    }

    /// <summary>
    /// Software dimming for a monitor that has no hardware brightness control: a black, click-through,
    /// always-on-top window over that monitor whose opacity is the "darkness".
    /// </summary>
    internal sealed class DimOverlay
    {
        private const double MaxOpacity = 0.85; // never fully black, the screen must stay usable

        private readonly Window _window;
        private readonly NativeMethods.RECT _bounds;

        public DimOverlay(NativeMethods.RECT bounds)
        {
            _bounds = bounds;
            _window = new Window();
            _window.WindowStyle = WindowStyle.None;
            _window.AllowsTransparency = true;
            _window.Background = Brushes.Black;
            _window.Opacity = 0;
            _window.ShowInTaskbar = false;
            _window.ShowActivated = false;
            _window.Topmost = true;
            _window.Focusable = false;
            _window.IsHitTestVisible = false;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Width = 200;
            _window.Height = 200;
            _window.SourceInitialized += delegate
            {
                OverlayStyles.MakeClickThrough(_window);
                Place();
            };
        }

        public bool Covers(NativeMethods.RECT bounds)
        {
            return bounds.Left == _bounds.Left && bounds.Top == _bounds.Top && bounds.Right == _bounds.Right && bounds.Bottom == _bounds.Bottom;
        }

        /// <summary>100 = no dimming, 0 = darkest.</summary>
        public void SetLevel(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            double opacity = (100 - percent) / 100.0 * MaxOpacity;
            if (opacity < 0.005)
            {
                if (_window.IsVisible) _window.Hide();
                return;
            }
            _window.Opacity = opacity;
            if (!_window.IsVisible) _window.Show();
            Place();
        }

        // Positioned with raw pixels through Win32 so mixed-DPI setups land on the right monitor.
        private void Place()
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;
            NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST,
                _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top,
                NativeMethods.SWP_NOACTIVATE);
        }

        public void Close()
        {
            _window.Close();
        }
    }

    internal static class OverlayStyles
    {
        /// <summary>Mouse clicks pass through, the window never takes focus and stays out of Alt+Tab.</summary>
        public static void MakeClickThrough(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        }
    }
}
