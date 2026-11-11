using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Brighter
{
    /// <summary>The sliders belonging to one monitor.</summary>
    internal sealed class MonitorCard
    {
        public MonitorDevice Device;
        public BrightnessSlider Brightness;
        public BrightnessSlider Contrast;    // null when the monitor has no contrast control
        public BrightnessSlider Volume;      // null when the monitor has no speakers / volume control
        public BrightnessSlider Temperature;
        public int Base;                     // brightness when a relative master drag started
    }

    /// <summary>
    /// The main window: monitor cards, tray behaviour, shortcuts and the background automations.
    /// The settings screen lives in MainWindow.Settings.cs. Everything is built in code (no XAML)
    /// so the project compiles with the csc.exe that ships with Windows.
    /// </summary>
    internal sealed partial class MainWindow : Window
    {
        private const int HotkeyUp = 1;
        private const int HotkeyDown = 2;
        private const int HotkeyAllUp = 3;
        private const int HotkeyAllDown = 4;
        private const int ProfileHotkeyBase = 100;

        private readonly AppSettings _settings;
        private readonly StackPanel _monitorPanel = new StackPanel();
        private readonly StackPanel _settingsPanel = new StackPanel();
        private readonly Border _layoutHost = new Border();
        private readonly TextBlock _status;
        private readonly TextBlock _title;
        private readonly List<MonitorCard> _cards = new List<MonitorCard>();
        private readonly List<int> _hotkeyIds = new List<int>();
        private readonly Dictionary<string, DimOverlay> _overlays = new Dictionary<string, DimOverlay>();
        private readonly DispatcherTimer _saveTimer = new DispatcherTimer();
        private readonly DispatcherTimer _scheduleTimer = new DispatcherTimer();
        private readonly DispatcherTimer _activityTimer = new DispatcherTimer();
        private readonly int _ownProcessId = Process.GetCurrentProcess().Id;

        private List<MonitorDevice> _devices = new List<MonitorDevice>();
        private HashSet<string> _knownIds = new HashSet<string>();
        private TrayIcon _tray;
        private Osd _osd;
        private HwndSource _source;
        private string _monitorStatus = "";
        private string _lastPeriod;
        private bool _loading;
        private bool _reloadQueued;
        private bool _firstLoad = true;
        private bool _showSettings;
        private bool _exiting;
        private bool _anchored;
        private bool _hotkeysFailed;
        private int _lastAutoHide;

        // "Keep this?" confirmations: a change that can leave the user without a picture is undone
        // automatically unless they confirm it in time.
        private List<DisplayPlacement> _layoutPrevious;
        private int _layoutSeconds;
        private TextBlock _layoutCountdown;
        private Border _inputBanner;
        private TextBlock _inputCountdown;
        private string _inputDeviceId;
        private int _inputPrevious;
        private int _inputSeconds;

        private readonly DispatcherTimer _retryTimer = new DispatcherTimer();
        private int _retryCount;
        private bool _sleptAll;
        private int _sleptAllStamp;
        private string _updateVersion;

        // Automations that change values temporarily must not overwrite what the user chose.
        private bool _transient;
        private bool _idleDimmed;
        private Dictionary<string, int[]> _idleSnapshot;
        private AppRule _activeRule;
        private Dictionary<string, int[]> _ruleSnapshot;
        private IntPtr _lastForeground;

        // Relative master slider bookkeeping.
        private int _masterPrevious;
        private int _masterBase;
        private int _masterStamp;

        public MainWindow()
        {
            _settings = SettingsStore.Load();

            Title = "Brighter";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            FontFamily = Theme.Ui;

            // The shadow lives on its own layer so the effect does not blur the text above it.
            Border shadow = new Border();
            shadow.Margin = new Thickness(18);
            shadow.CornerRadius = new CornerRadius(16);
            shadow.Background = Theme.WindowBg;
            DropShadowEffect effect = new DropShadowEffect();
            effect.BlurRadius = 26;
            effect.ShadowDepth = 5;
            effect.Direction = 270;
            effect.Opacity = 0.5;
            effect.Color = Colors.Black;
            shadow.Effect = effect;

            Border frame = new Border();
            frame.Margin = new Thickness(18);
            frame.CornerRadius = new CornerRadius(16);
            frame.Background = Theme.WindowBg;
            frame.BorderBrush = Theme.CardBorder;
            frame.BorderThickness = new Thickness(1);

            _status = MakeText("", 11.5, Theme.Muted, FontWeights.Normal);
            _status.Margin = new Thickness(20, 0, 20, 14);
            _status.TextTrimming = TextTrimming.CharacterEllipsis;

            _title = MakeText("Brighter", 15, Theme.Text, FontWeights.SemiBold);

            StackPanel host = new StackPanel();
            host.Margin = new Thickness(16, 2, 16, 4);
            host.Children.Add(BuildInputBanner());
            host.Children.Add(_monitorPanel);
            host.Children.Add(_settingsPanel);
            _settingsPanel.Visibility = Visibility.Collapsed;

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.MaxHeight = 640;
            scroll.Focusable = false;
            scroll.Content = host;
            ApplyScrollBarStyle(scroll);
            // Safety net: if the wheel event reaches the window unhandled, scroll the list ourselves.
            MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 2.0);
                e.Handled = true;
            };

            UIElement titleBar = BuildTitleBar();

            DockPanel dock = new DockPanel();
            DockPanel.SetDock(titleBar, Dock.Top);
            dock.Children.Add(titleBar);
            DockPanel.SetDock(_status, Dock.Bottom);
            dock.Children.Add(_status);
            dock.Children.Add(scroll);
            frame.Child = dock;

            Grid root = new Grid();
            root.Children.Add(shadow);
            root.Children.Add(frame);
            Content = root;

            _saveTimer.Interval = TimeSpan.FromSeconds(1.2);
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SettingsStore.Save(_settings);
            };

            _scheduleTimer.Interval = TimeSpan.FromSeconds(30);
            _scheduleTimer.Tick += delegate { CheckSchedule(); };
            _scheduleTimer.Start();

            _retryTimer.Tick += delegate
            {
                _retryTimer.Stop();
                Reload(0);
            };

            _activityTimer.Interval = TimeSpan.FromSeconds(1);
            _activityTimer.Tick += delegate
            {
                TickCountdowns();
                CheckSleepWake();
                CheckIdle();
                CheckForegroundApp();
            };
            _activityTimer.Start();

            try
            {
                _tray = new TrayIcon();
                _tray.Open += delegate { ToggleFromTray(); };
                _tray.Exit += delegate { ExitApp(); };
                _tray.SleepAll += delegate { SleepAll(); };
                _tray.ProfileNames = delegate()
                {
                    List<string> names = new List<string>();
                    foreach (ProfileData profile in _settings.Profiles)
                        names.Add(profile.Name);
                    return names;
                };
                _tray.ProfileChosen += delegate(string chosen)
                {
                    ProfileData profile = FindProfile(chosen);
                    if (profile != null) ApplyProfile(profile);
                };
            }
            catch (Exception)
            {
                _tray = null; // no tray available: the close button will exit instead of hiding
            }

            SizeChanged += delegate
            {
                if (_anchored && IsVisible) Anchor();
            };
            Deactivated += delegate
            {
                // Opened from the tray: behave like a flyout and tuck away when focus goes elsewhere.
                if (_anchored && IsVisible)
                {
                    _lastAutoHide = Environment.TickCount;
                    Hide();
                }
            };
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            BuildSettingsView();
            Reload(0);

            UpdateChecker.CheckAsync(delegate(string newer)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _updateVersion = newer;
                    BuildSettingsView();
                }));
            });
        }

        // ------------------------------------------------------------------ title bar

        private UIElement BuildTitleBar()
        {
            Grid bar = new Grid();
            bar.Height = 54;
            bar.Background = Brushes.Transparent;
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    _anchored = false;
                    DragMove();
                }
            };

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.Margin = new Thickness(20, 0, 0, 0);
            left.IsHitTestVisible = false;

            SunIcon logo = new SunIcon();
            logo.Width = 22;
            logo.Height = 22;
            left.Children.Add(logo);

            _title.Margin = new Thickness(10, 0, 0, 0);
            _title.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(_title);
            bar.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Margin = new Thickness(0, 0, 12, 0);

            FlatButton gear = MakeGlyphButton("\uE713", L.Settings, Theme.ChipHover);
            gear.Click += delegate
            {
                _showSettings = !_showSettings;
                gear.Label.Foreground = _showSettings ? Theme.Accent : Theme.Text;
                UpdateView();
            };
            right.Children.Add(gear);

            FlatButton refresh = MakeGlyphButton("\uE72C", L.Refresh, Theme.ChipHover);
            refresh.Click += delegate { Reload(0); };
            right.Children.Add(refresh);

            FlatButton minimize = MakeGlyphButton("\uE921", L.Minimize, Theme.ChipHover);
            minimize.Click += delegate { WindowState = WindowState.Minimized; };
            right.Children.Add(minimize);

            FlatButton closeButton = MakeGlyphButton("\uE8BB", L.CloseToTray, Theme.CloseHover);
            closeButton.Click += delegate { Close(); };
            right.Children.Add(closeButton);

            bar.Children.Add(right);
            return bar;
        }

        private static FlatButton MakeGlyphButton(string glyph, string tooltip, Brush hover)
        {
            FlatButton button = new FlatButton(glyph, Theme.Icons, 11, Theme.Text, Brushes.Transparent, hover);
            button.Width = 36;
            button.Height = 30;
            button.Margin = new Thickness(2, 0, 0, 0);
            button.CornerRadius = new CornerRadius(8);
            button.ToolTip = tooltip;
            return button;
        }

        private void UpdateView()
        {
            _monitorPanel.Visibility = _showSettings ? Visibility.Collapsed : Visibility.Visible;
            _settingsPanel.Visibility = _showSettings ? Visibility.Visible : Visibility.Collapsed;
            _title.Text = _showSettings ? "Brighter  \u00B7  " + L.Settings : "Brighter";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_showSettings)
                _status.Text = SettingsStore.FilePath;
            else if (_hotkeysFailed)
                _status.Text = _monitorStatus + "  \u00B7  " + L.HotkeysFailed;
            else
                _status.Text = _monitorStatus;
        }

        // ------------------------------------------------------------------ tray / window lifetime

        /// <summary>Called when the exe is started a second time.</summary>
        public void ShowFromOutside()
        {
            _anchored = false;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void ToggleFromTray()
        {
            // Clicking the tray icon first takes focus away (which auto-hides the flyout); do not reopen it.
            if (!IsVisible && unchecked(Environment.TickCount - _lastAutoHide) < 400) return;

            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
                return;
            }
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Anchor();
            Activate();
            _anchored = true;
        }

        /// <summary>Parks the window in the bottom-right corner of the work area, next to the tray.</summary>
        private void Anchor()
        {
            UpdateLayout();
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth + 6;
            Top = area.Bottom - ActualHeight + 6;
        }

        private void ExitApp()
        {
            _exiting = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // The close button only hides the window; "Exit" in the tray menu really quits.
            if (!_exiting && _tray != null)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _scheduleTimer.Stop();
            _activityTimer.Stop();
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
            UnregisterHotkeys();
            GammaRamp.ResetAll();
            CloseOverlays();
            if (_osd != null) _osd.Close();
            if (_tray != null) _tray.Dispose();
            foreach (MonitorDevice device in _devices)
                device.Dispose();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        // ------------------------------------------------------------------ hotkeys

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (_source != null)
            {
                _source.AddHook(WndProc);
                UpdateHotkeys();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                OnHotkey(wParam.ToInt32());
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void UpdateHotkeys()
        {
            if (_source == null) return;
            UnregisterHotkeys();
            _hotkeysFailed = false;
            if (_settings.HotkeysEnabled)
            {
                bool ok = true;
                ok &= RegisterHotkey(HotkeyUp, _settings.KeyUp);
                ok &= RegisterHotkey(HotkeyDown, _settings.KeyDown);
                ok &= RegisterHotkey(HotkeyAllUp, _settings.KeyAllUp);
                ok &= RegisterHotkey(HotkeyAllDown, _settings.KeyAllDown);
                for (int i = 0; i < _settings.Profiles.Count; i++)
                    ok &= RegisterHotkey(ProfileHotkeyBase + i, _settings.Profiles[i].Hotkey);
                _hotkeysFailed = !ok;
                if (!ok) Log.Write("hotkeys: at least one shortcut could not be registered");
            }
            UpdateStatus();
        }

        private bool RegisterHotkey(int id, HotkeyBinding binding)
        {
            if (binding == null || binding.Key == 0) return true;
            if (!NativeMethods.RegisterHotKey(_source.Handle, id, binding.Modifiers, binding.Key)) return false;
            _hotkeyIds.Add(id);
            return true;
        }

        private void UnregisterHotkeys()
        {
            if (_source == null) return;
            foreach (int id in _hotkeyIds)
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            _hotkeyIds.Clear();
        }

        private void OnHotkey(int id)
        {
            if (id >= ProfileHotkeyBase)
            {
                int index = id - ProfileHotkeyBase;
                if (index >= _settings.Profiles.Count) return;
                ProfileData profile = _settings.Profiles[index];
                ApplyProfile(profile);
                ShowOsd(profile.Name, AverageBrightness());
                return;
            }

            bool up = id == HotkeyUp || id == HotkeyAllUp;
            bool all = id == HotkeyAllUp || id == HotkeyAllDown;
            int delta = up ? _settings.HotkeyStep : -_settings.HotkeyStep;

            IntPtr target = IntPtr.Zero;
            if (!all)
            {
                NativeMethods.POINT cursor;
                if (!NativeMethods.GetCursorPos(out cursor)) return;
                target = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }

            MonitorCard shown = null;
            foreach (MonitorCard card in _cards)
            {
                if (!all && card.Device.HMonitor != target) continue;
                card.Brightness.Value = card.Brightness.Value + delta;
                if (shown == null) shown = card;
            }
            if (shown == null) return;

            if (all) ShowOsd(L.AllMonitors, AverageBrightness());
            else ShowOsd(shown.Device.Name, shown.Brightness.Value);
        }

        private void ShowOsd(string name, int percent)
        {
            try
            {
                if (_osd == null) _osd = new Osd();
                _osd.Show(name, percent);
            }
            catch (Exception)
            {
            }
        }

        private int AverageBrightness()
        {
            if (_cards.Count == 0) return 0;
            int sum = 0;
            foreach (MonitorCard card in _cards)
                sum += card.Brightness.Value;
            return sum / _cards.Count;
        }

        // ------------------------------------------------------------------ command line (from a second process)

        /// <summary>Another Brighter process dropped its arguments into the command file; run them here.</summary>
        public void RunCommandFile()
        {
            try
            {
                string path = CommandLine.CommandFile;
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                File.Delete(path);
                ExecuteCommands(CommandLine.Parse(lines));
            }
            catch (Exception)
            {
            }
        }

        private void ExecuteCommands(List<Command> commands)
        {
            foreach (Command command in commands)
            {
                if (command.Kind == "profile")
                {
                    ProfileData profile = FindProfile(command.Name);
                    if (profile != null) ApplyProfile(profile);
                    continue;
                }

                foreach (MonitorCard card in _cards)
                {
                    if (command.Target != 0 && command.Target != card.Device.Index) continue;
                    if (command.Kind == "set")
                        card.Brightness.Value = command.Value;
                    else if (command.Kind == "contrast" && card.Contrast != null)
                        card.Contrast.Value = command.Value;
                    else if (command.Kind == "volume" && card.Volume != null)
                        card.Volume.Value = command.Value;
                    else if (command.Kind == "temp")
                        card.Temperature.Value = GammaRamp.LevelFromKelvin(command.Value);
                }
            }
        }

        // ------------------------------------------------------------------ loading

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // Raised on a system thread when a monitor is plugged in, removed or rearranged.
            Dispatcher.BeginInvoke(new Action(delegate { Reload(800); }));
        }

        private void Reload(int delayMs)
        {
            if (_loading)
            {
                _reloadQueued = true;
                return;
            }
            _loading = true;
            _monitorStatus = L.Scanning;
            UpdateStatus();

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<MonitorDevice> found;
                try
                {
                    if (delayMs > 0) Thread.Sleep(delayMs);
                    found = MonitorService.Enumerate();
                }
                catch (Exception error)
                {
                    Log.Error("enumerate", error);
                    found = new List<MonitorDevice>();
                }
                Dispatcher.BeginInvoke(new Action(delegate { ShowDevices(found); }));
            });
        }

        private void ShowDevices(List<MonitorDevice> found)
        {
            foreach (MonitorDevice old in _devices)
                old.Dispose();
            CloseOverlays();
            _devices = found;
            foreach (MonitorDevice device in _devices)
                Log.Write("monitor " + device.Index + ": " + device.Name + " [" + device.Connection + "] " + device.Method + (device.HasContrast ? " +contrast" : "") + (device.HasVolume ? " +volume" : ""));

            // Colour temperature and software dimming exist only inside this app, so they always
            // start from the remembered value.
            foreach (MonitorDevice device in _devices)
            {
                MonitorMemory remembered = FindMemory(device.Id);
                if (remembered == null) continue;
                if (remembered.Temperature >= 0) device.Temperature = remembered.Temperature;

                // A monitor that has taken hardware commands before must never silently drop to the
                // dimming overlay: show it as "not answering" and keep trying instead.
                if (device.Method == ControlMethod.Software && remembered.Hardware)
                {
                    device.Method = ControlMethod.None;
                    device.Detail = device.Detail.Replace(L.Software, L.NoAnswer);
                    Log.Write("ddc: " + device.Name + " did not answer, will retry");
                }
                if (device.Method != ControlMethod.Ddc && device.Method != ControlMethod.Wmi)
                    device.SetInitialPercent(remembered.Brightness);
            }

            bool waiting = false;
            foreach (MonitorDevice device in _devices)
            {
                if (device.Method == ControlMethod.None) waiting = true;
                if (device.Method != ControlMethod.Ddc) continue;
                MonitorMemory memory = FindMemory(device.Id);
                if (memory == null)
                {
                    Remember(device);
                    memory = FindMemory(device.Id);
                }
                if (memory != null && !memory.Hardware)
                {
                    memory.Hardware = true;
                    SaveSoon();
                }
            }
            _retryTimer.Stop();
            if (waiting)
            {
                _retryCount++;
                _retryTimer.Interval = TimeSpan.FromSeconds(_retryCount <= 10 ? 6 : 30);
                _retryTimer.Start();
            }
            else
            {
                _retryCount = 0;
            }

            BuildMonitorView();

            foreach (MonitorCard card in _cards)
            {
                MonitorDevice device = card.Device;
                if (device.Temperature < 100) GammaRamp.Apply(device.GdiName, device.Temperature);
                if (device.Method == ControlMethod.Software && device.Percent < 100)
                    GetOverlay(device).SetLevel(device.Percent);
            }

            // A monitor that was not there a moment ago gets its remembered values back.
            HashSet<string> present = new HashSet<string>();
            foreach (MonitorCard card in _cards)
            {
                string id = card.Device.Id;
                present.Add(id);
                if (_firstLoad || !_settings.RestoreOnConnect || _knownIds.Contains(id)) continue;
                MonitorMemory memory = FindMemory(id);
                if (memory == null) continue;
                ApplyValues(card, memory.Brightness, memory.Contrast, -1);
            }
            _knownIds = present;
            _firstLoad = false;

            BuildLayoutCard();

            _loading = false;
            CheckSchedule();
            if (_reloadQueued)
            {
                _reloadQueued = false;
                Reload(300);
            }
        }

        // ------------------------------------------------------------------ software dimming overlays

        private DimOverlay GetOverlay(MonitorDevice device)
        {
            DimOverlay overlay;
            if (!_overlays.TryGetValue(device.Id, out overlay))
            {
                overlay = new DimOverlay(device.Bounds);
                _overlays[device.Id] = overlay;
            }
            return overlay;
        }

        private void CloseOverlays()
        {
            foreach (DimOverlay overlay in _overlays.Values)
            {
                try { overlay.Close(); }
                catch (Exception) { }
            }
            _overlays.Clear();
        }

        // ------------------------------------------------------------------ monitor view

        private void BuildMonitorView()
        {
            _cards.Clear();
            _monitorPanel.Children.Clear();

            if (_settings.Profiles.Count > 0)
                _monitorPanel.Children.Add(BuildProfileChips());

            if (_devices.Count == 0)
            {
                TextBlock empty = MakeText(L.NoMonitors, 13, Theme.Muted, FontWeights.Normal);
                empty.Margin = new Thickness(4, 12, 4, 20);
                _monitorPanel.Children.Add(empty);
            }

            // Cards are built first so the master slider can drive their sliders.
            List<UIElement> elements = new List<UIElement>();
            foreach (MonitorDevice device in _devices)
                elements.Add(BuildMonitorCard(device));

            if (_cards.Count >= 2)
                _monitorPanel.Children.Add(BuildMasterCard(AverageBrightness()));
            foreach (UIElement element in elements)
                _monitorPanel.Children.Add(element);

            _monitorStatus = L.Status(_devices.Count);
            UpdateStatus();
        }

        private UIElement BuildProfileChips()
        {
            WrapPanel row = new WrapPanel();
            row.Margin = new Thickness(0, 0, 0, 4);
            foreach (ProfileData profile in _settings.Profiles)
            {
                ProfileData captured = profile;
                FlatButton chip = MakeChip(profile.Name, Theme.Accent);
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.Click += delegate { ApplyProfile(captured); };
                row.Children.Add(chip);
            }
            return row;
        }

        private UIElement BuildMasterCard(int average)
        {
            StackPanel body = new StackPanel();

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border badge = MakeBadge();
            SunIcon sun = new SunIcon();
            sun.Width = 20;
            sun.Height = 20;
            sun.HorizontalAlignment = HorizontalAlignment.Center;
            sun.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = sun;
            header.Children.Add(badge);

            StackPanel titles = new StackPanel();
            titles.Margin = new Thickness(12, 0, 0, 0);
            titles.VerticalAlignment = VerticalAlignment.Center;
            titles.Children.Add(MakeText(L.AllMonitors, 14.5, Theme.Text, FontWeights.SemiBold));
            titles.Children.Add(MakeText(_settings.RelativeMaster ? L.AllMonitorsRelativeHint : L.AllMonitorsHint, 12, Theme.Muted, FontWeights.Normal));
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            body.Children.Add(header);

            TextBlock percentLabel;
            BrightnessSlider master;
            body.Children.Add(BuildSliderRow(null, average, 14, out master, out percentLabel));
            _masterPrevious = average;
            _masterStamp = 0;

            master.ValueChanged += delegate
            {
                int level = master.Value;
                percentLabel.Text = level + "%";
                if (_settings.RelativeMaster)
                {
                    // Keep the differences between monitors: remember where everything was when
                    // this drag started and move all of them by the same amount.
                    int now = Environment.TickCount;
                    if (_masterStamp == 0 || unchecked(now - _masterStamp) > 1500)
                    {
                        _masterBase = _masterPrevious;
                        foreach (MonitorCard card in _cards)
                            card.Base = card.Brightness.Value;
                    }
                    _masterStamp = now == 0 ? 1 : now;
                    foreach (MonitorCard card in _cards)
                        card.Brightness.Value = card.Base + (level - _masterBase);
                }
                else
                {
                    foreach (MonitorCard card in _cards)
                        card.Brightness.Value = level;
                }
                _masterPrevious = level;
            };

            body.Children.Add(BuildPresets(master));
            return WrapCard(body);
        }

        private UIElement BuildMonitorCard(MonitorDevice device)
        {
            StackPanel body = new StackPanel();

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border badge = MakeBadge();
            TextBlock number = MakeText(device.Index.ToString(), 15, Theme.Accent, FontWeights.SemiBold);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = number;
            header.Children.Add(badge);

            StackPanel titles = new StackPanel();
            titles.Margin = new Thickness(12, 0, 10, 0);
            titles.VerticalAlignment = VerticalAlignment.Center;
            TextBlock nameText = MakeText(device.Name, 14.5, Theme.Text, FontWeights.SemiBold);
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            titles.Children.Add(nameText);
            if (!string.IsNullOrEmpty(device.Detail))
            {
                TextBlock detail = MakeText(device.Detail, 12, Theme.Muted, FontWeights.Normal);
                detail.TextTrimming = TextTrimming.CharacterEllipsis;
                titles.Children.Add(detail);
            }
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);

            if (!string.IsNullOrEmpty(device.Connection))
            {
                Border connection = new Border();
                connection.Padding = new Thickness(10, 4, 10, 5);
                connection.CornerRadius = new CornerRadius(9);
                connection.Background = Theme.Chip;
                connection.VerticalAlignment = VerticalAlignment.Center;
                connection.Child = MakeText(device.Connection, 11.5, Theme.Accent, FontWeights.SemiBold);
                Grid.SetColumn(connection, 2);
                header.Children.Add(connection);
            }
            body.Children.Add(header);

            if (device.Method == ControlMethod.Software)
            {
                TextBlock note = MakeText(L.SoftwareNote, 11.5, Theme.Muted, FontWeights.Normal);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(note);
            }

            if (device.Method == ControlMethod.None)
            {
                TextBlock warning = MakeText(L.NotResponding, 11.5, Theme.Accent, FontWeights.Normal);
                warning.TextWrapping = TextWrapping.Wrap;
                warning.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(warning);
            }

            MonitorCard card = new MonitorCard();
            card.Device = device;

            // Brightness
            TextBlock brightnessLabel;
            BrightnessSlider brightness;
            body.Children.Add(BuildSliderRow(L.Brightness, device.Percent, 14, out brightness, out brightnessLabel));
            brightness.ValueChanged += delegate
            {
                int level = brightness.Value;
                brightnessLabel.Text = level + "%";
                device.RequestPercent(level);
                if (device.Method == ControlMethod.Software)
                    GetOverlay(device).SetLevel(level);
                Remember(device);
            };
            card.Brightness = brightness;
            if (device.Method == ControlMethod.None)
            {
                brightness.IsEnabled = false;
                brightness.Opacity = 0.35;
            }

            // Contrast
            if (device.HasContrast)
            {
                TextBlock contrastLabel;
                BrightnessSlider contrast;
                body.Children.Add(BuildSliderRow(L.Contrast, device.Contrast, 4, out contrast, out contrastLabel));
                contrast.ValueChanged += delegate
                {
                    int level = contrast.Value;
                    contrastLabel.Text = level + "%";
                    device.RequestContrast(level);
                    Remember(device);
                };
                card.Contrast = contrast;
            }

            // Colour temperature (gamma ramp, works on every display)
            TextBlock temperatureLabel;
            BrightnessSlider temperature;
            body.Children.Add(BuildSliderRow(L.Temperature, device.Temperature, 4, out temperature, out temperatureLabel));
            temperatureLabel.Text = GammaRamp.KelvinFromLevel(device.Temperature) + "K";
            temperature.ValueChanged += delegate
            {
                int level = temperature.Value;
                temperatureLabel.Text = GammaRamp.KelvinFromLevel(level) + "K";
                device.Temperature = level;
                GammaRamp.Apply(device.GdiName, level);
                Remember(device);
            };
            card.Temperature = temperature;

            // Speaker volume
            if (device.HasVolume)
            {
                TextBlock volumeLabel;
                BrightnessSlider volume;
                body.Children.Add(BuildSliderRow(L.Volume, device.Volume, 4, out volume, out volumeLabel));
                volume.ValueChanged += delegate
                {
                    int level = volume.Value;
                    volumeLabel.Text = level + "%";
                    device.RequestVolume(level);
                };
                card.Volume = volume;
            }
            _cards.Add(card);

            // Bottom row: presets on the left, input + power on the right.
            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.Children.Add(BuildPresets(brightness));

            if (device.IsDdc)
            {
                StackPanel actions = new StackPanel();
                actions.Orientation = Orientation.Horizontal;
                actions.Margin = new Thickness(0, 10, 0, 0);

                FlatButton inputButton = MakeChip(L.Input, Theme.Text);
                inputButton.ToolTip = L.InputTip;
                inputButton.Click += delegate { OpenInputMenu(device, inputButton); };
                actions.Children.Add(inputButton);

                FlatButton powerButton = new FlatButton("\uE7E8", Theme.Icons, 12, Theme.Muted, Theme.Chip, Theme.ChipHover);
                powerButton.Width = 34;
                powerButton.CornerRadius = new CornerRadius(9);
                powerButton.Margin = new Thickness(6, 0, 0, 0);
                powerButton.ToolTip = L.PowerTip;
                if (device.PoweredOff) powerButton.Label.Foreground = Theme.Accent;
                powerButton.Click += delegate
                {
                    bool turnOff = !device.PoweredOff;
                    device.SetPower(!turnOff);
                    powerButton.Label.Foreground = turnOff ? Theme.Accent : Theme.Muted;
                };
                actions.Children.Add(powerButton);

                Grid.SetColumn(actions, 1);
                footer.Children.Add(actions);
            }
            body.Children.Add(footer);
            return WrapCard(body);
        }

        private static UIElement BuildSliderRow(string caption, int percent, double topMargin, out BrightnessSlider slider, out TextBlock valueLabel)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, topMargin, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });

            if (caption != null)
            {
                TextBlock captionText = MakeText(caption, 11.5, Theme.Muted, FontWeights.Normal);
                captionText.Width = 62;
                captionText.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(captionText);
            }

            slider = new BrightnessSlider();
            slider.SetSilently(percent);
            slider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);

            valueLabel = MakeText(slider.Value + "%", 15, Theme.Text, FontWeights.SemiBold);
            valueLabel.TextAlignment = TextAlignment.Right;
            valueLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(valueLabel, 2);
            row.Children.Add(valueLabel);
            return row;
        }

        private static UIElement BuildPresets(BrightnessSlider slider)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 10, 0, 0);

            int[] presets = new int[] { 0, 25, 50, 75, 100 };
            for (int i = 0; i < presets.Length; i++)
            {
                int preset = presets[i];
                FlatButton chip = MakeChip(preset + "%", Theme.Muted);
                chip.Padding = new Thickness(10, 5, 10, 6);
                chip.Margin = new Thickness(0, 0, 5, 0);
                chip.Click += delegate { slider.Value = preset; };
                row.Children.Add(chip);
            }
            return row;
        }

        // ------------------------------------------------------------------ input source menu

        private void OpenInputMenu(MonitorDevice device, FlatButton anchorButton)
        {
            // Asking a monitor for its input list can take a second or two the first time.
            anchorButton.Label.Text = "\u2026";
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                int current = -1;
                List<int> codes;
                try
                {
                    codes = device.ReadInputs(out current);
                }
                catch (Exception)
                {
                    codes = new List<int>();
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    anchorButton.Label.Text = L.Input;
                    if (codes.Count > 0)
                        ShowInputPopup(device, anchorButton, codes, current);
                }));
            });
        }

        private void ShowInputPopup(MonitorDevice device, FlatButton anchorButton, List<int> codes, int current)
        {
            Popup popup = new Popup();
            popup.PlacementTarget = anchorButton;
            popup.Placement = PlacementMode.Bottom;
            popup.VerticalOffset = 4;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;

            StackPanel items = new StackPanel();
            foreach (int code in codes)
            {
                int captured = code;
                // Some monitors use their own numbering, so the standard name of the active input can be
                // wrong. Windows knows how the monitor is really connected: trust that for the active one.
                string label = MonitorDevice.InputName(code);
                if (code == current)
                {
                    bool matches = device.Connection.Length > 0 && label.StartsWith(device.Connection, StringComparison.OrdinalIgnoreCase);
                    label = (matches || device.Connection.Length == 0 ? label : device.Connection) + "  \u00B7  " + L.Current;
                }
                FlatButton item = new FlatButton(label, Theme.Ui, 12.5,
                    code == current ? Theme.Accent : Theme.Text, Brushes.Transparent, Theme.ChipHover);
                item.Label.HorizontalAlignment = HorizontalAlignment.Left;
                item.Padding = new Thickness(12, 6, 12, 7);
                item.CornerRadius = new CornerRadius(7);
                item.MinWidth = 150;
                item.Click += delegate
                {
                    popup.IsOpen = false;
                    SwitchInput(device, captured, current);
                };
                items.Children.Add(item);
            }

            Border box = new Border();
            box.Background = Theme.Card;
            box.BorderBrush = Theme.CardBorder;
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(10);
            box.Padding = new Thickness(5);
            box.Child = items;

            popup.Child = box;
            popup.IsOpen = true;
        }

        // ------------------------------------------------------------------ memory / profiles

        private void SaveSoon()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private MonitorMemory FindMemory(string id)
        {
            foreach (MonitorMemory memory in _settings.Monitors)
            {
                if (memory.MonitorId == id) return memory;
            }
            return null;
        }

        private void Remember(MonitorDevice device)
        {
            if (_transient || device.Method == ControlMethod.None) return;
            MonitorMemory memory = FindMemory(device.Id);
            if (memory == null)
            {
                memory = new MonitorMemory();
                memory.MonitorId = device.Id;
                _settings.Monitors.Add(memory);
            }
            memory.Brightness = device.Percent;
            memory.Contrast = device.HasContrast ? device.Contrast : -1;
            memory.Temperature = device.Temperature;
            SaveSoon();
        }

        private ProfileData FindProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (ProfileData profile in _settings.Profiles)
            {
                if (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)) return profile;
            }
            return null;
        }

        /// <summary>Moves a card's sliders; a negative value leaves that slider alone.</summary>
        private static void ApplyValues(MonitorCard card, int brightness, int contrast, int temperature)
        {
            if (brightness >= 0) card.Brightness.Value = brightness;
            if (contrast >= 0 && card.Contrast != null) card.Contrast.Value = contrast;
            if (temperature >= 0) card.Temperature.Value = temperature;
        }

        private void ApplyProfile(ProfileData profile)
        {
            foreach (MonitorCard card in _cards)
            {
                foreach (ProfileEntry entry in profile.Entries)
                {
                    if (entry.MonitorId != card.Device.Id) continue;
                    ApplyValues(card, entry.Brightness, entry.Contrast, entry.Temperature);
                    break;
                }
            }
        }

        private void SaveCurrentAsProfile(string name)
        {
            ProfileData profile = FindProfile(name);
            if (profile == null)
            {
                profile = new ProfileData();
                profile.Name = name;
                _settings.Profiles.Add(profile);
            }
            profile.Entries.Clear();
            foreach (MonitorCard card in _cards)
            {
                ProfileEntry entry = new ProfileEntry();
                entry.MonitorId = card.Device.Id;
                entry.Brightness = card.Device.Percent;
                entry.Contrast = card.Device.HasContrast ? card.Device.Contrast : -1;
                entry.Temperature = card.Device.Temperature;
                profile.Entries.Add(entry);
            }
            SaveSoon();
        }

        private Dictionary<string, int[]> TakeSnapshot()
        {
            Dictionary<string, int[]> snapshot = new Dictionary<string, int[]>();
            foreach (MonitorCard card in _cards)
            {
                snapshot[card.Device.Id] = new int[]
                {
                    card.Brightness.Value,
                    card.Contrast != null ? card.Contrast.Value : -1,
                    card.Temperature.Value
                };
            }
            return snapshot;
        }

        private void RestoreSnapshot(Dictionary<string, int[]> snapshot)
        {
            if (snapshot == null) return;
            foreach (MonitorCard card in _cards)
            {
                int[] values;
                if (snapshot.TryGetValue(card.Device.Id, out values))
                    ApplyValues(card, values[0], values[1], values[2]);
            }
        }

        // ------------------------------------------------------------------ automations

        private static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split(':');
            if (parts.Length != 2) return false;
            int hours;
            int minutes;
            if (!int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes)) return false;
            if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) return false;
            time = new TimeSpan(hours, minutes, 0);
            return true;
        }

        private static bool TryParseCoordinate(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            return double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Sunrise/sunset for the configured location, when that option is on and usable.</summary>
        private bool TryGetSunTimes(out TimeSpan sunrise, out TimeSpan sunset)
        {
            sunrise = TimeSpan.Zero;
            sunset = TimeSpan.Zero;
            double latitude;
            double longitude;
            if (!TryParseCoordinate(_settings.Latitude, out latitude) || !TryParseCoordinate(_settings.Longitude, out longitude))
                return false;
            return SunTimes.TryGet(DateTime.Now, latitude, longitude, out sunrise, out sunset);
        }

        /// <summary>Day/night switch times: sunrise and sunset when enabled and available, otherwise the fixed times.</summary>
        private bool GetScheduleTimes(out TimeSpan day, out TimeSpan night)
        {
            if (_settings.UseSun && TryGetSunTimes(out day, out night)) return true;
            day = TimeSpan.Zero;
            night = TimeSpan.Zero;
            return TryParseTime(_settings.DayTime, out day) && TryParseTime(_settings.NightTime, out night);
        }

        /// <summary>Applies the day or night profile once, at the moment the period changes.</summary>
        private void CheckSchedule()
        {
            if (!_settings.ScheduleEnabled || _cards.Count == 0) return;

            TimeSpan day;
            TimeSpan night;
            if (!GetScheduleTimes(out day, out night)) return;
            if (day == night) return;

            TimeSpan now = DateTime.Now.TimeOfDay;
            bool isDay = day < night ? (now >= day && now < night) : !(now >= night && now < day);
            string period = isDay ? "day" : "night";
            if (period == _lastPeriod) return;
            _lastPeriod = period;

            ProfileData profile = FindProfile(isDay ? _settings.DayProfile : _settings.NightProfile);
            if (profile != null) ApplyProfile(profile);
        }

        /// <summary>Dims every monitor after a period without keyboard or mouse input, restores on activity.</summary>
        private void CheckIdle()
        {
            NativeMethods.LASTINPUTINFO info = new NativeMethods.LASTINPUTINFO();
            info.cbSize = 8;
            if (!NativeMethods.GetLastInputInfo(ref info)) return;
            uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);

            if (_idleDimmed)
            {
                if (idleMs < 3000 || !_settings.IdleEnabled)
                {
                    Log.Write("idle: restoring");
                    _transient = true;
                    try { RestoreSnapshot(_idleSnapshot); }
                    finally { _transient = false; }
                    _idleSnapshot = null;
                    _idleDimmed = false;
                }
                return;
            }

            if (!_settings.IdleEnabled || _cards.Count == 0) return;
            uint limitMs = (uint)Math.Max(1, _settings.IdleMinutes) * 60000u;
            if (idleMs < limitMs) return;

            Log.Write("idle: dimming to " + _settings.IdlePercent + "% of the current brightness");
            _idleSnapshot = TakeSnapshot();
            _transient = true;
            try
            {
                foreach (MonitorCard card in _cards)
                {
                    // Relative to where each monitor is now, so a dim monitor gets dimmer too.
                    card.Brightness.Value = (int)Math.Round(card.Brightness.Value * _settings.IdlePercent / 100.0);
                }
            }
            finally
            {
                _transient = false;
            }
            _idleDimmed = true;
        }

        /// <summary>Applies a profile while a chosen program is in the foreground, and undoes it afterwards.</summary>
        private void CheckForegroundApp()
        {
            if (_settings.AppRules.Count == 0 && _activeRule == null) return;
            if (_cards.Count == 0) return;

            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero || window == _lastForeground) return;
            _lastForeground = window;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId == 0 || processId == (uint)_ownProcessId) return; // adjusting things in Brighter itself changes nothing

            string processName;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    processName = process.ProcessName;
                }
            }
            catch (Exception)
            {
                return;
            }

            AppRule match = null;
            foreach (AppRule rule in _settings.AppRules)
            {
                if (string.Equals(NormalizeProcessName(rule.Process), processName, StringComparison.OrdinalIgnoreCase))
                {
                    match = rule;
                    break;
                }
            }
            if (match == _activeRule) return;

            if (_activeRule == null) _ruleSnapshot = TakeSnapshot();
            _activeRule = match;

            _transient = true;
            try
            {
                if (match != null)
                {
                    ProfileData profile = FindProfile(match.Profile);
                    if (profile != null) ApplyProfile(profile);
                }
                else
                {
                    RestoreSnapshot(_ruleSnapshot);
                    _ruleSnapshot = null;
                }
            }
            finally
            {
                _transient = false;
            }
        }

        private static string NormalizeProcessName(string text)
        {
            string name = (text ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        // ------------------------------------------------------------------ confirmations / sleep

        private UIElement BuildInputBanner()
        {
            _inputCountdown = MakeText("", 12, Theme.Text, FontWeights.Normal);
            _inputCountdown.TextWrapping = TextWrapping.Wrap;

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 10, 0, 0);

            FlatButton keep = MakeChip(L.Keep, Theme.Accent);
            keep.Click += delegate { EndInputConfirmation(false); };
            buttons.Children.Add(keep);

            FlatButton revert = MakeChip(L.Revert, Theme.Text);
            revert.Margin = new Thickness(6, 0, 0, 0);
            revert.Click += delegate { EndInputConfirmation(true); };
            buttons.Children.Add(revert);

            StackPanel body = new StackPanel();
            body.Children.Add(_inputCountdown);
            body.Children.Add(buttons);

            _inputBanner = WrapCard(body);
            _inputBanner.BorderBrush = Theme.Accent;
            _inputBanner.Visibility = Visibility.Collapsed;
            return _inputBanner;
        }

        /// <summary>
        /// Switches a monitor's input and starts a countdown: if nobody confirms (because the screen
        /// went dark, say), the previous input is selected again.
        /// </summary>
        private void SwitchInput(MonitorDevice device, int code, int current)
        {
            if (code == current) return;
            Log.Write("input: " + device.Name + " " + current + " -> " + code);
            device.SetInput(code);
            if (current <= 0) return; // previous input unknown, nothing to go back to

            _inputDeviceId = device.Id;
            _inputPrevious = current;
            _inputSeconds = 12;
            _inputCountdown.Text = L.InputKeepQuestion(_inputSeconds);
            _inputBanner.Visibility = Visibility.Visible;
        }

        private void EndInputConfirmation(bool revert)
        {
            string id = _inputDeviceId;
            _inputDeviceId = null;
            _inputBanner.Visibility = Visibility.Collapsed;
            if (!revert || id == null) return;

            foreach (MonitorDevice device in _devices)
            {
                if (device.Id != id) continue;
                Log.Write("input: reverting " + device.Name + " to " + _inputPrevious);
                device.SetInput(_inputPrevious);
                return;
            }
            Log.Write("input: could not revert, the monitor is no longer visible to Windows");
        }

        private void KeepLayout()
        {
            _layoutPrevious = null;
            BuildLayoutCard();
        }

        private void RevertLayout()
        {
            List<DisplayPlacement> previous = _layoutPrevious;
            _layoutPrevious = null;
            if (previous != null)
            {
                Log.Write("layout: reverting");
                DisplayLayout.Apply(previous);
            }
            BuildLayoutCard();
        }

        private void TickCountdowns()
        {
            if (_layoutPrevious != null)
            {
                _layoutSeconds--;
                if (_layoutSeconds <= 0) RevertLayout();
                else if (_layoutCountdown != null) _layoutCountdown.Text = L.LayoutKeepQuestion(_layoutSeconds);
            }
            if (_inputDeviceId != null)
            {
                _inputSeconds--;
                if (_inputSeconds <= 0) EndInputConfirmation(true);
                else _inputCountdown.Text = L.InputKeepQuestion(_inputSeconds);
            }
        }

        /// <summary>Tray menu: every DDC/CI monitor to standby. They wake on the next key press or mouse move.</summary>
        private void SleepAll()
        {
            Log.Write("sleep all");
            foreach (MonitorDevice device in _devices)
                device.SetPower(false);
            _sleptAll = true;
            _sleptAllStamp = Environment.TickCount;
        }

        private void CheckSleepWake()
        {
            if (!_sleptAll) return;
            NativeMethods.LASTINPUTINFO info = new NativeMethods.LASTINPUTINFO();
            info.cbSize = 8;
            if (!NativeMethods.GetLastInputInfo(ref info)) return;
            // Only input that arrived a moment after the menu click counts.
            if (unchecked((int)info.dwTime - _sleptAllStamp) < 1500) return;

            _sleptAll = false;
            foreach (MonitorDevice device in _devices)
                device.SetPower(true);
            BuildMonitorView();
        }

        /// <summary>A rule that was deleted while it was in effect: put the previous values back.</summary>
        private void RevalidateActiveRule()
        {
            if (_activeRule == null || _settings.AppRules.Contains(_activeRule)) return;
            _activeRule = null;
            _transient = true;
            try { RestoreSnapshot(_ruleSnapshot); }
            finally { _transient = false; }
            _ruleSnapshot = null;
        }

        // ------------------------------------------------------------------ small builders

        /// <summary>Thin dark scrollbar instead of the default light one. Falls back to the default on any error.</summary>
        private static void ApplyScrollBarStyle(ScrollViewer scroll)
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='8'/><Setter Property='MinWidth' Value='8'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template>" +
                "<ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#3A4052' Margin='1,0'/></ControlTemplate>" +
                "</Thumb.Template></Thumb></Track.Thumb></Track>" +
                "</Grid></ControlTemplate>" +
                "</Setter.Value></Setter></Style>";
            try
            {
                scroll.Resources[typeof(ScrollBar)] = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
            }
            catch (Exception)
            {
            }
        }

        private static FlatButton MakeChip(string text, Brush foreground)
        {
            FlatButton chip = new FlatButton(text, Theme.Ui, 12, foreground, Theme.Chip, Theme.ChipHover);
            chip.Padding = new Thickness(12, 5, 12, 6);
            chip.CornerRadius = new CornerRadius(9);
            chip.VerticalAlignment = VerticalAlignment.Center;
            return chip;
        }

        private static TextBox MakeTextBox(string text)
        {
            TextBox box = new TextBox();
            box.Text = text;
            box.FontSize = 12.5;
            box.Padding = new Thickness(8, 5, 8, 5);
            box.Background = Theme.Chip;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.CardBorder;
            box.CaretBrush = Theme.Text;
            box.VerticalAlignment = VerticalAlignment.Center;
            return box;
        }

        private static Border MakeBadge()
        {
            Border badge = new Border();
            badge.Width = 40;
            badge.Height = 40;
            badge.CornerRadius = new CornerRadius(12);
            badge.Background = Theme.Chip;
            return badge;
        }

        private static Border WrapCard(UIElement body)
        {
            Border wrapper = new Border();
            wrapper.Background = Theme.Card;
            wrapper.BorderBrush = Theme.CardBorder;
            wrapper.BorderThickness = new Thickness(1);
            wrapper.CornerRadius = new CornerRadius(14);
            wrapper.Padding = new Thickness(16);
            wrapper.Margin = new Thickness(0, 0, 0, 10);
            wrapper.Child = body;
            return wrapper;
        }

        private static TextBlock MakeText(string text, double size, Brush brush, FontWeight weight)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontSize = size;
            block.Foreground = brush;
            block.FontWeight = weight;
            return block;
        }
    }
}
