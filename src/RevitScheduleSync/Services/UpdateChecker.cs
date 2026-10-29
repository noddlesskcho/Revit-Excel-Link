using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using RevitScheduleSync.App;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.Services
{
    /// <summary>A newer release published on GitHub.</summary>
    internal sealed record UpdateInfo(Version Version, string Url);

    /// <summary>
    /// Asks GitHub once per Revit session for the latest release of this add-in. Only the public
    /// "latest release" page is requested; nothing about the user, model or machine is sent.
    /// Every failure (offline, proxy, firewall) is logged and otherwise ignored.
    /// </summary>
    internal static class UpdateChecker
    {
        private static Task<UpdateInfo?>? _check;

        private static string NoticeFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitScheduleSync", "update-notice.txt");

        /// <summary>Starts the check in the background. Safe to call more than once.</summary>
        public static void Start(ILogger logger) => _check ??= Task.Run(() => CheckAsync(logger));

        /// <summary>The newer release, or null when up to date, still checking, or the check failed.</summary>
        public static UpdateInfo? Available =>
            _check is { Status: TaskStatus.RanToCompletion } ? _check.Result : null;

        /// <summary>Waits briefly for the result (used by the About window).</summary>
        public static async Task<(bool Checked, UpdateInfo? Update)> GetAsync(ILogger logger, TimeSpan timeout)
        {
            Start(logger);
            var check = _check!;
            var finished = await Task.WhenAny(check, Task.Delay(timeout)).ConfigureAwait(true);
            return finished == check && check.Status == TaskStatus.RanToCompletion ? (Checked, check.Result) : (false, null);
        }

        /// <summary>True when the check reached GitHub and read the latest release.</summary>
        private static bool Checked { get; set; }

        /// <summary>True the first time a given newer version is seen; later calls for the same version return false.</summary>
        public static bool ShouldNotify(UpdateInfo update)
        {
            try
            {
                if (File.Exists(NoticeFile) && File.ReadAllText(NoticeFile).Trim() == update.Version.ToString())
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(NoticeFile)!);
                File.WriteAllText(NoticeFile, update.Version.ToString());
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<UpdateInfo?> CheckAsync(ILogger logger)
        {
            try
            {
#if NETFRAMEWORK
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
#endif
                // github.com/<repo>/releases/latest redirects to .../releases/tag/<tag>. Reading the redirect
                // avoids the REST API, whose 60 requests/hour limit is shared by everyone behind one office IP.
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"RevitExcelLink/{PluginInfo.NumericVersion}");

                using var response = await http.GetAsync(PluginInfo.LatestReleaseUrl).ConfigureAwait(false);
                var location = response.Headers.Location;
                if (location == null)
                {
                    logger.Info($"Update check: no release redirect (HTTP {(int)response.StatusCode}).");
                    return null;
                }

                var url = (location.IsAbsoluteUri ? location : new Uri(new Uri(PluginInfo.LatestReleaseUrl), location)).ToString();
                var tag = url.Substring(url.LastIndexOf('/') + 1);
                Checked = true;

                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                {
                    logger.Warning($"Update check: unrecognised release tag '{tag}'.");
                    return null;
                }

                var current = PluginInfo.NumericVersion;
                logger.Info($"Update check: installed {current}, latest {latest}.");
                return latest > current ? new UpdateInfo(latest, url ?? PluginInfo.LatestReleaseUrl) : null;
            }
            catch (Exception ex)
            {
                logger.Info($"Update check skipped: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
