using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Brighter
{
    /// <summary>The settings screen of the main window.</summary>
    internal sealed partial class MainWindow
    {
        private void BuildSettingsView()
        {
            _settingsPanel.Children.Clear();
            _settingsPanel.Children.Add(_layoutHost);
            _settingsPanel.Children.Add(BuildGeneralCard());
            _settingsPanel.Children.Add(BuildHotkeyCard());
            _settingsPanel.Children.Add(BuildProfilesCard());
            _settingsPanel.Children.Add(BuildScheduleCard());
            _settingsPanel.Children.Add(BuildIdleCard());
            _settingsPanel.Children.Add(BuildAppRulesCard());
            _settingsPanel.Children.Add(BuildAboutCard());
        }

        /// <summary>Profiles were added or removed: every place that lists them is rebuilt.</summary>
        private void OnProfilesChanged()
        {
            SaveSoon();
            UpdateHotkeys();
            BuildMonitorView();
            BuildSettingsView();
        }

        // ------------------------------------------------------------------ arrangement

        private void BuildLayoutCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeText(L.Layout, 14.5, Theme.Text, FontWeights.SemiBold));

            // A new arrangement was just applied: ask to keep it, undo automatically otherwise.
            if (_layoutPrevious != null)
            {
                _layoutCountdown = MakeWrappedText(L.LayoutKeepQuestion(_layoutSeconds));
                _layoutCountdown.Foreground = Theme.Text;
                body.Children.Add(_layoutCountdown);

                StackPanel confirm = new StackPanel();
                confirm.Orientation = Orientation.Horizontal;
                confirm.HorizontalAlignment = HorizontalAlignment.Right;
                confirm.Margin = new Thickness(0, 10, 0, 0);

                FlatButton keep = MakeChip(L.Keep, Theme.Accent);
                keep.Click += delegate { KeepLayout(); };
                confirm.Children.Add(keep);

                FlatButton revert = MakeChip(L.Revert, Theme.Text);
                revert.Margin = new Thickness(6, 0, 0, 0);
                revert.Click += delegate { RevertLayout(); };
                confirm.Children.Add(revert);

                body.Children.Add(confirm);
                _layoutHost.Child = WrapCard(body);
                return;
            }
            _layoutCountdown = null;

            List<DisplayPlacement> placements = DisplayLayout.Read(_devices);
            if (placements.Count < 2)
            {
                body.Children.Add(MakeWrappedText(L.LayoutSingle));
                _layoutHost.Child = WrapCard(body);
                return;
            }

            body.Children.Add(MakeWrappedText(L.LayoutHint));

            LayoutEditor editor = new LayoutEditor(placements);
            Border stage = new Border();
            stage.Background = Theme.Chip;
            stage.CornerRadius = new CornerRadius(10);
            stage.Margin = new Thickness(0, 12, 0, 0);
            stage.Child = editor;
            body.Children.Add(stage);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 10, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock result = MakeText("", 12, Theme.Muted, FontWeights.Normal);
            result.VerticalAlignment = VerticalAlignment.Center;
            result.TextTrimming = TextTrimming.CharacterEllipsis;
            actions.Children.Add(result);

            FlatButton makePrimary = MakeChip(L.MakePrimary, Theme.Text);
            makePrimary.Click += delegate { editor.MakeSelectedPrimary(); };
            Grid.SetColumn(makePrimary, 1);
            actions.Children.Add(makePrimary);

            FlatButton applyLayout = MakeChip(L.Apply, Theme.Accent);
            applyLayout.Margin = new Thickness(6, 0, 0, 0);
            applyLayout.Click += delegate
            {
                List<DisplayPlacement> before = DisplayLayout.Read(_devices);
                if (!DisplayLayout.Apply(editor.Items))
                {
                    result.Text = L.LayoutFailed;
                    return;
                }
                _layoutPrevious = before;
                _layoutSeconds = 15;
                BuildLayoutCard();
            };
            Grid.SetColumn(applyLayout, 2);
            actions.Children.Add(applyLayout);

            body.Children.Add(actions);
            _layoutHost.Child = WrapCard(body);
        }

        // ------------------------------------------------------------------ general

        private UIElement BuildGeneralCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeText(L.General, 14.5, Theme.Text, FontWeights.SemiBold));
            body.Children.Add(MakeToggleRow(L.AutoStart, L.AutoStartHint, AutoStart.IsEnabled, delegate(bool on)
            {
                AutoStart.Set(on);
            }));
            body.Children.Add(MakeToggleRow(L.Restore, L.RestoreHint, _settings.RestoreOnConnect, delegate(bool on)
            {
                _settings.RestoreOnConnect = on;
                SaveSoon();
            }));
            body.Children.Add(MakeToggleRow(L.RelativeMaster, L.RelativeMasterHint, _settings.RelativeMaster, delegate(bool on)
            {
                _settings.RelativeMaster = on;
                SaveSoon();
                BuildMonitorView();
            }));
            return WrapCard(body);
        }

        // ------------------------------------------------------------------ shortcuts

        private UIElement BuildHotkeyCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeToggleRow(L.Hotkeys, L.HotkeysHint, _settings.HotkeysEnabled, delegate(bool on)
            {
                _settings.HotkeysEnabled = on;
                UpdateHotkeys();
                SaveSoon();
            }));
            body.Children.Add(MakeLabeledRow(L.KeyUp, MakeHotkeyBox(_settings.KeyUp)));
            body.Children.Add(MakeLabeledRow(L.KeyDown, MakeHotkeyBox(_settings.KeyDown)));
            body.Children.Add(MakeLabeledRow(L.KeyAllUp, MakeHotkeyBox(_settings.KeyAllUp)));
            body.Children.Add(MakeLabeledRow(L.KeyAllDown, MakeHotkeyBox(_settings.KeyAllDown)));
            body.Children.Add(MakeLabeledRow(L.HotkeyStep, MakeNumberBox(_settings.HotkeyStep, 1, 50, delegate(int number)
            {
                _settings.HotkeyStep = number;
            })));
            return WrapCard(body);
        }

        private HotkeyBox MakeHotkeyBox(HotkeyBinding binding)
        {
            HotkeyBox box = new HotkeyBox(binding.Modifiers, binding.Key);
            // While a box is listening the global shortcuts are released, otherwise Windows would
            // swallow the very combination being typed.
            box.GotKeyboardFocus += delegate { UnregisterHotkeys(); };
            box.LostKeyboardFocus += delegate { UpdateHotkeys(); };
            box.Changed += delegate
            {
                binding.Modifiers = box.Modifiers;
                binding.Key = box.Key;
                SaveSoon();
            };
            return box;
        }

        // ------------------------------------------------------------------ profiles

        private UIElement BuildProfilesCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeText(L.Profiles, 14.5, Theme.Text, FontWeights.SemiBold));
            body.Children.Add(MakeWrappedText(L.ProfilesHint));

            foreach (ProfileData profile in _settings.Profiles)
                body.Children.Add(BuildProfileRow(profile));

            Grid addRow = new Grid();
            addRow.Margin = new Thickness(0, 12, 0, 0);
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox nameBox = MakeTextBox(L.Profile + " " + (_settings.Profiles.Count + 1));
            addRow.Children.Add(nameBox);

            FlatButton saveButton = MakeChip(L.SaveCurrent, Theme.Accent);
            saveButton.Margin = new Thickness(8, 0, 0, 0);
            saveButton.Click += delegate
            {
                string chosen = nameBox.Text.Trim();
                if (chosen.Length == 0) chosen = L.Profile + " " + (_settings.Profiles.Count + 1);
                SaveCurrentAsProfile(chosen);
                OnProfilesChanged();
            };
            Grid.SetColumn(saveButton, 1);
            addRow.Children.Add(saveButton);
            body.Children.Add(addRow);
            return WrapCard(body);
        }

        private UIElement BuildProfileRow(ProfileData profile)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 10, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock nameText = MakeText(profile.Name, 13, Theme.Text, FontWeights.Normal);
            nameText.VerticalAlignment = VerticalAlignment.Center;
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(nameText);

            HotkeyBox hotkey = MakeHotkeyBox(profile.Hotkey);
            hotkey.MinWidth = 96;
            Grid.SetColumn(hotkey, 1);
            row.Children.Add(hotkey);

            FlatButton applyButton = MakeChip(L.Apply, Theme.Accent);
            applyButton.Margin = new Thickness(6, 0, 0, 0);
            applyButton.Click += delegate { ApplyProfile(profile); };
            Grid.SetColumn(applyButton, 2);
            row.Children.Add(applyButton);

            FlatButton deleteButton = MakeChip(L.Delete, Theme.Muted);
            deleteButton.Margin = new Thickness(6, 0, 0, 0);
            deleteButton.Click += delegate
            {
                _settings.Profiles.Remove(profile);
                OnProfilesChanged();
            };
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(deleteButton);
            return row;
        }

        // ------------------------------------------------------------------ schedule

        private UIElement BuildScheduleCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeToggleRow(L.Schedule, L.ScheduleHint, _settings.ScheduleEnabled, delegate(bool on)
            {
                _settings.ScheduleEnabled = on;
                _lastPeriod = null;
                SaveSoon();
                CheckSchedule();
            }));
            body.Children.Add(MakeScheduleRow(L.Day, _settings.DayTime, _settings.DayProfile,
                delegate(string text) { _settings.DayTime = text; },
                delegate(string chosen) { _settings.DayProfile = chosen; }));
            body.Children.Add(MakeScheduleRow(L.Night, _settings.NightTime, _settings.NightProfile,
                delegate(string text) { _settings.NightTime = text; },
                delegate(string chosen) { _settings.NightProfile = chosen; }));

            TextBlock sunInfo = MakeText("", 11.5, Theme.Muted, FontWeights.Normal);
            sunInfo.Margin = new Thickness(0, 8, 0, 0);
            sunInfo.TextWrapping = TextWrapping.Wrap;

            body.Children.Add(MakeToggleRow(L.UseSun, L.UseSunHint, _settings.UseSun, delegate(bool on)
            {
                _settings.UseSun = on;
                _lastPeriod = null;
                SaveSoon();
                CheckSchedule();
            }));

            Grid location = new Grid();
            location.Margin = new Thickness(0, 10, 0, 0);
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock latitudeCaption = MakeText(L.Latitude, 12, Theme.Muted, FontWeights.Normal);
            latitudeCaption.VerticalAlignment = VerticalAlignment.Center;
            latitudeCaption.Margin = new Thickness(0, 0, 8, 0);
            location.Children.Add(latitudeCaption);

            TextBox latitudeBox = MakeTextBox(_settings.Latitude);
            Grid.SetColumn(latitudeBox, 1);
            location.Children.Add(latitudeBox);

            TextBlock longitudeCaption = MakeText(L.Longitude, 12, Theme.Muted, FontWeights.Normal);
            longitudeCaption.VerticalAlignment = VerticalAlignment.Center;
            longitudeCaption.Margin = new Thickness(12, 0, 8, 0);
            Grid.SetColumn(longitudeCaption, 2);
            location.Children.Add(longitudeCaption);

            TextBox longitudeBox = MakeTextBox(_settings.Longitude);
            Grid.SetColumn(longitudeBox, 3);
            location.Children.Add(longitudeBox);

            latitudeBox.TextChanged += delegate
            {
                _settings.Latitude = latitudeBox.Text.Trim();
                _lastPeriod = null;
                SaveSoon();
                sunInfo.Text = DescribeSun();
            };
            longitudeBox.TextChanged += delegate
            {
                _settings.Longitude = longitudeBox.Text.Trim();
                _lastPeriod = null;
                SaveSoon();
                sunInfo.Text = DescribeSun();
            };

            body.Children.Add(location);
            sunInfo.Text = DescribeSun();
            body.Children.Add(sunInfo);
            return WrapCard(body);
        }

        private string DescribeSun()
        {
            TimeSpan sunrise;
            TimeSpan sunset;
            if (!TryGetSunTimes(out sunrise, out sunset)) return L.SunNeedsLocation;
            return L.SunToday(FormatTime(sunrise), FormatTime(sunset));
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.Hours.ToString("00") + ":" + time.Minutes.ToString("00");
        }

        private UIElement MakeScheduleRow(string caption, string time, string profileName, Action<string> setTime, Action<string> setProfile)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock captionText = MakeText(caption, 13, Theme.Text, FontWeights.Normal);
            captionText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(captionText);

            TextBox timeBox = MakeTextBox(time);
            timeBox.MaxLength = 5;
            timeBox.TextAlignment = TextAlignment.Center;
            timeBox.TextChanged += delegate
            {
                TimeSpan parsed;
                bool valid = TryParseTime(timeBox.Text, out parsed);
                timeBox.Foreground = valid ? Theme.Text : Theme.CloseHover;
                if (!valid) return;
                setTime(timeBox.Text.Trim());
                _lastPeriod = null;
                SaveSoon();
            };
            Grid.SetColumn(timeBox, 1);
            row.Children.Add(timeBox);

            FlatButton picker = MakeProfilePicker(profileName, delegate(string chosen)
            {
                setProfile(chosen);
                _lastPeriod = null;
                SaveSoon();
            });
            Grid.SetColumn(picker, 3);
            row.Children.Add(picker);
            return row;
        }

        // ------------------------------------------------------------------ idle

        private UIElement BuildIdleCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeToggleRow(L.Idle, L.IdleHint, _settings.IdleEnabled, delegate(bool on)
            {
                _settings.IdleEnabled = on;
                SaveSoon();
            }));
            body.Children.Add(MakeLabeledRow(L.IdleMinutes, MakeNumberBox(_settings.IdleMinutes, 1, 240, delegate(int number)
            {
                _settings.IdleMinutes = number;
            })));
            body.Children.Add(MakeLabeledRow(L.IdlePercent, MakeNumberBox(_settings.IdlePercent, 0, 100, delegate(int number)
            {
                _settings.IdlePercent = number;
            })));

            // While a video plays: cycles through the three behaviours on each click.
            FlatButton media = MakeChip(IdleMediaLabel(_settings.IdleMediaMode), Theme.Accent);
            media.ToolTip = L.IdleMediaTip;
            media.Click += delegate
            {
                _settings.IdleMediaMode = (_settings.IdleMediaMode + 1) % 3;
                media.Label.Text = IdleMediaLabel(_settings.IdleMediaMode);
                _mediaStamps.Clear();
                SaveSoon();
            };
            body.Children.Add(MakeLabeledRow(L.IdleMedia, media));
            return WrapCard(body);
        }

        private static string IdleMediaLabel(int mode)
        {
            if (mode == IdleMediaIgnore) return L.IdleMediaIgnore;
            if (mode == IdleMediaKeepAll) return L.IdleMediaKeepAll;
            return L.IdleMediaOnly;
        }

        // ------------------------------------------------------------------ per-application profiles

        private UIElement BuildAppRulesCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeText(L.AppRules, 14.5, Theme.Text, FontWeights.SemiBold));
            body.Children.Add(MakeWrappedText(L.AppRulesHint));

            foreach (AppRule rule in _settings.AppRules)
                body.Children.Add(BuildAppRuleRow(rule));

            Grid addRow = new Grid();
            addRow.Margin = new Thickness(0, 12, 0, 0);
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox processBox = MakeTextBox("");
            addRow.Children.Add(processBox);

            string[] picked = new string[] { "" };
            FlatButton picker = MakeProfilePicker("", delegate(string chosen) { picked[0] = chosen; });
            picker.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(picker, 1);
            addRow.Children.Add(picker);

            FlatButton addButton = MakeChip(L.Add, Theme.Accent);
            addButton.Margin = new Thickness(6, 0, 0, 0);
            addButton.Click += delegate
            {
                string processName = processBox.Text.Trim();
                if (processName.Length == 0 || picked[0].Length == 0) return;
                AppRule added = new AppRule();
                added.Process = processName;
                added.Profile = picked[0];
                _settings.AppRules.Add(added);
                _lastForeground = IntPtr.Zero;
                SaveSoon();
                BuildSettingsView();
            };
            Grid.SetColumn(addButton, 2);
            addRow.Children.Add(addButton);
            body.Children.Add(addRow);
            return WrapCard(body);
        }

        private UIElement BuildAppRuleRow(AppRule rule)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 10, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock processText = MakeText(rule.Process, 13, Theme.Text, FontWeights.Normal);
            processText.VerticalAlignment = VerticalAlignment.Center;
            processText.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(processText);

            TextBlock profileText = MakeText(rule.Profile, 12, Theme.Accent, FontWeights.SemiBold);
            profileText.VerticalAlignment = VerticalAlignment.Center;
            profileText.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(profileText, 1);
            row.Children.Add(profileText);

            FlatButton deleteButton = MakeChip(L.Delete, Theme.Muted);
            deleteButton.Margin = new Thickness(8, 0, 0, 0);
            deleteButton.Click += delegate
            {
                _settings.AppRules.Remove(rule);
                RevalidateActiveRule();
                _lastForeground = IntPtr.Zero;
                SaveSoon();
                BuildSettingsView();
            };
            Grid.SetColumn(deleteButton, 2);
            row.Children.Add(deleteButton);
            return row;
        }

        // ------------------------------------------------------------------ about

        private UIElement BuildAboutCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(MakeText("Brighter " + AppInfo.VersionText, 14.5, Theme.Text, FontWeights.SemiBold));
            body.Children.Add(MakeWrappedText(L.AboutHint));

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.Margin = new Thickness(0, 10, 0, 0);

            FlatButton openLog = MakeChip(L.OpenLog, Theme.Text);
            openLog.Click += delegate
            {
                try
                {
                    if (System.IO.File.Exists(Log.FilePath))
                        System.Diagnostics.Process.Start(Log.FilePath);
                }
                catch (Exception)
                {
                }
            };
            buttons.Children.Add(openLog);

            if (_updateVersion != null)
            {
                FlatButton download = MakeChip(L.UpdateAvailable(_updateVersion), Theme.Accent);
                download.Margin = new Thickness(6, 0, 0, 0);
                download.Click += delegate
                {
                    try { System.Diagnostics.Process.Start(UpdateChecker.ReleasesUrl); }
                    catch (Exception) { }
                };
                buttons.Children.Add(download);
            }

            body.Children.Add(buttons);
            return WrapCard(body);
        }

        // ------------------------------------------------------------------ row builders

        private static UIElement MakeToggleRow(string title, string hint, bool isOn, Action<bool> changed)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel texts = new StackPanel();
            texts.Margin = new Thickness(0, 0, 12, 0);
            texts.Children.Add(MakeText(title, 13, Theme.Text, FontWeights.Normal));
            TextBlock hintText = MakeText(hint, 11.5, Theme.Muted, FontWeights.Normal);
            hintText.TextWrapping = TextWrapping.Wrap;
            texts.Children.Add(hintText);
            row.Children.Add(texts);

            ToggleSwitch toggle = new ToggleSwitch();
            toggle.IsOn = isOn;
            toggle.Toggled += delegate { changed(toggle.IsOn); };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        private static UIElement MakeLabeledRow(string caption, UIElement control)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 10, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock captionText = MakeText(caption, 12.5, Theme.Text, FontWeights.Normal);
            captionText.VerticalAlignment = VerticalAlignment.Center;
            captionText.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(captionText);

            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private static TextBlock MakeWrappedText(string text)
        {
            TextBlock block = MakeText(text, 12, Theme.Muted, FontWeights.Normal);
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        private TextBox MakeNumberBox(int value, int min, int max, Action<int> changed)
        {
            TextBox box = MakeTextBox(value.ToString());
            box.Width = 64;
            box.MaxLength = 3;
            box.TextAlignment = TextAlignment.Center;
            box.TextChanged += delegate
            {
                int parsed;
                bool valid = int.TryParse(box.Text.Trim(), out parsed) && parsed >= min && parsed <= max;
                box.Foreground = valid ? Theme.Text : Theme.CloseHover;
                if (!valid) return;
                changed(parsed);
                SaveSoon();
            };
            return box;
        }

        /// <summary>A chip that cycles through "none" and every profile each time it is clicked.</summary>
        private FlatButton MakeProfilePicker(string initial, Action<string> changed)
        {
            string selected = FindProfile(initial) != null ? initial : "";
            FlatButton chip = MakeChip(selected.Length > 0 ? selected : L.Profile + " \u2014", Theme.Accent);
            chip.MinWidth = 90;
            chip.Click += delegate
            {
                List<string> names = new List<string>();
                names.Add("");
                foreach (ProfileData profile in _settings.Profiles)
                    names.Add(profile.Name);
                int index = names.IndexOf(selected);
                selected = names[(index + 1) % names.Count];
                chip.Label.Text = selected.Length > 0 ? selected : L.Profile + " \u2014";
                changed(selected);
            };
            return chip;
        }
    }
}
