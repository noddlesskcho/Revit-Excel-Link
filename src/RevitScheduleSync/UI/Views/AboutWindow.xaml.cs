using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using RevitScheduleSync.App;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.Views
{
    /// <summary>Version, support contact, update status and diagnostics. No view model: it only shows facts.</summary>
    public partial class AboutWindow : Window
    {
        private readonly string _host;
        private readonly string _addinFolder;
        private readonly string? _bundleFolder;
        private string _downloadUrl = PluginInfo.LatestReleaseUrl;

        public AboutWindow(string revitHost)
        {
            InitializeComponent();

            _host = $"{revitHost}, {RuntimeInformation.FrameworkDescription}";
            _addinFolder = Path.GetDirectoryName(typeof(AboutWindow).Assembly.Location)!;
            _bundleFolder = FindBundleFolder(_addinFolder);

            Logo.Source = RibbonIcons.Create(48);
            VersionText.Text = PluginInfo.Version;
            HostText.Text = _host;
            FolderText.Text = _bundleFolder ?? _addinFolder;

            Loaded += async (_, _) =>
            {
                var (reached, update) = await UpdateChecker.GetAsync(AddinHost.Logger, TimeSpan.FromSeconds(8));
                if (update != null)
                {
                    _downloadUrl = update.Url;
                    UpdateText.Text = $"Version {update.Version} is available.";
                    DownloadButton.Visibility = Visibility.Visible;
                }
                else
                {
                    UpdateText.Text = reached ? "You have the latest version." : "Could not check (offline?).";
                    DownloadButton.Content = "Check on GitHub";
                    DownloadButton.Visibility = Visibility.Visible;
                }
            };
        }

        private string Diagnostics()
        {
            var text = new StringBuilder();
            text.AppendLine($"{PluginInfo.ProductName} {PluginInfo.Version}");
            text.AppendLine($"Host: {_host}");
            text.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})");
            text.AppendLine($"Installed in: {_bundleFolder ?? _addinFolder}");
            text.AppendLine($"Logs: {FileLogger.DefaultLogDirectory}");
            return text.ToString();
        }

        private void OnEmail(object sender, RoutedEventArgs e)
        {
            var subject = $"{PluginInfo.ProductName} {PluginInfo.NumericVersion}: problem report";
            var body = "Describe the problem and the steps to reproduce it:" + Environment.NewLine + Environment.NewLine +
                       Environment.NewLine + "----" + Environment.NewLine + Diagnostics();
            Open($"mailto:{PluginInfo.SupportEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
        }

        private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Diagnostics());
                CopiedText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AddinHost.Logger.Warning("Could not copy diagnostics to the clipboard.", ex);
            }
        }

        private void OnOpenLogs(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(FileLogger.DefaultLogDirectory);
            Open(FileLogger.DefaultLogDirectory);
        }

        private void OnDownload(object sender, RoutedEventArgs e) => Open(_downloadUrl);

        private void OnWebsite(object sender, RoutedEventArgs e) => Open(PluginInfo.RepositoryUrl);

        private void OnHelp(object sender, RoutedEventArgs e) =>
            Open(BundleFile(@"Contents\Resources\help.html") ?? PluginInfo.RepositoryUrl + "#readme");

        private void OnNotices(object sender, RoutedEventArgs e) =>
            Open(BundleFile(@"Contents\THIRD-PARTY-NOTICES.txt") ?? PluginInfo.RepositoryUrl + "/blob/main/packaging/THIRD-PARTY-NOTICES.txt");

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private string? BundleFile(string relative)
        {
            var path = _bundleFolder == null ? null : Path.Combine(_bundleFolder, relative);
            return path != null && File.Exists(path) ? path : null;
        }

        /// <summary>The .bundle folder (holds PackageContents.xml) above the add-in DLL, or null for a developer install.</summary>
        private static string? FindBundleFolder(string start)
        {
            for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "PackageContents.xml")))
                {
                    return folder.FullName;
                }
            }

            return null;
        }

        private static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddinHost.Logger.Warning($"Could not open '{target}'.", ex);
                MessageBox.Show($"Could not open:\n{target}", PluginInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
