using System;
using System.Collections.Generic;
using System.Reflection;

namespace Brighter
{
    /// <summary>
    /// Notification-area icon. WPF has no tray icon of its own, so this borrows the WinForms one.
    /// WinForms types are written out in full here to keep them away from the WPF namespaces.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly System.Windows.Forms.ContextMenu _menu;

        public event EventHandler Open;
        public event EventHandler Exit;
        public event EventHandler SleepAll;
        public event Action<string> ProfileChosen;

        /// <summary>Supplies the profile names shown in the right-click menu.</summary>
        public Func<List<string>> ProfileNames;

        public TrayIcon()
        {
            _icon = new System.Windows.Forms.NotifyIcon();
            _icon.Text = "Brighter";
            try
            {
                _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception)
            {
                _icon.Icon = System.Drawing.SystemIcons.Application;
            }

            _menu = new System.Windows.Forms.ContextMenu();
            _menu.Popup += delegate(object sender, EventArgs e) { Rebuild(); }; // profiles may have changed
            Rebuild();
            _icon.ContextMenu = _menu;

            _icon.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    Raise(Open);
            };
            _icon.Visible = true;
        }

        private void Rebuild()
        {
            _menu.MenuItems.Clear();
            _menu.MenuItems.Add(L.Open, delegate(object sender, EventArgs e) { Raise(Open); });

            List<string> names = ProfileNames != null ? ProfileNames() : new List<string>();
            if (names.Count > 0)
            {
                _menu.MenuItems.Add("-");
                foreach (string name in names)
                {
                    string captured = name;
                    _menu.MenuItems.Add(name, delegate(object sender, EventArgs e)
                    {
                        Action<string> handler = ProfileChosen;
                        if (handler != null) handler(captured);
                    });
                }
            }

            _menu.MenuItems.Add("-");
            _menu.MenuItems.Add(L.SleepAll, delegate(object sender, EventArgs e) { Raise(SleepAll); });
            _menu.MenuItems.Add("-");
            _menu.MenuItems.Add(L.Exit, delegate(object sender, EventArgs e) { Raise(Exit); });
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
