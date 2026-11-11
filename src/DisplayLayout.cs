using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Brighter
{
    /// <summary>Where one display sits on the Windows desktop, in pixels.</summary>
    internal sealed class DisplayPlacement
    {
        public string DeviceName = "";
        public int Label;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Primary;
    }

    /// <summary>Reads and writes the desktop arrangement (what "Display settings > Rearrange" does).</summary>
    internal static class DisplayLayout
    {
        public static List<DisplayPlacement> Read(List<MonitorDevice> devices)
        {
            List<DisplayPlacement> list = new List<DisplayPlacement>();
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                foreach (MonitorDevice device in devices)
                {
                    if (string.IsNullOrEmpty(device.GdiName)) continue;

                    bool seen = false;
                    foreach (DisplayPlacement existing in list)
                    {
                        if (string.Equals(existing.DeviceName, device.GdiName, StringComparison.OrdinalIgnoreCase)) seen = true;
                    }
                    if (seen) continue; // mirrored outputs share one desktop rectangle

                    Clear(buffer);
                    if (!NativeMethods.EnumDisplaySettingsEx(device.GdiName, NativeMethods.ENUM_CURRENT_SETTINGS, buffer, 0)) continue;

                    DisplayPlacement placement = new DisplayPlacement();
                    placement.DeviceName = device.GdiName;
                    placement.Label = device.Index;
                    placement.X = Marshal.ReadInt32(buffer, NativeMethods.DevModePositionXOffset);
                    placement.Y = Marshal.ReadInt32(buffer, NativeMethods.DevModePositionYOffset);
                    placement.Width = Marshal.ReadInt32(buffer, NativeMethods.DevModeWidthOffset);
                    placement.Height = Marshal.ReadInt32(buffer, NativeMethods.DevModeHeightOffset);
                    placement.Primary = device.IsPrimary;
                    if (placement.Width > 0 && placement.Height > 0)
                        list.Add(placement);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return list;
        }

        /// <summary>Applies the arrangement. Returns false when Windows rejects it.</summary>
        public static bool Apply(List<DisplayPlacement> placements)
        {
            if (placements.Count == 0) return false;

            // Windows requires the primary display at (0,0); shift everything accordingly.
            DisplayPlacement primary = placements[0];
            foreach (DisplayPlacement placement in placements)
            {
                if (placement.Primary) primary = placement;
            }
            int offsetX = primary.X;
            int offsetY = primary.Y;

            List<DisplayPlacement> ordered = new List<DisplayPlacement>();
            ordered.Add(primary);
            foreach (DisplayPlacement placement in placements)
            {
                if (placement != primary) ordered.Add(placement);
            }

            bool ok = true;
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                foreach (DisplayPlacement placement in ordered)
                {
                    Clear(buffer);
                    Marshal.WriteInt32(buffer, NativeMethods.DevModeFieldsOffset, NativeMethods.DM_POSITION);
                    Marshal.WriteInt32(buffer, NativeMethods.DevModePositionXOffset, placement.X - offsetX);
                    Marshal.WriteInt32(buffer, NativeMethods.DevModePositionYOffset, placement.Y - offsetY);

                    uint flags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET;
                    if (placement == primary) flags |= NativeMethods.CDS_SET_PRIMARY;
                    int staged = NativeMethods.ChangeDisplaySettingsEx(placement.DeviceName, buffer, IntPtr.Zero, flags, IntPtr.Zero);
                    Log.Write("layout: " + placement.DeviceName + " -> (" + (placement.X - offsetX) + "," + (placement.Y - offsetY) + ")" + (placement == primary ? " primary" : "") + ", result " + staged);
                    if (staged != 0) ok = false;
                }
                // Commit everything staged above in one go.
                int committed = NativeMethods.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
                Log.Write("layout: commit result " + committed);
                if (committed != 0) ok = false;
            }
            catch (Exception error)
            {
                Log.Error("layout", error);
                ok = false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return ok;
        }

        private static void Clear(IntPtr buffer)
        {
            for (int i = 0; i < NativeMethods.SizeOfDevMode; i++)
                Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteInt16(buffer, NativeMethods.DevModeSizeOffset, (short)NativeMethods.SizeOfDevMode);
        }
    }

    /// <summary>Drag-and-drop picture of the desktop: one box per display, snapping edge to edge.</summary>
    internal sealed class LayoutEditor : Canvas
    {
        private readonly List<DisplayPlacement> _items;
        private readonly List<Border> _boxes = new List<Border>();
        private double _scale = 0.05;
        private double _originX;
        private double _originY;
        private int _minX;
        private int _minY;
        private int _dragIndex = -1;
        private int _selected;
        private Point _dragStart;
        private int _startX;
        private int _startY;

        public LayoutEditor(List<DisplayPlacement> items)
        {
            _items = items;
            Height = 170;
            ClipToBounds = true;
            Background = Brushes.Transparent;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Primary) _selected = i;
                Border box = CreateBox(i);
                _boxes.Add(box);
                Children.Add(box);
            }
            SizeChanged += delegate { Fit(); };
        }

        public List<DisplayPlacement> Items
        {
            get { return _items; }
        }

        /// <summary>Makes the selected display the primary one (applied together with the positions).</summary>
        public void MakeSelectedPrimary()
        {
            if (_selected < 0 || _selected >= _items.Count) return;
            for (int i = 0; i < _items.Count; i++)
                _items[i].Primary = i == _selected;
            UpdateBoxes();
        }

        private Border CreateBox(int index)
        {
            TextBlock number = new TextBlock();
            number.FontSize = 15;
            number.FontWeight = FontWeights.SemiBold;
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;

            Border box = new Border();
            box.CornerRadius = new CornerRadius(6);
            box.BorderThickness = new Thickness(2);
            box.Cursor = Cursors.SizeAll;
            box.Child = number;

            box.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                _dragIndex = index;
                _selected = index;
                _dragStart = e.GetPosition(this);
                _startX = _items[index].X;
                _startY = _items[index].Y;
                box.CaptureMouse();
                Panel.SetZIndex(box, 10);
                UpdateBoxes();
                e.Handled = true;
            };
            box.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (_dragIndex != index) return;
                Point now = e.GetPosition(this);
                _items[index].X = _startX + (int)Math.Round((now.X - _dragStart.X) / _scale);
                _items[index].Y = _startY + (int)Math.Round((now.Y - _dragStart.Y) / _scale);
                UpdateBoxes();
            };
            box.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_dragIndex != index) return;
                _dragIndex = -1;
                box.ReleaseMouseCapture();
                Panel.SetZIndex(box, 0);
                Snap(index);
                if (Overlaps(index))
                {
                    _items[index].X = _startX;
                    _items[index].Y = _startY;
                }
                Fit();
                e.Handled = true;
            };
            return box;
        }

        /// <summary>Recomputes the zoom so the whole arrangement fits with room to drag around.</summary>
        private void Fit()
        {
            if (_items.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (DisplayPlacement item in _items)
            {
                minX = Math.Min(minX, item.X);
                minY = Math.Min(minY, item.Y);
                maxX = Math.Max(maxX, item.X + item.Width);
                maxY = Math.Max(maxY, item.Y + item.Height);
            }
            double width = Math.Max(1, maxX - minX);
            double height = Math.Max(1, maxY - minY);

            _scale = Math.Min(ActualWidth / (width * 1.5), ActualHeight / (height * 1.6));
            _minX = minX;
            _minY = minY;
            _originX = (ActualWidth - width * _scale) / 2;
            _originY = (ActualHeight - height * _scale) / 2;
            UpdateBoxes();
        }

        private void UpdateBoxes()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                DisplayPlacement item = _items[i];
                Border box = _boxes[i];
                box.Width = Math.Max(4, item.Width * _scale - 2);
                box.Height = Math.Max(4, item.Height * _scale - 2);
                SetLeft(box, _originX + (item.X - _minX) * _scale + 1);
                SetTop(box, _originY + (item.Y - _minY) * _scale + 1);
                box.Background = i == _selected ? Theme.ChipHover : Theme.Card;
                box.BorderBrush = i == _selected ? Theme.Accent : Theme.Muted;

                TextBlock number = (TextBlock)box.Child;
                number.Text = item.Primary ? item.Label + " \u2605" : item.Label.ToString();
                number.Foreground = i == _selected ? Theme.Accent : Theme.Text;
            }
        }

        /// <summary>Moves the dropped display so it touches the nearest other display, edge to edge.</summary>
        private void Snap(int index)
        {
            DisplayPlacement moved = _items[index];
            DisplayPlacement anchor = null;
            double best = double.MaxValue;
            double movedCenterX = moved.X + moved.Width / 2.0;
            double movedCenterY = moved.Y + moved.Height / 2.0;

            for (int i = 0; i < _items.Count; i++)
            {
                if (i == index) continue;
                DisplayPlacement other = _items[i];
                double dx = movedCenterX - (other.X + other.Width / 2.0);
                double dy = movedCenterY - (other.Y + other.Height / 2.0);
                double distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    anchor = other;
                }
            }
            if (anchor == null) return;

            double normalX = (movedCenterX - (anchor.X + anchor.Width / 2.0)) / ((anchor.Width + moved.Width) / 2.0);
            double normalY = (movedCenterY - (anchor.Y + anchor.Height / 2.0)) / ((anchor.Height + moved.Height) / 2.0);

            if (Math.Abs(normalX) >= Math.Abs(normalY))
            {
                // left or right of the anchor
                moved.X = normalX >= 0 ? anchor.X + anchor.Width : anchor.X - moved.Width;
                moved.Y = Clamp(moved.Y, anchor.Y - moved.Height + 1, anchor.Y + anchor.Height - 1);
                int tolerance = anchor.Height / 10;
                if (Math.Abs(moved.Y - anchor.Y) <= tolerance)
                    moved.Y = anchor.Y;
                else if (Math.Abs((moved.Y + moved.Height) - (anchor.Y + anchor.Height)) <= tolerance)
                    moved.Y = anchor.Y + anchor.Height - moved.Height;
            }
            else
            {
                // above or below the anchor
                moved.Y = normalY >= 0 ? anchor.Y + anchor.Height : anchor.Y - moved.Height;
                moved.X = Clamp(moved.X, anchor.X - moved.Width + 1, anchor.X + anchor.Width - 1);
                int tolerance = anchor.Width / 10;
                if (Math.Abs(moved.X - anchor.X) <= tolerance)
                    moved.X = anchor.X;
                else if (Math.Abs((moved.X + moved.Width) - (anchor.X + anchor.Width)) <= tolerance)
                    moved.X = anchor.X + anchor.Width - moved.Width;
            }
        }

        private bool Overlaps(int index)
        {
            DisplayPlacement a = _items[index];
            for (int i = 0; i < _items.Count; i++)
            {
                if (i == index) continue;
                DisplayPlacement b = _items[i];
                bool separate = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                if (!separate) return true;
            }
            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
