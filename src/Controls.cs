using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Brighter
{
    /// <summary>Custom-drawn 0-100 slider with a gradient fill. Mouse, wheel and keyboard aware.</summary>
    internal sealed class BrightnessSlider : FrameworkElement
    {
        private const double ThumbRadius = 9;
        private const double TrackHeight = 8;

        private double _value;
        private bool _dragging;
        private bool _hover;

        public event EventHandler ValueChanged;

        public BrightnessSlider()
        {
            Height = 30;
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return (int)Math.Round(_value); }
            set { SetLevel(value, true); }
        }

        /// <summary>Updates the position without raising <see cref="ValueChanged"/>.</summary>
        public void SetSilently(int value)
        {
            SetLevel(value, false);
        }

        private void SetLevel(double value, bool raise)
        {
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            int before = Value;
            _value = value;
            InvalidateVisual();
            if (raise && before != Value)
            {
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 2 * ThumbRadius || height <= 0) return;

            // Invisible rectangle so the whole strip reacts to the mouse.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            double left = ThumbRadius;
            double right = width - ThumbRadius;
            double centerY = height / 2;
            double half = TrackHeight / 2;

            Rect track = new Rect(left - half, centerY - half, (right - left) + TrackHeight, TrackHeight);
            dc.DrawRoundedRectangle(Theme.Track, null, track, half, half);

            double x = left + (right - left) * _value / 100.0;

            LinearGradientBrush fillBrush = new LinearGradientBrush(Theme.AccentStart, Theme.AccentEnd, new Point(track.Left, 0), new Point(track.Right, 0));
            fillBrush.MappingMode = BrushMappingMode.Absolute;
            Rect fill = new Rect(track.Left, track.Top, (x - track.Left) + half, TrackHeight);
            dc.DrawRoundedRectangle(fillBrush, null, fill, half, half);

            Point center = new Point(x, centerY);
            if (_hover || _dragging || IsKeyboardFocused)
                dc.DrawEllipse(Theme.AccentGlow, null, center, ThumbRadius + 5, ThumbRadius + 5);
            dc.DrawEllipse(Theme.Thumb, null, center, ThumbRadius, ThumbRadius);
            dc.DrawEllipse(Theme.Accent, null, center, 3.5, 3.5);
        }

        private void UpdateFromMouse(Point position)
        {
            double left = ThumbRadius;
            double right = ActualWidth - ThumbRadius;
            if (right <= left) return;
            SetLevel((position.X - left) / (right - left) * 100.0, true);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            _dragging = true;
            CaptureMouse();
            UpdateFromMouse(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                UpdateFromMouse(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _dragging = false;
            InvalidateVisual();
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            SetLevel(Value + (e.Delta > 0 ? 5 : -5), true);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = 0;
            if (e.Key == Key.Left || e.Key == Key.Down) step = -1;
            else if (e.Key == Key.Right || e.Key == Key.Up) step = 1;
            else if (e.Key == Key.PageDown) step = -10;
            else if (e.Key == Key.PageUp) step = 10;
            else if (e.Key == Key.Home) step = -100;
            else if (e.Key == Key.End) step = 100;
            if (step != 0)
            {
                SetLevel(Value + step, true);
                e.Handled = true;
            }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            InvalidateVisual();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            InvalidateVisual();
        }
    }

    /// <summary>Flat rounded button used for title-bar glyphs and preset chips.</summary>
    internal sealed class FlatButton : Border
    {
        private readonly Brush _normal;
        private readonly Brush _hover;
        private bool _pressed;

        public event EventHandler Click;

        /// <summary>The text element, for callers that need to change text, colour or alignment.</summary>
        public TextBlock Label { get; private set; }

        public FlatButton(string text, FontFamily font, double fontSize, Brush foreground, Brush normal, Brush hover)
        {
            _normal = normal;
            _hover = hover;
            Background = normal;
            Cursor = Cursors.Hand;

            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontFamily = font;
            label.FontSize = fontSize;
            label.Foreground = foreground;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            Child = label;
            Label = label;
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Background = _hover;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Background = _normal;
            _pressed = false;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _pressed = true;
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_pressed) return;
            _pressed = false;
            e.Handled = true;
            EventHandler handler = Click;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    /// <summary>On/off pill switch.</summary>
    internal sealed class ToggleSwitch : FrameworkElement
    {
        private bool _on;

        public event EventHandler Toggled;

        public ToggleSwitch()
        {
            Width = 40;
            Height = 22;
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public bool IsOn
        {
            get { return _on; }
            set { _on = value; InvalidateVisual(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(_on ? Theme.Accent : Theme.Track, null, new Rect(0, 0, 40, 22), 11, 11);
            dc.DrawEllipse(Theme.Thumb, null, new Point(_on ? 29 : 11, 11), 8, 8);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _on = !_on;
            InvalidateVisual();
            e.Handled = true;
            EventHandler handler = Toggled;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    /// <summary>Click it, press a key combination, done. Backspace/Delete clears, Esc cancels.</summary>
    internal sealed class HotkeyBox : Border
    {
        private readonly TextBlock _label;
        private uint _modifiers;
        private uint _key;

        public event EventHandler Changed;

        public HotkeyBox(uint modifiers, uint key)
        {
            _modifiers = modifiers;
            _key = key;

            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            Background = Theme.Chip;
            BorderBrush = Theme.CardBorder;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(9);
            Padding = new Thickness(10, 4, 10, 5);
            MinWidth = 120;
            VerticalAlignment = VerticalAlignment.Center;

            _label = new TextBlock();
            _label.FontSize = 12;
            _label.Foreground = Theme.Text;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.Text = Describe(_modifiers, _key);
            Child = _label;
        }

        public uint Modifiers
        {
            get { return _modifiers; }
        }

        public uint Key
        {
            get { return _key; }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            e.Handled = true;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            BorderBrush = Theme.Accent;
            _label.Text = "\u2026";
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            BorderBrush = Theme.CardBorder;
            _label.Text = Describe(_modifiers, _key);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            e.Handled = true;

            System.Windows.Input.Key pressed = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            switch (pressed)
            {
                case System.Windows.Input.Key.LeftCtrl:
                case System.Windows.Input.Key.RightCtrl:
                case System.Windows.Input.Key.LeftAlt:
                case System.Windows.Input.Key.RightAlt:
                case System.Windows.Input.Key.LeftShift:
                case System.Windows.Input.Key.RightShift:
                case System.Windows.Input.Key.LWin:
                case System.Windows.Input.Key.RWin:
                    return; // wait for the real key
            }

            if (pressed == System.Windows.Input.Key.Escape)
            {
                Keyboard.ClearFocus();
                return;
            }

            if (pressed == System.Windows.Input.Key.Back || pressed == System.Windows.Input.Key.Delete)
            {
                _modifiers = 0;
                _key = 0;
            }
            else
            {
                ModifierKeys held = Keyboard.Modifiers;
                uint modifiers = 0;
                if ((held & ModifierKeys.Alt) != 0) modifiers |= NativeMethods.MOD_ALT;
                if ((held & ModifierKeys.Control) != 0) modifiers |= NativeMethods.MOD_CONTROL;
                if ((held & ModifierKeys.Shift) != 0) modifiers |= NativeMethods.MOD_SHIFT;
                if ((held & ModifierKeys.Windows) != 0) modifiers |= NativeMethods.MOD_WIN;
                if (modifiers == 0) return; // a bare key would hijack normal typing

                _modifiers = modifiers;
                _key = (uint)KeyInterop.VirtualKeyFromKey(pressed);
            }

            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
            Keyboard.ClearFocus();
        }

        public static string Describe(uint modifiers, uint key)
        {
            if (key == 0) return "\u2014";
            string text = "";
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) text += "Ctrl+";
            if ((modifiers & NativeMethods.MOD_ALT) != 0) text += "Alt+";
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) text += "Shift+";
            if ((modifiers & NativeMethods.MOD_WIN) != 0) text += "Win+";

            System.Windows.Input.Key wpfKey = KeyInterop.KeyFromVirtualKey((int)key);
            if (wpfKey == System.Windows.Input.Key.Up) return text + "\u2191";
            if (wpfKey == System.Windows.Input.Key.Down) return text + "\u2193";
            if (wpfKey == System.Windows.Input.Key.Left) return text + "\u2190";
            if (wpfKey == System.Windows.Input.Key.Right) return text + "\u2192";
            return text + wpfKey;
        }
    }

    /// <summary>The little sun used as the app logo.</summary>
    internal sealed class SunIcon : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;

            Point center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(Theme.Accent, null, center, size * 0.22, size * 0.22);

            Pen pen = new Pen(Theme.Accent, Math.Max(1.5, size * 0.09));
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                dc.DrawLine(pen,
                    new Point(center.X + cos * size * 0.34, center.Y + sin * size * 0.34),
                    new Point(center.X + cos * size * 0.46, center.Y + sin * size * 0.46));
            }
        }
    }
}
