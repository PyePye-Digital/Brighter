using System;
using System.IO;
using System.Reflection;

namespace Brighter
{
    /// <summary>Version and project location.</summary>
    internal static class AppInfo
    {
        /// <summary>GitHub "owner/repo". Leave empty to switch the update check off.</summary>
        public static readonly string Repository = "PyePye-Digital/Brighter";

        public static string VersionText
        {
            get
            {
                System.Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }
    }

    /// <summary>Plain text log in %APPDATA%\Brighter\log.txt, so problems can be reported.</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static string FilePath
        {
            get { return Path.Combine(SettingsStore.Folder, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(SettingsStore.Folder);
                    FileInfo info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 512 * 1024) info.Delete(); // start over, never grow forever
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void Error(string where, Exception error)
        {
            Write("ERROR " + where + ": " + error.GetType().Name + ": " + error.Message);
        }
    }
}
