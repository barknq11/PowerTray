using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace PowerTray
{
    static class Updater
    {
        const string LatestApi = "https://api.github.com/repos/barknq11/PowerTray/releases/latest";
        public const string ReleasesPage = "https://github.com/barknq11/PowerTray/releases/latest";

        // The background thread only ever writes this field; the UI timer reads it.
        // Handing the result back through shared state rather than a callback avoids
        // marshalling onto the UI thread from a worker, which a tray app has no clean
        // way to do anyway (NotifyIcon has no Invoke).
        static volatile string available;

        public static string AvailableVersion { get { return available; } }

        public static void StartCheck()
        {
            if (!Config.CheckForUpdates) return;

            // An autostart app launches at every boot. Without a throttle that is a
            // request to GitHub every single time, and the API allows 60/hour per IP.
            if ((DateTime.UtcNow - Config.LastUpdateCheck).TotalHours < 24) return;

            var thread = new Thread(Run);
            thread.IsBackground = true;   // must never hold up shutdown
            thread.Start();
        }

        static void Run()
        {
            try
            {
                string latest = FetchLatestTag();
                Config.LastUpdateCheck = DateTime.UtcNow;

                if (latest != null && IsNewer(latest, Program.Version))
                    available = latest;
            }
            catch
            {
                // Offline, DNS failure, rate limited, GitHub down - all equally
                // uninteresting to someone who just wants to switch a power plan.
            }
        }

        static string FetchLatestTag()
        {
            // .NET Framework 4.0 defaults to TLS 1.0 and its SecurityProtocolType enum
            // predates Tls12, so the raw value has to be cast in. Omit this and every
            // request to GitHub fails while the code still looks perfectly correct.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }

            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "PowerTray/" + Program.Version);
                client.Headers.Add("Accept", "application/vnd.github+json");

                string json = client.DownloadString(LatestApi);

                // Framework 4.0 has no JSON parser worth referencing an extra assembly
                // for, and one well-known field out of a known response is not worth it.
                Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
        }

        public static bool IsNewer(string candidate, string current)
        {
            Version a, b;
            if (!Version.TryParse(Normalize(candidate), out a)) return false;
            if (!Version.TryParse(Normalize(current), out b)) return false;
            return a > b;
        }

        static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "0.0";

            raw = raw.Trim();
            if (raw.Length > 0 && (raw[0] == 'v' || raw[0] == 'V')) raw = raw.Substring(1);

            int dash = raw.IndexOf('-');          // drop prerelease suffixes
            if (dash >= 0) raw = raw.Substring(0, dash);

            if (raw.IndexOf('.') < 0) raw += ".0";
            return raw;
        }
    }
}
