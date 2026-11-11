using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Brighter
{
    /// <summary>Asks GitHub Releases whether a newer version exists. Does nothing until AppInfo.Repository is set.</summary>
    internal static class UpdateChecker
    {
        public static string ReleasesUrl
        {
            get { return "https://github.com/" + AppInfo.Repository + "/releases/latest"; }
        }

        /// <summary>Calls <paramref name="found"/> (on a worker thread) with the newer version number, if there is one.</summary>
        public static void CheckAsync(Action<string> found)
        {
            if (string.IsNullOrEmpty(AppInfo.Repository)) return;

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest");
                    request.UserAgent = "Brighter/" + AppInfo.VersionText;
                    request.Accept = "application/vnd.github+json";
                    request.Timeout = 8000;

                    string body;
                    using (WebResponse response = request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        body = reader.ReadToEnd();
                    }

                    Match match = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(\\.[0-9]+)*)\"");
                    if (!match.Success) return;

                    string tag = match.Groups[1].Value;
                    System.Version latest = new System.Version(tag.IndexOf('.') < 0 ? tag + ".0" : tag);
                    System.Version current = new System.Version(AppInfo.VersionText);
                    if (latest > current) found(tag);
                }
                catch (Exception error)
                {
                    Log.Error("update check", error);
                }
            });
        }
    }
}
