using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Brighter
{
    internal static class Program
    {
        private const string ShowEventName = "Brighter.Show.6f1d7c2e";
        private const string CommandEventName = "Brighter.Command.6f1d7c2e";
        private const string ExitEventName = "Brighter.Exit.6f1d7c2e";
        private const string RunningMutexName = "Brighter.Running.6f1d7c2e";

        [STAThread]
        private static void Main(string[] args)
        {
            // "--exit" is used by the installer and uninstaller: close a running copy properly, so the
            // colour temperature is reset and displays turned off from the app come back.
            if (Array.IndexOf(args, "--exit") >= 0)
            {
                ExitRunningInstance();
                return;
            }

            // Command-line use (see CommandLine.cs): hand over to a running instance, or do it and exit.
            List<Command> commands = CommandLine.Parse(args);
            if (commands.Count > 0)
            {
                RunCommands(args, commands);
                return;
            }

            // Single instance: a second launch just asks the running one to show its window.
            bool createdNew;
            EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out createdNew);
            if (!createdNew)
            {
                showSignal.Set();
                return;
            }
            EventWaitHandle commandSignal = new EventWaitHandle(false, EventResetMode.AutoReset, CommandEventName);
            EventWaitHandle exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            Mutex running = new Mutex(true, RunningMutexName); // released when the app ends; "--exit" waits for it

            // "--tray" is passed by the Start-with-Windows entry: start hidden in the notification area.
            bool startHidden = Array.IndexOf(args, "--tray") >= 0;

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // A tray tool should survive a bug in one feature: log it and carry on.
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Write("FATAL " + e.ExceptionObject);
            };
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Log.Write("ERROR ui: " + e.Exception);
                e.Handled = true;
            };
            Log.Write("Brighter " + AppInfo.VersionText + " started");

            MainWindow window = new MainWindow();
            new WindowInteropHelper(window).EnsureHandle(); // hotkeys need a window handle even while hidden

            Thread listener = new Thread(delegate()
            {
                try
                {
                    WaitHandle[] handles = new WaitHandle[] { showSignal, commandSignal, exitSignal };
                    while (true)
                    {
                        int which = WaitHandle.WaitAny(handles);
                        if (which == 0)
                            window.Dispatcher.BeginInvoke(new Action(window.ShowFromOutside));
                        else if (which == 1)
                            window.Dispatcher.BeginInvoke(new Action(window.RunCommandFile));
                        else
                            window.Dispatcher.BeginInvoke(new Action(window.ExitFromOutside));
                    }
                }
                catch (Exception)
                {
                }
            });
            listener.IsBackground = true;
            listener.Start();

            if (!startHidden)
                window.Show();

            app.Run();
            try { running.ReleaseMutex(); }
            catch (Exception) { }
            GC.KeepAlive(exitSignal);
            GC.KeepAlive(showSignal);
            GC.KeepAlive(commandSignal);
        }

        /// <summary>Asks a running Brighter to quit and waits (up to 10 s) until it has.</summary>
        private static void ExitRunningInstance()
        {
            EventWaitHandle exitSignal;
            try
            {
                exitSignal = EventWaitHandle.OpenExisting(ExitEventName);
            }
            catch (Exception)
            {
                return; // not running
            }

            Mutex running = null;
            try { running = Mutex.OpenExisting(RunningMutexName); }
            catch (Exception) { }

            exitSignal.Set();
            if (running == null)
            {
                Thread.Sleep(2000);
                return;
            }
            try
            {
                if (running.WaitOne(10000)) running.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                // the other process ended without releasing it: it is gone, which is what we wanted
            }
            catch (Exception)
            {
            }
        }

        private static void RunCommands(string[] args, List<Command> commands)
        {
            EventWaitHandle running = null;
            try
            {
                running = EventWaitHandle.OpenExisting(CommandEventName);
            }
            catch (Exception)
            {
                running = null; // not running
            }

            if (running != null)
            {
                try
                {
                    Directory.CreateDirectory(SettingsStore.Folder);
                    File.WriteAllLines(CommandLine.CommandFile, args);
                    running.Set();
                }
                catch (Exception)
                {
                }
                return;
            }

            try
            {
                CommandLine.RunHeadless(commands);
            }
            catch (Exception)
            {
            }
        }
    }
}
