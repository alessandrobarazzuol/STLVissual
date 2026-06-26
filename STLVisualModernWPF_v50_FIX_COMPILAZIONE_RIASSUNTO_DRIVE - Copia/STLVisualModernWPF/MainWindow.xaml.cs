using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace STLVisualModernWPF
{
    public partial class MainWindow : Window
    {
        private const string Password = "20242lbg";
        private const string GitHubOwner = "alessandrobarazzuol";
        private const string GitHubRepo = "STLVissual";
        private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/alessandrobarazzuol/STLVissual/releases/latest";
        private readonly string AppFolder;
        private readonly string GuidesFile;
        private readonly string ConfigFile;
        private readonly string ExercisesFolder;
        private readonly string SummariesFile;
        private readonly string GitHubUpdateStateFile;
        private readonly string GoogleCredentialsFile;
        private readonly string GoogleTokenFolder;
        private const string GoogleDriveBackupFolderName = "STLVisualModernWPF_Alessandro_Barazzoli";
        private const string GoogleDriveBackupFileName = "guide_ed_esercizi_STLVisualModernWPF.json";

        private string current = "list";
        private int nextValue = 1;
        private int nextKey = 1;
        private string currentGuideKey = "list:Panoramica";
        private string? currentLoadedExerciseFile = null;
        private DispatcherTimer? driveAutoSyncTimer;
        private bool driveAutoSyncRunning = false;
        private DateTime lastDriveAutoImportUtc = DateTime.MinValue;

        private readonly List<int> listValues = new();
        private readonly List<int> vectorValues = new();
        private readonly SortedSet<int> setValues = new();
        private readonly Stack<int> stackValues = new();
        private readonly Queue<int> queueValues = new();
        private readonly SortedDictionary<int, int> mapValues = new();

        private Dictionary<string, string> customGuides = new();
        private Dictionary<string, SummaryNote> containerSummaries = new();
        private int activeVisualIndex = -1;
        private readonly HashSet<int> activeVisualIndexes = new();
        private readonly Dictionary<int, string> activeVisualLabels = new();

        public MainWindow()
        {
            AppFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STLVisualModernWPF");
            Directory.CreateDirectory(AppFolder);
            GuidesFile = System.IO.Path.Combine(AppFolder, "guide_personalizzate.json");
            ConfigFile = System.IO.Path.Combine(AppFolder, "config.json");
            SummariesFile = System.IO.Path.Combine(AppFolder, "riassunti_contenitori.json");
            GitHubUpdateStateFile = System.IO.Path.Combine(AppFolder, "github_update_state.json");
            GoogleCredentialsFile = System.IO.Path.Combine(AppFolder, "credentials.json");
            GoogleTokenFolder = System.IO.Path.Combine(AppFolder, "GoogleDriveOAuthToken");
            ExercisesFolder = System.IO.Path.Combine(AppFolder, "esercizi_salvati");
            Directory.CreateDirectory(ExercisesFolder);

            if (!AskPassword())
            {
                Close();
                return;
            }

            InitializeComponent();
            LoadGuides();
            LoadSummaries();
            LoadConfig();
            BuildMethodButtons();
            SelectContainer("list");
            SetCppEditorText(DefaultCppExample());
            RefreshSavedExercisesList();
            RegisterEditableShiftClickPopups();
            StartDriveAutoSync();
        }


        private async void CheckGitHubUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckGitHubUpdatesAsync();
        }

        private async Task CheckGitHubUpdatesAsync()
        {
            try
            {
                BtnCercaAggiornamenti.IsEnabled = false;
                BtnCercaAggiornamenti.Content = "Controllo...";

                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("STLVisualModernWPF-Updater/1.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                string json = await http.GetStringAsync(GitHubLatestReleaseApiUrl);
                var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (release == null)
                {
                    MessageBox.Show("Non riesco a leggere l'ultima release da GitHub.", "Aggiornamenti GitHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var asset = release.Assets?
                    .Where(a => !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
                    .OrderByDescending(a => IsInstallerAsset(a.Name))
                    .ThenByDescending(a => a.Size)
                    .FirstOrDefault(a => IsInstallerAsset(a.Name));

                if (asset == null)
                {
                    MessageBox.Show(
                        "Ho trovato l'ultima release su GitHub, ma non contiene un file di installazione .exe o .zip.",
                        "Aggiornamenti GitHub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string localVersion = GetLocalVersionText();
                string releaseTitle = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name;
                string sizeText = FormatBytes(asset.Size);

                if (IsGitHubReleaseAlreadyInstalled(release, asset))
                {
                    MessageBox.Show(
                        "Il programma è già aggiornato.\n\n" +
                        $"Versione installata: {localVersion}\n" +
                        $"Ultima release GitHub: {releaseTitle}\n" +
                        $"Tag: {release.TagName}",
                        "Cerca aggiornamenti",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string message =
                    "Nuova versione disponibile su GitHub:\n\n" +
                    $"Release: {releaseTitle}\n" +
                    $"Tag: {release.TagName}\n" +
                    $"Versione installata: {localVersion}\n" +
                    $"File: {asset.Name}\n" +
                    $"Dimensione: {sizeText}\n\n" +
                    "Vuoi scaricare e avviare l'installazione?";

                var answer = MessageBox.Show(message, "Cerca aggiornamenti", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "STLVisualModernWPF_Update");
                Directory.CreateDirectory(tempFolder);
                string downloadPath = System.IO.Path.Combine(tempFolder, MakeSafeFileName(asset.Name));

                BtnCercaAggiornamenti.Content = "Download...";
                await DownloadFileAsync(asset.BrowserDownloadUrl!, downloadPath);

                MessageBox.Show(
                    "Download completato. Ora verrà avviato il file di installazione.\n\n" + downloadPath,
                    "Aggiornamento scaricato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                SaveInstalledGitHubReleaseState(release, asset);
                LaunchInstallerAndCloseApp(downloadPath);
                return;
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show("Errore di rete durante il controllo aggiornamenti:\n\n" + ex.Message, "Aggiornamenti GitHub", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il controllo aggiornamenti:\n\n" + ex.Message, "Aggiornamenti GitHub", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCercaAggiornamenti.IsEnabled = true;
                BtnCercaAggiornamenti.Content = "⬇ AGGIORNAMENTI";
            }
        }


        private bool IsGitHubReleaseAlreadyInstalled(GitHubReleaseInfo release, GitHubReleaseAsset asset)
        {
            var state = LoadGitHubUpdateState();
            string? releaseTag = NormalizeText(release.TagName);
            string? assetName = NormalizeText(asset.Name);

            if (!string.IsNullOrWhiteSpace(releaseTag) &&
                string.Equals(NormalizeText(state?.InstalledTagName), releaseTag, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(assetName) &&
                string.Equals(NormalizeText(state?.InstalledAssetName), assetName, StringComparison.OrdinalIgnoreCase))
                return true;

            Version? remoteVersion = ExtractVersionFromRelease(release, asset);
            Version? localVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (remoteVersion != null && localVersion != null && localVersion >= remoteVersion)
                return true;

            return false;
        }

        private void SaveInstalledGitHubReleaseState(GitHubReleaseInfo release, GitHubReleaseAsset asset)
        {
            try
            {
                var state = new GitHubUpdateState
                {
                    InstalledTagName = release.TagName,
                    InstalledReleaseName = release.Name,
                    InstalledAssetName = asset.Name,
                    InstalledAt = DateTimeOffset.Now,
                    InstalledAssemblyVersion = GetLocalVersionText()
                };

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GitHubUpdateStateFile, json, Encoding.UTF8);
            }
            catch
            {
                // Non bloccare l'aggiornamento se non riesco a salvare lo stato.
            }
        }

        private GitHubUpdateState? LoadGitHubUpdateState()
        {
            try
            {
                if (!File.Exists(GitHubUpdateStateFile)) return null;
                string json = File.ReadAllText(GitHubUpdateStateFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<GitHubUpdateState>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static Version? ExtractVersionFromRelease(GitHubReleaseInfo release, GitHubReleaseAsset asset)
        {
            string text = string.Join(" ", new[] { release.Name, release.TagName, asset.Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var match = Regex.Match(text, @"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?!\d)");
            if (!match.Success) return null;

            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            int revision = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
            return new Version(major, minor, build, revision);
        }

        private static bool IsInstallerAsset(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.EndsWith(".exe") || lower.EndsWith(".zip") || lower.Contains("setup") || lower.Contains("installer") || lower.Contains("install");
        }

        private static string GetLocalVersionText()
        {
            try
            {
                Version? v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "non disponibile" : v.ToString();
            }
            catch
            {
                return "non disponibile";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "dimensione non disponibile";
            double mb = bytes / 1024d / 1024d;
            return mb >= 1 ? $"{mb:0.0} MB" : $"{bytes / 1024d:0.0} KB";
        }

        private static string MakeSafeFileName(string fileName)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            return fileName;
        }

        private static async Task DownloadFileAsync(string url, string destinationPath)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("STLVisualModernWPF-Updater/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(destinationPath);
            await input.CopyToAsync(output);
        }

        private static void LaunchInstallerAndCloseApp(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                MessageBox.Show("Il file di installazione scaricato non è stato trovato.", "Aggiornamento", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string tempBatPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "STLVisualModernWPF_AvviaAggiornamento.bat");

            int currentPid = Process.GetCurrentProcess().Id;
            string escapedInstallerPath = installerPath.Replace("\"", "\"\"");

            string bat =
                "@echo off\r\n" +
                "setlocal\r\n" +
                $"set \"INSTALLER={escapedInstallerPath}\"\r\n" +
                $"set \"APP_PID={currentPid}\"\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                ":WAIT_APP_CLOSE\r\n" +
                "tasklist /FI \"PID eq %APP_PID%\" | find \"%APP_PID%\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto WAIT_APP_CLOSE\r\n" +
                ")\r\n" +
                "start \"\" \"%INSTALLER%\"\r\n" +
                "endlocal\r\n" +
                "del \"%~f0\" >nul 2>nul\r\n";

            File.WriteAllText(tempBatPath, bat, Encoding.Default);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempBatPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Current.Shutdown();
        }

        private sealed class GitHubUpdateState
        {
            public string? InstalledTagName { get; set; }
            public string? InstalledReleaseName { get; set; }
            public string? InstalledAssetName { get; set; }
            public DateTimeOffset? InstalledAt { get; set; }
            public string? InstalledAssemblyVersion { get; set; }
        }

        private sealed class GitHubReleaseInfo
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("published_at")]
            public DateTimeOffset? PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubReleaseAsset>? Assets { get; set; }
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }



        private void StartDriveAutoSync()
        {
            // Sincronizzazione automatica: dopo il primo salvataggio su Drive,
            // gli altri PC importano periodicamente il database senza premere IMPORTA.
            driveAutoSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            driveAutoSyncTimer.Tick += async (_, _) => await AutoImportFromDriveIfAvailableAsync();
            driveAutoSyncTimer.Start();
            _ = AutoImportFromDriveIfAvailableAsync();
        }

        private async Task AutoImportFromDriveIfAvailableAsync()
        {
            if (driveAutoSyncRunning) return;
            if (!File.Exists(GoogleCredentialsFile)) return;

            // Evita import continui durante modifiche ravvicinate.
            if ((DateTime.UtcNow - lastDriveAutoImportUtc).TotalSeconds < 45) return;

            try
            {
                driveAutoSyncRunning = true;
                bool imported = await TryImportDatabaseFromGoogleDriveOAuthAsync(showMessages: false);
                if (imported)
                    lastDriveAutoImportUtc = DateTime.UtcNow;
            }
            catch
            {
                // Silenzioso: non disturba la lezione se manca rete o Drive non risponde.
            }
            finally
            {
                driveAutoSyncRunning = false;
            }
        }

        private void RegisterEditableShiftClickPopups()
        {
            RegisterEditablePopup(GuideTextBox, "Guida / metodo");
            RegisterEditablePopup(ExerciseTextBox, "Testo esercizio / consegna");
            RegisterEditablePopup(GeneratedCodeBox, "Codice C++ generato");
            RegisterEditablePopup(CppEditorBox, "Editor C++");
            RegisterEditablePopup(TreeOutputBox, "Risultato visite / guida albero");
            RegisterTreePopup(ExerciseTree, "Albero file system / esercizi salvati");
        }

        private void RegisterEditablePopup(TextBox box, string title)
        {
            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift) return;
                e.Handled = true;
                string? edited = ShowEditablePopup(title, box.Text, box.FontFamily, box.FontSize, box.TextWrapping == TextWrapping.NoWrap);
                if (edited != null) box.Text = edited;
            };
        }

        private void RegisterEditablePopup(RichTextBox box, string title)
        {
            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift) return;
                e.Handled = true;
                string original = new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
                string? edited = ShowEditablePopup(title, original, box.FontFamily, box.FontSize, true);
                if (edited != null)
                {
                    box.Document.Blocks.Clear();
                    box.Document.Blocks.Add(new Paragraph(new Run(edited)));
                }
            };
        }

        private void RegisterTreePopup(TreeView tree, string title)
        {
            tree.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift) return;
                e.Handled = true;
                ShowExerciseTreePopup(title);
            };
        }

        private void ShowExerciseTreePopup(string title)
        {
            Window popup = new()
            {
                Title = "Pop-up file system - " + title,
                Owner = this,
                Width = Math.Min(SystemParameters.WorkArea.Width * 0.82, 1100),
                Height = Math.Min(SystemParameters.WorkArea.Height * 0.82, 760),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };

            Grid grid = new() { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock info = new()
            {
                Text = "File system esercizi ingrandito. Fai doppio clic su un file esercizio per importarlo/caricarlo nel programma.",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TreeView bigTree = new()
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 16, 32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                FontSize = 18,
                Padding = new Thickness(8)
            };

            // Forza colori chiari nel pop-up: alcune installazioni di Windows/WPF
            // applicano il testo nero ai TreeViewItem e quindi l'albero diventa invisibile.
            Style popupTreeItemStyle = new(typeof(TreeViewItem));
            popupTreeItemStyle.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, Brushes.White));
            popupTreeItemStyle.Setters.Add(new Setter(TreeViewItem.BackgroundProperty, (Brush)Brushes.Transparent));
            popupTreeItemStyle.Setters.Add(new Setter(TreeViewItem.FontSizeProperty, 18.0));
            popupTreeItemStyle.Setters.Add(new Setter(TreeViewItem.PaddingProperty, new Thickness(4, 3, 4, 3)));
            bigTree.ItemContainerStyle = popupTreeItemStyle;

            if (!Directory.Exists(ExercisesFolder)) Directory.CreateDirectory(ExercisesFolder);
            var root = new TreeViewItem { Header = MakeTreeHeader("📁 esercizi_salvati", Brushes.White), Tag = ExercisesFolder, IsExpanded = true, Foreground = Brushes.White };
            bigTree.Items.Add(root);
            AddExerciseTreeItems(root, ExercisesFolder, Brushes.White);

            bigTree.MouseDoubleClick += (_, e) =>
            {
                if (bigTree.SelectedItem is not TreeViewItem item || item.Tag is not string path) return;
                if (Directory.Exists(path) || !File.Exists(path)) return;

                LoadExerciseFromFile(path, showMessage: false);
                currentLoadedExerciseFile = path;
                e.Handled = true;
                popup.Close();
            };

            Button close = new()
            {
                Content = "Chiudi",
                Width = 110,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            close.Click += (_, __) => popup.Close();

            Grid.SetRow(info, 0);
            Grid.SetRow(bigTree, 1);
            Grid.SetRow(close, 2);
            grid.Children.Add(info);
            grid.Children.Add(bigTree);
            grid.Children.Add(close);
            popup.Content = grid;
            popup.ShowDialog();
            RefreshSavedExercisesList();
        }

        private string TreeViewToText(TreeView tree)
        {
            StringBuilder sb = new();
            foreach (object item in tree.Items) AppendTreeItemText(sb, item, 0);
            return sb.Length == 0 ? "Nessun elemento nell'albero." : sb.ToString();
        }

        private void AppendTreeItemText(StringBuilder sb, object item, int level)
        {
            if (item is TreeViewItem tvi)
            {
                sb.Append(new string(' ', level * 2));
                sb.AppendLine(tvi.Header?.ToString() ?? "");
                foreach (object child in tvi.Items) AppendTreeItemText(sb, child, level + 1);
            }
            else
            {
                sb.Append(new string(' ', level * 2));
                sb.AppendLine(item?.ToString() ?? "");
            }
        }

        private string? ShowEditablePopup(string title, string text, FontFamily fontFamily, double fontSize, bool noWrap)
        {
            Window popup = new()
            {
                Title = "Pop-up modifica - " + title,
                Owner = this,
                Width = Math.Min(SystemParameters.WorkArea.Width * 0.82, 1150),
                Height = Math.Min(SystemParameters.WorkArea.Height * 0.82, 760),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };

            Grid grid = new() { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock info = new()
            {
                Text = "Puoi leggere e modificare il contenuto. Puoi selezionare una parte e usare i pulsanti per ingrandire, evidenziare o colorare. Premi OK per riportare il testo nella casella originale.",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            RichTextBox editor = new()
            {
                AcceptsTab = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = fontFamily,
                FontSize = Math.Max(18, fontSize + 4),
                Background = new SolidColorBrush(Color.FromRgb(11, 16, 32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Padding = new Thickness(10),
                SpellCheck = { IsEnabled = false }
            };
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new Run(text ?? "")));

            ToolBar toolbar = new() { Margin = new Thickness(0, 0, 0, 8) };
            Button bigger = new() { Content = "A+", Width = 45, Margin = new Thickness(2) };
            Button smaller = new() { Content = "A-", Width = 45, Margin = new Thickness(2) };
            Button yellow = new() { Content = "Evidenzia", Width = 85, Margin = new Thickness(2) };
            Button white = new() { Content = "Bianco", Width = 65, Margin = new Thickness(2) };
            Button black = new() { Content = "Nero", Width = 55, Margin = new Thickness(2) };
            Button red = new() { Content = "Rosso", Width = 60, Margin = new Thickness(2) };
            Button blue = new() { Content = "Blu", Width = 50, Margin = new Thickness(2) };
            Button clear = new() { Content = "Pulisci formato", Width = 110, Margin = new Thickness(2) };
            toolbar.Items.Add(bigger); toolbar.Items.Add(smaller); toolbar.Items.Add(new Separator());
            toolbar.Items.Add(yellow); toolbar.Items.Add(white); toolbar.Items.Add(black); toolbar.Items.Add(red); toolbar.Items.Add(blue); toolbar.Items.Add(new Separator()); toolbar.Items.Add(clear);

            bigger.Click += (_, __) => ApplySelectionFontDelta(editor, 4);
            smaller.Click += (_, __) => ApplySelectionFontDelta(editor, -2);
            yellow.Click += (_, __) => editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Gold);
            white.Click += (_, __) => editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            black.Click += (_, __) => editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Black);
            red.Click += (_, __) => editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.OrangeRed);
            blue.Click += (_, __) => editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.DeepSkyBlue);
            clear.Click += (_, __) =>
            {
                editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Max(18, fontSize + 4));
                editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
                editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            };

            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Button ok = new() { Content = "OK - salva testo", Width = 150, Height = 36, Margin = new Thickness(6, 0, 0, 0) };
            Button cancel = new() { Content = "Annulla", Width = 110, Height = 36, Margin = new Thickness(6, 0, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            Grid.SetRow(info, 0);
            Grid.SetRow(toolbar, 1);
            Grid.SetRow(editor, 2);
            Grid.SetRow(buttons, 3);
            grid.Children.Add(info);
            grid.Children.Add(toolbar);
            grid.Children.Add(editor);
            grid.Children.Add(buttons);
            popup.Content = grid;

            string? result = null;
            ok.Click += (_, __) => { result = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd('\r', '\n'); popup.DialogResult = true; popup.Close(); };
            cancel.Click += (_, __) => { popup.DialogResult = false; popup.Close(); };
            popup.ShowDialog();
            return result;
        }

        private void ApplySelectionFontDelta(RichTextBox editor, double delta)
        {
            object currentSize = editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            double size = currentSize is double d ? d : editor.FontSize;
            editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Max(8, size + delta));
        }

        private bool AskPassword()
        {
            for (int i = 0; i < 3; i++)
            {
                var dialog = new PasswordWindow();
                bool? res = dialog.ShowDialog();
                if (res == true && dialog.PasswordValue == Password)
                    return true;

                MessageBox.Show("Password non valida.", "Accesso negato", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        private void ContainerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == BtnList) SelectContainer("list");
            if (sender == BtnVector) SelectContainer("vector");
            if (sender == BtnSet) SelectContainer("set");
            if (sender == BtnStack) SelectContainer("stack");
            if (sender == BtnQueue) SelectContainer("queue");
            if (sender == BtnMap) SelectContainer("map");
        }

        private void SelectContainer(string name)
        {
            current = name;
            ClearVisualHighlights();
            ContainerTitle.Text = $"std::{name}";
            BuildMethodButtons();
            HighlightMenu();
            ShowGuide("Panoramica", OverviewText());
            BuildOverloadButtons("Panoramica");
            DrawContainer();
        }

        private void HighlightMenu()
        {
            foreach (var b in new[] { BtnList, BtnVector, BtnSet, BtnStack, BtnQueue, BtnMap })
                b.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            Button selected = current switch
            {
                "list" => BtnList,
                "vector" => BtnVector,
                "set" => BtnSet,
                "stack" => BtnStack,
                "queue" => BtnQueue,
                _ => BtnMap
            };
            selected.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204));
        }

        private void BuildMethodButtons()
        {
            MethodsGrid.Children.Clear();

            string[] labels = current switch
            {
                "list" => new[] { "push_back", "push_front", "insert", "erase", "pop_back", "pop_front", "remove", "clear", "front / back", "size / empty", "sort/reverse", "unique" },
                "vector" => new[] { "push_back", "emplace_back", "insert", "erase", "pop_back", "clear", "front / back", "at / operator[]", "size/capacity", "resize", "sort", "reverse" },
                "set" => new[] { "insert", "emplace", "erase", "find", "count", "clear", "begin / end", "lower_bound", "upper_bound", "size / empty", "contains", "reset" },
                "stack" => new[] { "push", "emplace", "pop", "top", "size", "empty", "reset", "-", "-", "-", "-", "-" },
                "queue" => new[] { "push", "emplace", "pop", "front", "back", "size", "empty", "reset", "-", "-", "-", "-" },
                _ => new[] { "insert", "emplace", "operator[]", "at/find", "erase", "clear", "begin / end", "size / empty", "count", "lower_bound", "upper_bound", "reset" }
            };

            foreach (string label in labels)
            {
                var btn = new Button
                {
                    Content = label,
                    IsEnabled = label != "-",
                    Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
                    Foreground = Brushes.White,
                    Margin = new Thickness(3),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 12,
                    MinHeight = 30
                };
                btn.Click += (_, _) => RunMethod(label);
                MethodsGrid.Children.Add(btn);
            }
        }

        private void ClearVisualHighlights()
        {
            activeVisualIndex = -1;
            activeVisualIndexes.Clear();
            activeVisualLabels.Clear();
        }

        private void HighlightVisual(int index, string label)
        {
            if (index < 0) return;
            activeVisualIndex = index;
            activeVisualIndexes.Add(index);
            activeVisualLabels[index] = label;
        }

        private void HighlightFirstLast(int count, string firstLabel, string lastLabel)
        {
            if (count <= 0) return;
            HighlightVisual(0, firstLabel);
            if (count > 1) HighlightVisual(count - 1, lastLabel);
        }

        private string? ApplyReadHighlight(string method)
        {
            ClearVisualHighlights();

            if (current == "list")
            {
                if (method == "front / back")
                {
                    if (listValues.Count == 0) return "La lista è vuota: front() e back() non si possono usare.";
                    HighlightFirstLast(listValues.Count, "front()", "back()");
                    return listValues.Count == 1
                        ? $"front() e back() restituiscono lo stesso nodo: {listValues[0]}."
                        : $"front() restituisce {listValues.First()} e back() restituisce {listValues.Last()}.";
                }
            }
            else if (current == "vector")
            {
                if (method == "front / back")
                {
                    if (vectorValues.Count == 0) return "Il vector è vuoto: front() e back() non si possono usare.";
                    HighlightFirstLast(vectorValues.Count, "front()", "back()");
                    return vectorValues.Count == 1
                        ? $"front() e back() restituiscono lo stesso elemento: {vectorValues[0]}."
                        : $"front() restituisce {vectorValues.First()} e back() restituisce {vectorValues.Last()}.";
                }
                if (method == "at / operator[]")
                {
                    if (vectorValues.Count == 0) return "Il vector è vuoto: non c'è nessun indice da leggere.";
                    int index = Math.Min(1, vectorValues.Count - 1);
                    HighlightVisual(index, $"[{index}]");
                    return $"at({index}) e operator[{index}] leggono l'elemento evidenziato: {vectorValues[index]}.";
                }
            }
            else if (current == "set")
            {
                var data = setValues.ToList();
                if (method is "find" or "count" or "contains")
                {
                    if (data.Count == 0) return "Il set è vuoto: non c'è nessun elemento da cercare.";
                    HighlightVisual(0, method);
                    return $"{method} cerca un valore nel set. Nella demo viene evidenziato il valore trovato: {data[0]}.";
                }
                if (method == "begin / end")
                {
                    if (data.Count == 0) return "Il set è vuoto: begin() coincide con end().";
                    HighlightFirstLast(data.Count, "begin()", "ultimo");
                    return data.Count == 1
                        ? $"begin() punta all'unico elemento: {data[0]}. end() invece indica la posizione dopo l'ultimo."
                        : $"begin() punta al primo valore ordinato: {data.First()}. L'ultimo valore visibile è {data.Last()}; end() è la posizione dopo l'ultimo.";
                }
                if (method is "lower_bound" or "upper_bound")
                {
                    if (data.Count == 0) return "Il set è vuoto: il bound non trova elementi.";
                    HighlightVisual(0, method);
                    return $"{method} restituisce un iteratore al primo elemento utile secondo l'ordinamento. Nella demo è evidenziato {data[0]}.";
                }
            }
            else if (current == "stack")
            {
                if (method == "top")
                {
                    if (stackValues.Count == 0) return "Lo stack è vuoto: top() non si può usare.";
                    HighlightVisual(0, "top()");
                    return $"top() restituisce l'elemento in cima allo stack: {stackValues.Peek()}.";
                }
            }
            else if (current == "queue")
            {
                var data = queueValues.ToList();
                if (method == "front")
                {
                    if (data.Count == 0) return "La queue è vuota: front() non si può usare.";
                    HighlightVisual(0, "front()");
                    return $"front() restituisce il primo elemento in uscita: {data.First()}.";
                }
                if (method == "back")
                {
                    if (data.Count == 0) return "La queue è vuota: back() non si può usare.";
                    HighlightVisual(data.Count - 1, "back()");
                    return $"back() restituisce l'ultimo elemento inserito: {data.Last()}.";
                }
            }
            else if (current == "map")
            {
                var data = mapValues.ToList();
                if (method is "at/find" or "count" or "lower_bound" or "upper_bound")
                {
                    if (data.Count == 0) return "La map è vuota: non c'è nessuna coppia chiave-valore da leggere.";
                    HighlightVisual(0, method);
                    return $"{method} lavora sulla chiave e restituisce/evidenzia la coppia trovata: {data[0].Key}:{data[0].Value}.";
                }
                if (method == "begin / end")
                {
                    if (data.Count == 0) return "La map è vuota: begin() coincide con end().";
                    HighlightFirstLast(data.Count, "begin()", "ultimo");
                    return data.Count == 1
                        ? $"begin() punta all'unica coppia: {data[0].Key}:{data[0].Value}. end() è dopo l'ultimo elemento."
                        : $"begin() punta alla prima chiave ordinata: {data.First().Key}:{data.First().Value}. L'ultima coppia visibile è {data.Last().Key}:{data.Last().Value}; end() è dopo l'ultimo elemento.";
                }
            }

            return null;
        }

        private void RunMethod(string method)
        {
            int v = nextValue++;

            if (current == "list")
            {
                if (method == "push_back") listValues.Add(v);
                else if (method == "push_front") listValues.Insert(0, v);
                else if (method == "insert") listValues.Insert(listValues.Count / 2, v);
                else if (method == "erase" && listValues.Count > 0) listValues.RemoveAt(listValues.Count / 2);
                else if (method == "pop_back" && listValues.Count > 0) listValues.RemoveAt(listValues.Count - 1);
                else if (method == "pop_front" && listValues.Count > 0) listValues.RemoveAt(0);
                else if (method == "remove" && listValues.Count > 0) listValues.RemoveAll(x => x == listValues[0]);
                else if (method == "clear") listValues.Clear();
                else if (method == "sort/reverse") { listValues.Sort(); listValues.Reverse(); }
                else if (method == "unique") UniqueList(listValues);
            }
            else if (current == "vector")
            {
                if (method is "push_back" or "emplace_back") vectorValues.Add(v);
                else if (method == "insert") vectorValues.Insert(vectorValues.Count / 2, v);
                else if (method == "erase" && vectorValues.Count > 0) vectorValues.RemoveAt(vectorValues.Count / 2);
                else if (method == "pop_back" && vectorValues.Count > 0) vectorValues.RemoveAt(vectorValues.Count - 1);
                else if (method == "clear") vectorValues.Clear();
                else if (method == "resize") { while (vectorValues.Count < 5) vectorValues.Add(0); if (vectorValues.Count > 5) vectorValues.RemoveRange(5, vectorValues.Count - 5); }
                else if (method == "sort") vectorValues.Sort();
                else if (method == "reverse") vectorValues.Reverse();
            }
            else if (current == "set")
            {
                if (method is "insert" or "emplace") setValues.Add(v);
                else if (method == "erase" && setValues.Count > 0) setValues.Remove(setValues.First());
                else if (method is "clear" or "reset") setValues.Clear();
            }
            else if (current == "stack")
            {
                if (method is "push" or "emplace") stackValues.Push(v);
                else if (method == "pop" && stackValues.Count > 0) stackValues.Pop();
                else if (method == "reset") stackValues.Clear();
            }
            else if (current == "queue")
            {
                if (method is "push" or "emplace") queueValues.Enqueue(v);
                else if (method == "pop" && queueValues.Count > 0) queueValues.Dequeue();
                else if (method == "reset") queueValues.Clear();
            }
            else if (current == "map")
            {
                if (method is "insert" or "emplace" or "operator[]") mapValues[nextKey] = nextKey * 10;
                if (method is "insert" or "emplace" or "operator[]") nextKey++;
                else if (method == "erase" && mapValues.Count > 0) mapValues.Remove(mapValues.First().Key);
                else if (method is "clear" or "reset") mapValues.Clear();
            }

            var readInfo = ApplyReadHighlight(method);
            if (readInfo == null)
            {
                ClearVisualHighlights();
                HighlightVisual(GetLastInsertedIndex(method, v), method);
            }

            ShowGuide(method, DefaultMethodText(method) + (readInfo == null ? "" : "\n\nVALORE RESTITUITO / ELEMENTO EVIDENZIATO:\n" + readInfo));
            BuildOverloadButtons(method);
            DrawContainer();
        }

        private int GetLastInsertedIndex(string method, int value)
        {
            if (current == "list")
            {
                if (method == "push_back") return listValues.Count - 1;
                if (method == "push_front") return 0;
                if (method == "insert") return Math.Max(0, listValues.Count / 2);
            }
            if (current == "vector" && (method == "push_back" || method == "emplace_back")) return vectorValues.Count - 1;
            if (current == "vector" && method == "insert") return Math.Max(0, vectorValues.Count / 2);
            if (current == "set" && (method == "insert" || method == "emplace")) return setValues.ToList().IndexOf(value);
            if (current == "stack" && (method == "push" || method == "emplace")) return 0;
            if (current == "queue" && (method == "push" || method == "emplace")) return queueValues.Count - 1;
            if (current == "map" && (method == "insert" || method == "emplace" || method == "operator[]")) return mapValues.Count - 1;
            return -1;
        }

        private static void UniqueList(List<int> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
                if (values[i] == values[i - 1])
                    values.RemoveAt(i);
        }


        private void BuildOverloadButtons(string method)
        {
            OverloadActionsPanel.Children.Clear();

            var overloads = GetOverloadsForMethod(current, method);
            if (overloads.Count == 0)
            {
                var txt = new TextBlock
                {
                    Text = "Nessun overload eseguibile per questo metodo nella demo.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(4)
                };
                OverloadActionsPanel.Children.Add(txt);
                return;
            }

            foreach (var ov in overloads)
            {
                var btn = new Button
                {
                    Content = ov.Label,
                    Tag = ov.Key,
                    Background = new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                    Foreground = Brushes.White,
                    Margin = new Thickness(4),
                    Padding = new Thickness(10, 7, 10, 7)
                };
                btn.Click += (_, _) => RunOverload(method, ov.Key);
                OverloadActionsPanel.Children.Add(btn);
            }
        }

        private List<(string Key, string Label)> GetOverloadsForMethod(string container, string method)
        {
            var list = new List<(string, string)>();

            if (container == "list")
            {
                if (method == "insert")
                {
                    list.Add(("insert_value", "insert(pos, value)"));
                    list.Add(("insert_count", "insert(pos, count, value)"));
                    list.Add(("insert_range", "insert(pos, first, last)"));
                    list.Add(("insert_initializer", "insert(pos, { ... })"));
                }
                else if (method == "erase")
                {
                    list.Add(("erase_pos", "erase(pos)"));
                    list.Add(("erase_range", "erase(first, last)"));
                }
                else if (method == "push_back")
                {
                    list.Add(("push_back_lvalue", "push_back(const T&)"));
                    list.Add(("push_back_rvalue", "push_back(T&&)"));
                }
                else if (method == "push_front")
                {
                    list.Add(("push_front_lvalue", "push_front(const T&)"));
                    list.Add(("push_front_rvalue", "push_front(T&&)"));
                }
                else if (method == "unique")
                {
                    list.Add(("unique_plain", "unique()"));
                    list.Add(("unique_predicate", "unique(pred)"));
                }
                else if (method == "sort/reverse")
                {
                    list.Add(("sort_plain", "sort()"));
                    list.Add(("sort_desc", "sort(greater<int>())"));
                    list.Add(("reverse_plain", "reverse()"));
                }
            }

            if (container == "vector")
            {
                if (method == "insert")
                {
                    list.Add(("insert_value", "insert(pos, value)"));
                    list.Add(("insert_count", "insert(pos, count, value)"));
                    list.Add(("insert_range", "insert(pos, first, last)"));
                    list.Add(("insert_initializer", "insert(pos, { ... })"));
                }
                else if (method == "erase")
                {
                    list.Add(("erase_pos", "erase(pos)"));
                    list.Add(("erase_range", "erase(first, last)"));
                }
                else if (method == "resize")
                {
                    list.Add(("resize_n", "resize(n)"));
                    list.Add(("resize_n_value", "resize(n, value)"));
                }
                else if (method == "push_back")
                {
                    list.Add(("push_back_lvalue", "push_back(const T&)"));
                    list.Add(("push_back_rvalue", "push_back(T&&)"));
                }
                else if (method == "emplace_back")
                {
                    list.Add(("emplace_back_one", "emplace_back(value)"));
                    list.Add(("emplace_back_object", "emplace_back(args...)"));
                }
                else if (method == "sort")
                {
                    list.Add(("sort_plain", "sort(begin,end)"));
                    list.Add(("sort_desc", "sort(begin,end,greater)"));
                }
                else if (method == "reverse")
                {
                    list.Add(("reverse_plain", "reverse(begin,end)"));
                }
            }

            if (container == "set")
            {
                if (method == "insert")
                {
                    list.Add(("insert_value", "insert(value)"));
                    list.Add(("insert_range", "insert(first, last)"));
                    list.Add(("insert_initializer", "insert({ ... })"));
                }
                else if (method == "erase")
                {
                    list.Add(("erase_value", "erase(value)"));
                    list.Add(("erase_iterator", "erase(iterator)"));
                    list.Add(("erase_range", "erase(first, last)"));
                }
                else if (method == "emplace")
                {
                    list.Add(("emplace_value", "emplace(value)"));
                }
            }

            if (container == "map")
            {
                if (method == "insert")
                {
                    list.Add(("insert_pair", "insert({key,value})"));
                    list.Add(("insert_make_pair", "insert(make_pair)"));
                    list.Add(("insert_range", "insert(first,last)"));
                }
                else if (method == "emplace")
                {
                    list.Add(("emplace_pair", "emplace(key,value)"));
                }
                else if (method == "operator[]")
                {
                    list.Add(("operator_new", "m[key]=value nuova"));
                    list.Add(("operator_modify", "m[key]=value modifica"));
                }
                else if (method == "erase")
                {
                    list.Add(("erase_key", "erase(key)"));
                    list.Add(("erase_iterator", "erase(iterator)"));
                    list.Add(("erase_range", "erase(first,last)"));
                }
            }

            if (container == "stack")
            {
                if (method == "push")
                {
                    list.Add(("push_lvalue", "push(const T&)"));
                    list.Add(("push_rvalue", "push(T&&)"));
                }
                else if (method == "emplace")
                {
                    list.Add(("emplace_value", "emplace(value)"));
                }
            }

            if (container == "queue")
            {
                if (method == "push")
                {
                    list.Add(("push_lvalue", "push(const T&)"));
                    list.Add(("push_rvalue", "push(T&&)"));
                }
                else if (method == "emplace")
                {
                    list.Add(("emplace_value", "emplace(value)"));
                }
            }

            return list;
        }


        private int ReadInt(TextBox box, int fallback)
        {
            if (int.TryParse(box.Text, out int value)) return value;
            return fallback;
        }

        private int ReadOverloadPosition(int maxCount)
        {
            int pos = ReadInt(OverloadPosBox, 0);
            if (pos < 0) pos = 0;
            if (pos > maxCount) pos = maxCount;
            return pos;
        }

        private int ReadOverloadValue()
        {
            return ReadInt(OverloadValueBox, nextValue++);
        }

        private int ReadOverloadCount()
        {
            int count = ReadInt(OverloadCountBox, 2);
            return Math.Max(1, count);
        }

        private List<int> ReadOverloadRange()
        {
            var values = new List<int>();
            foreach (var part in OverloadRangeBox.Text.Split(',', ';', ' '))
            {
                if (int.TryParse(part.Trim(), out int n))
                    values.Add(n);
            }
            if (values.Count == 0) values.Add(ReadOverloadValue());
            return values;
        }

        private int ReadOverloadKey()
        {
            return ReadInt(OverloadKeyBox, nextKey++);
        }

        private void RunOverload(string method, string overloadKey)
        {
            int value = ReadOverloadValue();
            int count = ReadOverloadCount();
            var range = ReadOverloadRange();

            if (current == "list")
            {
                int pos = ReadOverloadPosition(listValues.Count);

                if (overloadKey == "insert_value")
                    listValues.Insert(pos, value);
                else if (overloadKey == "insert_count")
                    listValues.InsertRange(pos, Enumerable.Repeat(value, count));
                else if (overloadKey == "insert_range")
                    listValues.InsertRange(pos, range);
                else if (overloadKey == "insert_initializer")
                    listValues.InsertRange(pos, range);
                else if (overloadKey == "erase_pos" && listValues.Count > 0)
                    listValues.RemoveAt(Math.Min(pos, listValues.Count - 1));
                else if (overloadKey == "erase_range" && listValues.Count > 0)
                {
                    int start = Math.Min(pos, listValues.Count - 1);
                    int take = Math.Min(count, listValues.Count - start);
                    listValues.RemoveRange(start, take);
                }
                else if (overloadKey.StartsWith("push_back"))
                    listValues.Add(value);
                else if (overloadKey.StartsWith("push_front"))
                    listValues.Insert(0, value);
                else if (overloadKey == "unique_plain" || overloadKey == "unique_predicate")
                    UniqueList(listValues);
                else if (overloadKey == "sort_plain")
                    listValues.Sort();
                else if (overloadKey == "sort_desc")
                    listValues.Sort((a,b) => b.CompareTo(a));
                else if (overloadKey == "reverse_plain")
                    listValues.Reverse();
            }
            else if (current == "vector")
            {
                int pos = ReadOverloadPosition(vectorValues.Count);

                if (overloadKey == "insert_value")
                    vectorValues.Insert(pos, value);
                else if (overloadKey == "insert_count")
                    vectorValues.InsertRange(pos, Enumerable.Repeat(value, count));
                else if (overloadKey == "insert_range")
                    vectorValues.InsertRange(pos, range);
                else if (overloadKey == "insert_initializer")
                    vectorValues.InsertRange(pos, range);
                else if (overloadKey == "erase_pos" && vectorValues.Count > 0)
                    vectorValues.RemoveAt(Math.Min(pos, vectorValues.Count - 1));
                else if (overloadKey == "erase_range" && vectorValues.Count > 0)
                {
                    int start = Math.Min(pos, vectorValues.Count - 1);
                    int take = Math.Min(count, vectorValues.Count - start);
                    vectorValues.RemoveRange(start, take);
                }
                else if (overloadKey == "resize_n")
                {
                    int newSize = Math.Max(0, value);
                    while (vectorValues.Count < newSize) vectorValues.Add(0);
                    if (vectorValues.Count > newSize) vectorValues.RemoveRange(newSize, vectorValues.Count - newSize);
                }
                else if (overloadKey == "resize_n_value")
                {
                    int newSize = Math.Max(0, value);
                    while (vectorValues.Count < newSize) vectorValues.Add(count);
                    if (vectorValues.Count > newSize) vectorValues.RemoveRange(newSize, vectorValues.Count - newSize);
                }
                else if (overloadKey.StartsWith("push_back") || overloadKey.StartsWith("emplace_back"))
                    vectorValues.Add(value);
                else if (overloadKey == "sort_plain")
                    vectorValues.Sort();
                else if (overloadKey == "sort_desc")
                    vectorValues.Sort((a,b) => b.CompareTo(a));
                else if (overloadKey == "reverse_plain")
                    vectorValues.Reverse();
            }
            else if (current == "set")
            {
                if (overloadKey == "insert_value" || overloadKey == "emplace_value")
                    setValues.Add(value);
                else if (overloadKey == "insert_range" || overloadKey == "insert_initializer")
                    foreach (var x in range) setValues.Add(x);
                else if (overloadKey == "erase_value")
                    setValues.Remove(value);
                else if (overloadKey == "erase_iterator" && setValues.Count > 0)
                    setValues.Remove(setValues.First());
                else if (overloadKey == "erase_range" && setValues.Count > 0)
                    foreach (var x in setValues.Take(count).ToList()) setValues.Remove(x);
            }
            else if (current == "map")
            {
                int key = ReadOverloadKey();

                if (overloadKey == "insert_pair" || overloadKey == "insert_make_pair" || overloadKey == "emplace_pair")
                    mapValues.TryAdd(key, value);
                else if (overloadKey == "insert_range")
                {
                    foreach (var x in range)
                    {
                        mapValues[key] = x;
                        key++;
                    }
                }
                else if (overloadKey == "operator_new")
                    mapValues[key] = value;
                else if (overloadKey == "operator_modify")
                {
                    if (mapValues.Count == 0) mapValues[key] = value;
                    else mapValues[mapValues.First().Key] = value;
                }
                else if (overloadKey == "erase_key")
                    mapValues.Remove(key);
                else if (overloadKey == "erase_iterator" && mapValues.Count > 0)
                    mapValues.Remove(mapValues.First().Key);
                else if (overloadKey == "erase_range" && mapValues.Count > 0)
                    foreach (var k in mapValues.Keys.Take(count).ToList()) mapValues.Remove(k);
            }
            else if (current == "stack")
            {
                if (overloadKey.StartsWith("push") || overloadKey == "emplace_value")
                    stackValues.Push(value);
            }
            else if (current == "queue")
            {
                if (overloadKey.StartsWith("push") || overloadKey == "emplace_value")
                    queueValues.Enqueue(value);
            }

            ClearVisualHighlights();
            HighlightVisual(GuessActiveIndexAfterOverload(value, overloadKey), overloadKey);
            ShowGuide(method, DefaultMethodText(method) + "\n\nOVERLOAD ESEGUITO NELLA DEMO:\n" + overloadKey +
                "\n\nParametri usati:\nposizione = " + OverloadPosBox.Text +
                "\nvalore = " + OverloadValueBox.Text +
                "\nquantità = " + OverloadCountBox.Text +
                "\nrange = " + OverloadRangeBox.Text +
                "\nchiave map = " + OverloadKeyBox.Text);
            DrawContainer();
        }

        private void DrawContainer()
        {
            VisualCanvas.Children.Clear();

            int approxCount = current switch
            {
                "list" => listValues.Count,
                "vector" => vectorValues.Count,
                "set" => setValues.Count,
                "stack" => stackValues.Count,
                "queue" => queueValues.Count,
                _ => mapValues.Count
            };
            VisualCanvas.Width = 760;
            VisualCanvas.Height = Math.Max(240, 120 + ((Math.Max(approxCount, 1) - 1) / 10 + 1) * 46);

            var title = new TextBlock
            {
                Text = $"std::{current}",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(title, 20);
            Canvas.SetTop(title, 15);
            VisualCanvas.Children.Add(title);

            IEnumerable<string> items = current switch
            {
                "list" => listValues.Select(x => x.ToString()),
                "vector" => vectorValues.Select((x, i) => $"[{i}] {x}"),
                "set" => setValues.Select(x => x.ToString()),
                "stack" => stackValues.Select(x => x.ToString()),
                "queue" => queueValues.Select(x => x.ToString()),
                _ => mapValues.Select(p => $"{p.Key}:{p.Value}")
            };

            var data = items.ToList();
            if (data.Count == 0)
            {
                var empty = new TextBlock { Text = "[ contenitore vuoto ]", Foreground = Brushes.LightGray, FontSize = 16 };
                Canvas.SetLeft(empty, 20);
                Canvas.SetTop(empty, 70);
                VisualCanvas.Children.Add(empty);
                return;
            }

            double nodeW = approxCount <= 10 ? 42 : approxCount <= 20 ? 36 : 32;
            double nodeH = approxCount <= 10 ? 26 : approxCount <= 20 ? 24 : 22;
            double gap = approxCount <= 10 ? 10 : 8;
            double fontSize = approxCount <= 10 ? 11 : approxCount <= 20 ? 10 : 9;
            double mapExtra = current == "map" ? 18 : 0;
            int perRow = Math.Max(1, (int)((VisualCanvas.Width - 60) / (nodeW + mapExtra + gap)));
            double rowWidth = Math.Min(data.Count, perRow) * (nodeW + mapExtra) + (Math.Min(data.Count, perRow) - 1) * gap;
            double x0 = Math.Max(22, (VisualCanvas.Width - rowWidth) / 2);
            double y0 = 68;
            int visualIndex = 0;
            foreach (var item in data)
            {
                var rect = new Border
                {
                    Width = current == "map" ? Math.Max(60, nodeW + 14) : nodeW,
                    Height = nodeH,
                    CornerRadius = new CornerRadius(5),
                    Background = activeVisualIndexes.Contains(visualIndex) ? ContainerBrush() : new SolidColorBrush(Color.FromRgb(75, 85, 99)),
                    BorderBrush = activeVisualIndexes.Contains(visualIndex) ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    BorderThickness = new Thickness(1.5),
                    Child = new TextBlock
                    {
                        Text = item,
                        Foreground = Brushes.White,
                        FontFamily = new FontFamily("Consolas"),
                        FontWeight = FontWeights.Bold,
                        FontSize = fontSize,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetLeft(rect, x0);
                Canvas.SetTop(rect, y0);
                VisualCanvas.Children.Add(rect);

                if (activeVisualLabels.TryGetValue(visualIndex, out var label))
                {
                    var marker = new TextBlock
                    {
                        Text = "↑ " + label,
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    };
                    Canvas.SetLeft(marker, x0);
                    Canvas.SetTop(marker, Math.Max(42, y0 - 18));
                    VisualCanvas.Children.Add(marker);
                }

                visualIndex++;
                x0 += rect.Width + gap;
                if (visualIndex % perRow == 0)
                {
                    int remaining = data.Count - visualIndex;
                    int nextRowItems = Math.Min(remaining, perRow);
                    double nextRowWidth = nextRowItems * (nodeW + mapExtra) + Math.Max(0, nextRowItems - 1) * gap;
                    x0 = Math.Max(22, (VisualCanvas.Width - nextRowWidth) / 2);
                    y0 += nodeH + 20;
                }
            }
        }

        private int GuessActiveIndexAfterOverload(int value, string overloadKey)
        {
            if (overloadKey.Contains("erase") || overloadKey.Contains("clear") || overloadKey.Contains("sort") || overloadKey.Contains("reverse")) return -1;
            if (current == "list") return listValues.IndexOf(value);
            if (current == "vector") return vectorValues.IndexOf(value);
            if (current == "set") return setValues.ToList().IndexOf(value);
            if (current == "stack") return stackValues.Count > 0 ? 0 : -1;
            if (current == "queue") return queueValues.Count - 1;
            if (current == "map") return mapValues.Count - 1;
            return -1;
        }

        private Brush ContainerBrush()
        {
            return current switch
            {
                "list" => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                "vector" => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                "set" => new SolidColorBrush(Color.FromRgb(124, 58, 237)),
                "stack" => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                "queue" => new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                _ => new SolidColorBrush(Color.FromRgb(5, 150, 105))
            };
        }

        private void ShowGuide(string key, string defaultText)
        {
            currentGuideKey = $"{current}:{key}";
            GuideTextBox.Text = customGuides.TryGetValue(currentGuideKey, out var saved) ? saved : defaultText;
        }

        private async void SaveGuide_Click(object sender, RoutedEventArgs e)
        {
            customGuides[currentGuideKey] = GuideTextBox.Text;
            SaveGuides();
            string driveInfo = TryExportDatabaseToGoogleDriveFolder(false);
            driveInfo += await TryUploadDatabaseToGoogleDriveOAuthAsync(false);
            MessageBox.Show("Guida salvata. La prossima volta che riapri questo metodo, ritroverai le tue modifiche." + driveInfo, "Salvataggio guida");
        }


        private async void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Salva la guida/metodo attualmente modificata
                if (!string.IsNullOrWhiteSpace(currentGuideKey))
                    customGuides[currentGuideKey] = GuideTextBox.Text;
                SaveGuides();
                SaveSummaries();

                // 2) Salva anche l'esercizio attuale senza chiedere mille pulsanti
                Directory.CreateDirectory(ExercisesFolder);
                string targetFile = currentLoadedExerciseFile;
                if (string.IsNullOrWhiteSpace(targetFile) || !File.Exists(targetFile))
                {
                    string autoFolder = System.IO.Path.Combine(ExercisesFolder, "Salvataggi automatici");
                    Directory.CreateDirectory(autoFolder);
                    targetFile = System.IO.Path.Combine(autoFolder, "esercizio_stl_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                }

                var data = new SavedExercise
                {
                    Consegna = ExerciseTextBox.Text,
                    Soluzione = GetGeneratedCode(),
                    RiassuntiContenitori = new Dictionary<string, SummaryNote>(containerSummaries),
                    SalvatoIl = DateTime.Now
                };
                File.WriteAllText(targetFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                currentLoadedExerciseFile = targetFile;

                // 3) Aggiorna albero cartelle e crea database unico con struttura cartelle
                RefreshSavedExercisesList();

                // 4) Esporta su Google Drive locale e su OAuth Drive, se configurato
                string driveInfo = TryExportDatabaseToGoogleDriveFolder(false);
                driveInfo += await TryUploadDatabaseToGoogleDriveOAuthAsync(false);

                var backup = BuildBackupDatabase();
                MessageBox.Show(
                    $"SALVATAGGIO COMPLETO ESEGUITO.\n\nGuide salvate: {backup.Guide.Count}\nEsercizi salvati: {backup.Esercizi.Count}\nCartelle salvate: {backup.Cartelle.Count}\nRiassunti salvati: {backup.Riassunti.Count}" + driveInfo,
                    "SALVA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante SALVA:\n" + ex.Message, "SALVA", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportAll_Click(object sender, RoutedEventArgs e)
        {
            // Prima prova Google Drive OAuth, poi Google Drive locale, poi scelta manuale del file.
            if (File.Exists(GoogleCredentialsFile))
            {
                var oauthAnswer = MessageBox.Show(
                    "Vuoi importare dal database salvato nel tuo Google Drive OAuth?",
                    "IMPORTA",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (oauthAnswer == MessageBoxResult.Yes && await TryImportDatabaseFromGoogleDriveOAuthAsync())
                    return;
            }

            string? driveFile = TryGetGoogleDriveDatabaseFile(createFolder: false);
            if (!string.IsNullOrWhiteSpace(driveFile) && File.Exists(driveFile))
            {
                var answer = MessageBox.Show(
                    "Ho trovato il database nella cartella Google Drive locale.\n\nVuoi importare da qui?\n\n" + driveFile,
                    "IMPORTA",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes && ImportDatabaseFromFile(driveFile, "Google Drive locale"))
                    return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Scegli il file JSON esportato da Google Drive",
                Filter = "Database STL Visual JSON|*.json|Tutti i file|*.*",
                FileName = GoogleDriveBackupFileName
            };

            if (dlg.ShowDialog() == true)
                ImportDatabaseFromFile(dlg.FileName, "file selezionato");
        }

        private async void ExportGuides_Click(object sender, RoutedEventArgs e)
        {
            SaveGuides();
            TryExportDatabaseToGoogleDriveFolder(false);
            await TryUploadDatabaseToGoogleDriveOAuthAsync(true);

            var dlg = new SaveFileDialog
            {
                Filter = "Database guide + esercizi JSON|*.json|All files|*.*",
                FileName = GoogleDriveBackupFileName
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var backup = new BackupDatabase
                    {
                        Guide = new Dictionary<string, string>(customGuides),
                        Esercizi = new Dictionary<string, SavedExercise>(),
                        Cartelle = new List<string>(),
                        Riassunti = new Dictionary<string, SummaryNote>(containerSummaries),
                        EsportatoIl = DateTime.Now
                    };

                    if (Directory.Exists(ExercisesFolder))
                    {
                        foreach (var file in Directory.GetFiles(ExercisesFolder, "*.json", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var exercise = JsonSerializer.Deserialize<SavedExercise>(File.ReadAllText(file));
                                if (exercise != null)
                                    backup.Esercizi[System.IO.Path.GetRelativePath(ExercisesFolder, file)] = exercise;
                            }
                            catch { }
                        }
                    }

                    File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

                    MessageBox.Show(
                        $"Database esportato correttamente.\n\nGuide esportate: {backup.Guide.Count}\nEsercizi esportati: {backup.Esercizi.Count}\n\nFile:\n{dlg.FileName}",
                        "Esporta guide/esercizi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore durante l'esportazione:\n" + ex.Message, "Esporta guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ImportGuides_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(GoogleCredentialsFile))
            {
                var oauthAnswer = MessageBox.Show(
                    "Vuoi provare prima a importare il database dal Google Drive con login OAuth?",
                    "Importa da Google Drive OAuth",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (oauthAnswer == MessageBoxResult.Yes && await TryImportDatabaseFromGoogleDriveOAuthAsync())
                    return;
            }

            string? driveFile = TryGetGoogleDriveDatabaseFile(createFolder: false);
            if (!string.IsNullOrWhiteSpace(driveFile) && File.Exists(driveFile))
            {
                var answer = MessageBox.Show(
                    "Ho trovato il database nel Google Drive locale dell'account Alessandro Barazzoli.\n\nVuoi importare da lì?\n\n" + driveFile,
                    "Importa da Google Drive",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    if (ImportDatabaseFromFile(driveFile, "Google Drive"))
                        return;
                }
            }

            var dlg = new OpenFileDialog
            {
                Filter = "Database guide + esercizi JSON|*.json|All files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var backup = JsonSerializer.Deserialize<BackupDatabase>(json);

                    if (backup != null && (backup.Guide.Count > 0 || backup.Esercizi.Count > 0 || backup.Riassunti.Count > 0))
                    {
                        customGuides = backup.Guide ?? new();
                        containerSummaries = backup.Riassunti ?? new();
                        SaveGuides();
                        SaveSummaries();

                        Directory.CreateDirectory(ExercisesFolder);
                        foreach (var pair in backup.Esercizi)
                        {
                            var relativePath = SanitizeRelativeExercisePath(pair.Key);
                            if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                relativePath += ".json";
                            var target = System.IO.Path.Combine(ExercisesFolder, relativePath);
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target) ?? ExercisesFolder);
                            File.WriteAllText(target, JsonSerializer.Serialize(pair.Value, new JsonSerializerOptions { WriteIndented = true }));
                        }

                        RefreshSavedExercisesList();
                        if (customGuides.TryGetValue(currentGuideKey, out var saved))
                            GuideTextBox.Text = saved;

                        MessageBox.Show(
                            $"Database importato correttamente.\n\nGuide importate: {customGuides.Count}\nEsercizi importati: {backup.Esercizi.Count}\nRiassunti importati: {containerSummaries.Count}",
                            "Importa guide/esercizi",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    // Compatibilità con i vecchi file: solo guide, senza esercizi.
                    var oldGuides = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (oldGuides == null)
                    {
                        MessageBox.Show("Il file selezionato non contiene un database valido.", "Importa guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    customGuides = oldGuides;
                    SaveGuides();
                    if (customGuides.TryGetValue(currentGuideKey, out var oldSaved))
                        GuideTextBox.Text = oldSaved;
                    MessageBox.Show("Vecchio database guide importato correttamente.", "Importa guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore durante l'importazione:\n" + ex.Message, "Importa guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private BackupDatabase BuildBackupDatabase()
        {
            var backup = new BackupDatabase
            {
                Guide = new Dictionary<string, string>(customGuides),
                Esercizi = new Dictionary<string, SavedExercise>(),
                Cartelle = new List<string>(),
                Riassunti = new Dictionary<string, SummaryNote>(containerSummaries),
                EsportatoIl = DateTime.Now
            };

            if (Directory.Exists(ExercisesFolder))
            {
                foreach (var dir in Directory.GetDirectories(ExercisesFolder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        backup.Cartelle.Add(System.IO.Path.GetRelativePath(ExercisesFolder, dir));
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(ExercisesFolder, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var exercise = JsonSerializer.Deserialize<SavedExercise>(File.ReadAllText(file));
                        if (exercise != null)
                            backup.Esercizi[System.IO.Path.GetRelativePath(ExercisesFolder, file)] = exercise;
                    }
                    catch { }
                }
            }

            return backup;
        }

        private string? TryGetGoogleDriveDatabaseFile(bool createFolder)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>
            {
                System.IO.Path.Combine(userProfile, "Google Drive"),
                System.IO.Path.Combine(userProfile, "Google Drive", "My Drive"),
                System.IO.Path.Combine(userProfile, "Google Drive", "Il mio Drive"),
                System.IO.Path.Combine(userProfile, "My Drive"),
                System.IO.Path.Combine(userProfile, "Il mio Drive")
            };

            foreach (var drive in Directory.GetLogicalDrives())
            {
                candidates.Add(System.IO.Path.Combine(drive, "My Drive"));
                candidates.Add(System.IO.Path.Combine(drive, "Il mio Drive"));
                candidates.Add(System.IO.Path.Combine(drive, "Google Drive"));
            }

            foreach (string root in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    string folder = System.IO.Path.Combine(root, "STLVisualModernWPF_Alessandro_Barazzoli");
                    if (createFolder) Directory.CreateDirectory(folder);
                    if (Directory.Exists(folder))
                        return System.IO.Path.Combine(folder, GoogleDriveBackupFileName);
                }
                catch { }
            }

            return null;
        }

        private string TryExportDatabaseToGoogleDriveFolder(bool showMessage)
        {
            try
            {
                string? driveFile = TryGetGoogleDriveDatabaseFile(createFolder: true);
                if (string.IsNullOrWhiteSpace(driveFile))
                    return "\n\nNota Drive: non ho trovato una cartella Google Drive locale sul PC. Se installi Google Drive per desktop, il programma potrà usare quel file sincronizzato.";

                var backup = BuildBackupDatabase();
                File.WriteAllText(driveFile, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
                string msg = "\n\nDatabase salvato anche nella cartella Google Drive locale:\n" + driveFile;
                if (showMessage) MessageBox.Show(msg, "Google Drive", MessageBoxButton.OK, MessageBoxImage.Information);
                return msg;
            }
            catch (Exception ex)
            {
                return "\n\nNota Drive: esportazione automatica non riuscita: " + ex.Message;
            }
        }


        private void LoadGoogleCredentials_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleziona credentials.json di Google Cloud OAuth",
                Filter = "Google OAuth credentials.json|*.json|Tutti i file|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                Directory.CreateDirectory(AppFolder);
                File.Copy(dlg.FileName, GoogleCredentialsFile, true);
                MessageBox.Show(
                    "Credentials OAuth caricate correttamente.\n\nFile copiato in:\n" + GoogleCredentialsFile +
                    "\n\nAl prossimo salvataggio/importazione si aprirà il browser per il login Google.",
                    "Google Drive OAuth",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento di credentials.json:\n" + ex.Message, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncGoogleDriveOAuth_Click(object sender, RoutedEventArgs e)
        {
            string result = await TryUploadDatabaseToGoogleDriveOAuthAsync(true);
            if (!string.IsNullOrWhiteSpace(result))
                MessageBox.Show(result.Trim(), "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task<DriveService?> CreateGoogleDriveServiceAsync(bool showMessages = true)
        {
            if (!File.Exists(GoogleCredentialsFile))
            {
                if (showMessages)
                    MessageBox.Show(
                        "Prima carica il file credentials.json con il pulsante:\n\n🔑 Carica OAuth Drive\n\nLo crei da Google Cloud Console abilitando Google Drive API e scegliendo OAuth Client ID per applicazione desktop.",
                        "Google Drive OAuth",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                return null;
            }

            try
            {
                using var stream = new FileStream(GoogleCredentialsFile, FileMode.Open, FileAccess.Read);
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] { DriveService.Scope.DriveFile },
                    "alessandro.barazzoli@liceoconnigliano.it",
                    System.Threading.CancellationToken.None,
                    new FileDataStore(GoogleTokenFolder, true));

                return new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "STLVisualModernWPF"
                });
            }
            catch (Exception ex)
            {
                if (showMessages) MessageBox.Show("Errore login OAuth Google Drive:\n" + ex.Message, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private static string EscapeDriveQueryValue(string value) => value.Replace("'", "\\'");

        private async Task<string?> FindDriveFileIdAsync(DriveService service, string name, string? parentId, string mimeType)
        {
            string q = $"name = '{EscapeDriveQueryValue(name)}' and trashed = false and mimeType = '{mimeType}'";
            if (!string.IsNullOrWhiteSpace(parentId))
                q += $" and '{parentId}' in parents";

            var request = service.Files.List();
            request.Q = q;
            request.Fields = "files(id, name)";
            request.PageSize = 1;
            var result = await request.ExecuteAsync();
            return result.Files?.FirstOrDefault()?.Id;
        }

        private async Task<string?> FindOrCreateDriveFolderAsync(DriveService service)
        {
            string? folderId = await FindDriveFileIdAsync(service, GoogleDriveBackupFolderName, null, "application/vnd.google-apps.folder");
            if (!string.IsNullOrWhiteSpace(folderId)) return folderId;

            var folder = new DriveFile
            {
                Name = GoogleDriveBackupFolderName,
                MimeType = "application/vnd.google-apps.folder"
            };

            var create = service.Files.Create(folder);
            create.Fields = "id";
            var created = await create.ExecuteAsync();
            return created.Id;
        }

        private async Task<string> TryUploadDatabaseToGoogleDriveOAuthAsync(bool showMessage)
        {
            if (!File.Exists(GoogleCredentialsFile))
                return "\n\nNota OAuth Drive: credentials.json non caricato. Usa il pulsante 'Carica OAuth Drive'.";

            try
            {
                var service = await CreateGoogleDriveServiceAsync(showMessage);
                if (service == null) return "\n\nNota OAuth Drive: login non completato.";

                string? folderId = await FindOrCreateDriveFolderAsync(service);
                if (string.IsNullOrWhiteSpace(folderId))
                    return "\n\nNota OAuth Drive: impossibile creare/trovare la cartella su Google Drive.";

                var backup = BuildBackupDatabase();
                string json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                string? fileId = await FindDriveFileIdAsync(service, GoogleDriveBackupFileName, folderId, "application/json");
                using var stream = new MemoryStream(bytes);

                if (string.IsNullOrWhiteSpace(fileId))
                {
                    var metadata = new DriveFile
                    {
                        Name = GoogleDriveBackupFileName,
                        MimeType = "application/json",
                        Parents = new List<string> { folderId }
                    };
                    var create = service.Files.Create(metadata, stream, "application/json");
                    create.Fields = "id";
                    await create.UploadAsync();
                }
                else
                {
                    var metadata = new DriveFile
                    {
                        Name = GoogleDriveBackupFileName,
                        MimeType = "application/json"
                    };
                    var update = service.Files.Update(metadata, fileId, stream, "application/json");
                    await update.UploadAsync();
                }

                lastDriveAutoImportUtc = DateTime.UtcNow;
                string msg = "\n\nDatabase salvato su Google Drive OAuth nella cartella:\n" + GoogleDriveBackupFolderName;
                if (showMessage) MessageBox.Show(msg, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Information);
                return msg;
            }
            catch (Exception ex)
            {
                return "\n\nNota OAuth Drive: sincronizzazione non riuscita: " + ex.Message;
            }
        }

        private async Task<bool> TryImportDatabaseFromGoogleDriveOAuthAsync(bool showMessages = true)
        {
            if (!File.Exists(GoogleCredentialsFile))
            {
                if (showMessages) MessageBox.Show("Prima carica credentials.json con il pulsante 'Carica OAuth Drive'.", "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var service = await CreateGoogleDriveServiceAsync(showMessages);
                if (service == null) return false;

                string? folderId = await FindDriveFileIdAsync(service, GoogleDriveBackupFolderName, null, "application/vnd.google-apps.folder");
                if (string.IsNullOrWhiteSpace(folderId))
                {
                    if (showMessages) MessageBox.Show("Non ho trovato la cartella Drive: " + GoogleDriveBackupFolderName, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                string? fileId = await FindDriveFileIdAsync(service, GoogleDriveBackupFileName, folderId, "application/json");
                if (string.IsNullOrWhiteSpace(fileId))
                {
                    if (showMessages) MessageBox.Show("Non ho trovato il file Drive: " + GoogleDriveBackupFileName, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), GoogleDriveBackupFileName);
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    var get = service.Files.Get(fileId);
                    await get.DownloadAsync(output);
                }

                return ImportDatabaseFromFile(temp, "Google Drive OAuth", showMessages);
            }
            catch (Exception ex)
            {
                if (showMessages) MessageBox.Show("Errore importazione da Google Drive OAuth:\n" + ex.Message, "Google Drive OAuth", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool ImportDatabaseFromFile(string fileName, string sourceName, bool showMessages = true)
        {
            try
            {
                string json = File.ReadAllText(fileName);
                var backup = JsonSerializer.Deserialize<BackupDatabase>(json);
                if (backup == null)
                {
                    if (showMessages) MessageBox.Show("Il file selezionato non contiene un database valido.", "Importa guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                customGuides = backup.Guide ?? new();
                containerSummaries = backup.Riassunti ?? new();
                SaveGuides();
                SaveSummaries();

                Directory.CreateDirectory(ExercisesFolder);
                foreach (var dir in backup.Cartelle ?? new List<string>())
                {
                    var relativeDir = SanitizeRelativeExercisePath(dir);
                    Directory.CreateDirectory(System.IO.Path.Combine(ExercisesFolder, relativeDir));
                }

                foreach (var pair in backup.Esercizi)
                {
                    var relativePath = SanitizeRelativeExercisePath(pair.Key);
                    if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        relativePath += ".json";
                    var target = System.IO.Path.Combine(ExercisesFolder, relativePath);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target) ?? ExercisesFolder);
                    File.WriteAllText(target, JsonSerializer.Serialize(pair.Value, new JsonSerializerOptions { WriteIndented = true }));
                }

                RefreshSavedExercisesList();
                if (customGuides.TryGetValue(currentGuideKey, out var saved))
                    GuideTextBox.Text = saved;

                if (showMessages)
                    MessageBox.Show(
                        $"Database importato da {sourceName}.\n\nGuide importate: {customGuides.Count}\nEsercizi importati: {backup.Esercizi.Count}\nCartelle importate: {backup.Cartelle.Count}\nRiassunti importati: {containerSummaries.Count}",
                        "Importa guide/esercizi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                if (showMessages) MessageBox.Show("Errore durante l'importazione:\n" + ex.Message, "Importa guide/esercizi", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "esercizio_importato.json" : name;
        }

        private string SanitizeRelativeExercisePath(string relativePath)
        {
            var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(SanitizeFileName)
                                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "." && x != "..");
            var cleaned = System.IO.Path.Combine(parts.ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "esercizio_importato.json" : cleaned;
        }


        private void LoadGuides()
        {
            if (!File.Exists(GuidesFile)) return;
            try
            {
                customGuides = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(GuidesFile)) ?? new();
            }
            catch { customGuides = new(); }
        }

        private void SaveGuides()
        {
            File.WriteAllText(GuidesFile, JsonSerializer.Serialize(customGuides, new JsonSerializerOptions { WriteIndented = true }));
        }

        private string OverviewText() =>
$@"CONTENITORE: std::{current}

Questa guida è modificabile. Puoi aggiungere spiegazioni, esempi ed errori frequenti.
Premi 'Salva guida/metodo' per ricordare le modifiche anche domani.

Il file viene salvato in:
{GuidesFile}

Autore: Alessandro Barazzuol";

        private string DefaultMethodText(string method)
        {
            string header =
$@"METODO: {method}
CONTENITORE: std::{current}

Questa guida è già pronta, ma puoi modificarla liberamente.
Se aggiungi esempi o appunti personali, premi 'Salva guida/metodo' e il programma li ricorderà anche la prossima volta.

";

            return header + MethodGuide(current, method) + CompleteGuideAddendum(current, method);
        }

        private string MethodGuide(string container, string method)
        {
            if (container == "list")
            {
                if (method == "push_back") return @"DESCRIZIONE
push_back inserisce un nuovo elemento alla fine della lista.

SINTASSI
lista.push_back(valore);

ESEMPIO
std::list<int> lista;
lista.push_back(10);
lista.push_back(20);
// lista: 10 20

OVERLOAD PRINCIPALI
push_back(const T& value)
push_back(T&& value)

NOTE
In std::list l'inserimento in fondo è molto efficiente.
Non serve spostare tutti gli elementi come può accadere in un vector.";

                if (method == "push_front") return @"DESCRIZIONE
push_front inserisce un nuovo elemento all'inizio della lista.

SINTASSI
lista.push_front(valore);

ESEMPIO
std::list<int> lista;
lista.push_front(10);
lista.push_front(20);
// lista: 20 10

OVERLOAD PRINCIPALI
push_front(const T& value)
push_front(T&& value)

NOTE
È uno dei metodi più tipici della list, perché una lista collegata inserisce facilmente sia davanti sia dietro.";

                if (method == "insert") return @"DESCRIZIONE
insert inserisce uno o più elementi prima della posizione indicata da un iteratore.

SINTASSI
lista.insert(pos, valore);

ESEMPI
auto it = lista.begin();
std::advance(it, 1);
lista.insert(it, 99);

OVERLOAD PRINCIPALI
insert(pos, value)
insert(pos, count, value)
insert(pos, first, last)
insert(pos, {1, 2, 3})

ERRORE COMUNE
Non esiste lista[2]. Per arrivare a una posizione si usa un iteratore e std::advance.";

                if (method == "erase") return @"DESCRIZIONE
erase elimina un elemento o un intervallo di elementi usando iteratori.

SINTASSI
lista.erase(pos);
lista.erase(first, last);

ESEMPIO
auto it = lista.begin();
lista.erase(it);

NOTE
erase restituisce l'iteratore all'elemento successivo.
Dopo erase, l'iteratore eliminato non va più usato.";

                if (method == "pop_back") return @"DESCRIZIONE
pop_back rimuove l'ultimo elemento della lista.

SINTASSI
lista.pop_back();

ESEMPIO
if (!lista.empty()) {
    lista.pop_back();
}

ERRORE COMUNE
Non chiamare pop_back su una lista vuota.";

                if (method == "pop_front") return @"DESCRIZIONE
pop_front rimuove il primo elemento della lista.

SINTASSI
lista.pop_front();

ESEMPIO
if (!lista.empty()) {
    lista.pop_front();
}

ERRORE COMUNE
Non chiamare pop_front su una lista vuota.";

                if (method == "remove") return @"DESCRIZIONE
remove elimina tutti gli elementi uguali a un certo valore.

SINTASSI
lista.remove(valore);

ESEMPIO
std::list<int> lista = {1, 2, 2, 3};
lista.remove(2);
// lista: 1 3

DIFFERENZA CON erase
remove elimina per valore.
erase elimina tramite iteratore.";

                if (method == "clear") return @"DESCRIZIONE
clear elimina tutti gli elementi della lista.

SINTASSI
lista.clear();

ESEMPIO
lista.clear();

NOTE
Dopo clear:
lista.empty() == true
lista.size() == 0";

                if (method == "front / back") return @"DESCRIZIONE
front restituisce il primo elemento.
back restituisce l'ultimo elemento.

SINTASSI
lista.front();
lista.back();

ESEMPIO
if (!lista.empty()) {
    cout << lista.front();
    cout << lista.back();
}

ERRORE COMUNE
Non usare front o back su una lista vuota.";

                if (method == "size / empty") return @"DESCRIZIONE
size restituisce il numero di elementi.
empty controlla se la lista è vuota.

SINTASSI
lista.size();
lista.empty();

ESEMPIO
if (lista.empty()) {
    cout << ""lista vuota"";
}";

                if (method == "sort/reverse") return @"DESCRIZIONE
sort ordina la lista.
reverse inverte l'ordine degli elementi.

SINTASSI
lista.sort();
lista.reverse();

ESEMPIO
std::list<int> lista = {3, 1, 2};
lista.sort();    // 1 2 3
lista.reverse(); // 3 2 1

NOTA
Per std::list si usa lista.sort(), non std::sort.";

                if (method == "unique") return @"DESCRIZIONE
unique elimina i duplicati consecutivi.

SINTASSI
lista.unique();

ESEMPIO
std::list<int> lista = {1, 1, 2, 1};
lista.unique();
// risultato: 1 2 1

NOTA
Se vuoi eliminare tutti i duplicati, spesso fai prima:
lista.sort();
lista.unique();";
            }

            if (container == "vector")
            {
                if (method == "push_back") return @"DESCRIZIONE
push_back aggiunge un elemento alla fine del vector.

SINTASSI
v.push_back(valore);

ESEMPIO
std::vector<int> v;
v.push_back(10);
v.push_back(20);

OVERLOAD
push_back(const T& value)
push_back(T&& value)

NOTA
Se il vector non ha più capacità disponibile, può riallocare memoria e invalidare iteratori.";

                if (method == "emplace_back") return @"DESCRIZIONE
emplace_back costruisce direttamente un elemento alla fine del vector.

SINTASSI
v.emplace_back(argomenti);

ESEMPIO
std::vector<int> v;
v.emplace_back(10);

DIFFERENZA CON push_back
push_back inserisce un oggetto già creato.
emplace_back costruisce l'oggetto direttamente nel contenitore.";

                if (method == "insert") return @"DESCRIZIONE
insert inserisce elementi in una posizione del vector.

SINTASSI
v.insert(pos, valore);

OVERLOAD
insert(pos, value)
insert(pos, count, value)
insert(pos, first, last)
insert(pos, {1, 2, 3})

ESEMPIO
auto it = v.begin() + 1;
v.insert(it, 99);

NOTA
Può spostare elementi e invalidare iteratori.";

                if (method == "erase") return @"DESCRIZIONE
erase elimina un elemento o un intervallo.

SINTASSI
v.erase(pos);
v.erase(first, last);

ESEMPIO
v.erase(v.begin());

NOTA
Gli elementi successivi vengono spostati.";

                if (method == "pop_back") return @"DESCRIZIONE
pop_back elimina l'ultimo elemento del vector.

SINTASSI
v.pop_back();

ESEMPIO
if (!v.empty()) {
    v.pop_back();
}";

                if (method == "clear") return @"DESCRIZIONE
clear elimina tutti gli elementi del vector.

SINTASSI
v.clear();

NOTA
clear azzera size(), ma spesso non azzera capacity().";

                if (method == "front / back") return @"DESCRIZIONE
front restituisce il primo elemento.
back restituisce l'ultimo elemento.

ERRORE COMUNE
Non usarli se il vector è vuoto.";

                if (method == "at / operator[]") return @"DESCRIZIONE
operator[] accede per indice senza controllo.
at() accede per indice con controllo.

SINTASSI
v[0];
v.at(0);

DIFFERENZA
v[100] può causare comportamento non valido.
v.at(100) genera un errore controllato.";

                if (method == "size/capacity") return @"DESCRIZIONE
size indica quanti elementi ci sono.
capacity indica quanta memoria è già prenotata.

SINTASSI
v.size();
v.capacity();";

                if (method == "resize") return @"DESCRIZIONE
resize cambia la dimensione del vector.

SINTASSI
v.resize(n);
v.resize(n, valore);

ESEMPIO
v.resize(10, 0);";

                if (method == "sort") return @"DESCRIZIONE
sort ordina il vector usando l'algoritmo std::sort.

SINTASSI
std::sort(v.begin(), v.end());

ESEMPIO
#include <algorithm>
std::sort(v.begin(), v.end());";

                if (method == "reverse") return @"DESCRIZIONE
reverse inverte l'ordine del vector.

SINTASSI
std::reverse(v.begin(), v.end());";
            }

            if (container == "set")
            {
                if (method == "insert") return @"DESCRIZIONE
insert inserisce un valore nel set.

SINTASSI
s.insert(valore);

ESEMPIO
std::set<int> s;
s.insert(10);
s.insert(10); // non crea duplicato

NOTE
std::set ordina automaticamente gli elementi e non ammette duplicati.";

                if (method == "emplace") return @"DESCRIZIONE
emplace costruisce direttamente un elemento dentro il set.

SINTASSI
s.emplace(valore);";

                if (method == "erase") return @"DESCRIZIONE
erase elimina elementi dal set.

OVERLOAD
s.erase(value);
s.erase(iterator);
s.erase(first, last);";

                if (method == "find") return @"DESCRIZIONE
find cerca un elemento.

SINTASSI
auto it = s.find(x);

ESEMPIO
if (s.find(10) != s.end()) {
    cout << ""trovato"";
}";

                if (method == "count") return @"DESCRIZIONE
count restituisce 0 o 1 nel set.

SINTASSI
s.count(x);";

                if (method == "clear") return @"DESCRIZIONE
clear svuota il set.

SINTASSI
s.clear();";

                if (method == "begin / end") return @"DESCRIZIONE
begin punta al primo elemento.
end indica la posizione dopo l'ultimo.

ESEMPIO
for (auto it = s.begin(); it != s.end(); ++it) {
    cout << *it;
}";

                if (method == "lower_bound") return @"DESCRIZIONE
lower_bound restituisce il primo elemento non minore di x.

SINTASSI
s.lower_bound(x);";

                if (method == "upper_bound") return @"DESCRIZIONE
upper_bound restituisce il primo elemento maggiore di x.

SINTASSI
s.upper_bound(x);";

                if (method == "size / empty") return @"DESCRIZIONE
size conta gli elementi.
empty controlla se il set è vuoto.";

                if (method == "contains") return @"DESCRIZIONE
contains controlla se un elemento esiste.

SINTASSI C++20
s.contains(x);

IN DEV-C++ VECCHIO
s.find(x) != s.end();";

                if (method == "reset") return @"DESCRIZIONE
reset nella demo svuota il set.

Codice reale:
s.clear();";
            }

            if (container == "stack")
            {
                if (method == "push") return @"DESCRIZIONE
push inserisce un elemento in cima allo stack.

SINTASSI
st.push(valore);

LOGICA
stack è LIFO: last in, first out.";

                if (method == "emplace") return @"DESCRIZIONE
emplace costruisce direttamente l'elemento in cima.

SINTASSI
st.emplace(valore);";

                if (method == "pop") return @"DESCRIZIONE
pop rimuove l'elemento in cima.

SINTASSI
st.pop();

NOTA
pop non restituisce il valore. Prima usa top(), poi pop().";

                if (method == "top") return @"DESCRIZIONE
top legge l'elemento in cima.

SINTASSI
st.top();

ERRORE COMUNE
Non usare top su stack vuoto.";

                if (method == "size") return @"DESCRIZIONE
size restituisce il numero di elementi.

SINTASSI
st.size();";

                if (method == "empty") return @"DESCRIZIONE
empty controlla se lo stack è vuoto.

SINTASSI
st.empty();";

                if (method == "reset") return @"DESCRIZIONE
std::stack non ha clear().

Per svuotarlo:
while (!st.empty()) {
    st.pop();
}";
            }

            if (container == "queue")
            {
                if (method == "push") return @"DESCRIZIONE
push inserisce un elemento in fondo alla queue.

SINTASSI
q.push(valore);

LOGICA
queue è FIFO: first in, first out.";

                if (method == "emplace") return @"DESCRIZIONE
emplace costruisce direttamente l'elemento in fondo.

SINTASSI
q.emplace(valore);";

                if (method == "pop") return @"DESCRIZIONE
pop rimuove l'elemento davanti.

SINTASSI
q.pop();

NOTA
pop non restituisce il valore. Usa front() prima di pop().";

                if (method == "front") return @"DESCRIZIONE
front legge il primo elemento della coda.

SINTASSI
q.front();";

                if (method == "back") return @"DESCRIZIONE
back legge l'ultimo elemento inserito.

SINTASSI
q.back();";

                if (method == "size") return @"DESCRIZIONE
size restituisce il numero di elementi.

SINTASSI
q.size();";

                if (method == "empty") return @"DESCRIZIONE
empty controlla se la queue è vuota.

SINTASSI
q.empty();";

                if (method == "reset") return @"DESCRIZIONE
std::queue non ha clear().

Per svuotarla:
while (!q.empty()) {
    q.pop();
}";
            }

            if (container == "map")
            {
                if (method == "insert") return @"DESCRIZIONE
insert inserisce una coppia chiave-valore.

SINTASSI
m.insert({chiave, valore});

ESEMPIO
std::map<int, string> m;
m.insert({1, ""uno""});

NOTE
Se la chiave esiste già, insert non sostituisce il valore.";

                if (method == "emplace") return @"DESCRIZIONE
emplace costruisce direttamente la coppia chiave-valore.

SINTASSI
m.emplace(chiave, valore);";

                if (method == "operator[]") return @"DESCRIZIONE
operator[] accede o crea una chiave.

SINTASSI
m[chiave] = valore;

ESEMPIO
m[1] = ""uno"";

ATTENZIONE
Se la chiave non esiste, viene creata.";

                if (method == "at/find") return @"DESCRIZIONE
at accede a una chiave esistente.
find cerca una chiave.

SINTASSI
m.at(chiave);
m.find(chiave);

DIFFERENZA
at genera errore se la chiave non esiste.
find restituisce end().";

                if (method == "erase") return @"DESCRIZIONE
erase elimina elementi dalla map.

OVERLOAD
m.erase(key);
m.erase(iterator);
m.erase(first, last);";

                if (method == "clear") return @"DESCRIZIONE
clear svuota la map.

SINTASSI
m.clear();";

                if (method == "begin / end") return @"DESCRIZIONE
begin/end permettono di scorrere le coppie.

ESEMPIO
for (auto &p : m) {
    cout << p.first << "" "" << p.second;
}";

                if (method == "size / empty") return @"DESCRIZIONE
size conta le coppie.
empty controlla se la map è vuota.";

                if (method == "count") return @"DESCRIZIONE
count controlla se una chiave esiste.

SINTASSI
m.count(key);

In std::map restituisce 0 o 1.";

                if (method == "lower_bound") return @"DESCRIZIONE
lower_bound restituisce il primo elemento con chiave non minore di key.

SINTASSI
m.lower_bound(key);";

                if (method == "upper_bound") return @"DESCRIZIONE
upper_bound restituisce il primo elemento con chiave maggiore di key.

SINTASSI
m.upper_bound(key);";

                if (method == "reset") return @"DESCRIZIONE
reset nella demo svuota la map.

Codice reale:
m.clear();";
            }

            return @"Guida del metodo non ancora disponibile.
Puoi scriverla tu e salvarla con 'Salva guida/metodo'.";
        }

        private string ConstructorsGuide()
        {
            if (current == "list") return @"COSTRUTTORI std::list

std::list<int> a;
std::list<int> b(5);
std::list<int> c(5, 10);
std::list<int> d = {1, 2, 3};
std::list<int> e(d.begin(), d.end());
std::list<int> f(d);

SIGNIFICATO
a: lista vuota
b: 5 elementi inizializzati
c: 5 elementi uguali a 10
d: lista inizializzata con valori
e: costruita da intervallo
f: copia";

            if (current == "vector") return @"COSTRUTTORI std::vector

std::vector<int> v;
std::vector<int> v(5);
std::vector<int> v(5, 10);
std::vector<int> v = {1, 2, 3};
std::vector<int> v2(v.begin(), v.end());
std::vector<int> v3(v);

NOTA
vector usa memoria contigua e permette accesso per indice.";

            if (current == "set") return @"COSTRUTTORI std::set

std::set<int> s;
std::set<int> s = {3, 1, 2};
std::set<int> s2(s.begin(), s.end());

NOTA
Il set ordina automaticamente e non ammette duplicati.";

            if (current == "stack") return @"COSTRUTTORI std::stack

std::stack<int> st;

Con contenitore sottostante:
std::deque<int> d;
std::stack<int> st(d);

NOTA
stack è un adattatore LIFO.";

            if (current == "queue") return @"COSTRUTTORI std::queue

std::queue<int> q;

Con contenitore sottostante:
std::deque<int> d;
std::queue<int> q(d);

NOTA
queue è un adattatore FIFO.";

            return @"COSTRUTTORI std::map

std::map<int, string> m;
std::map<int, string> m = {{1, ""uno""}, {2, ""due""}};
std::map<int, string> m2(m.begin(), m.end());
std::map<int, string> m3(m);

NOTA
map contiene coppie chiave-valore ordinate per chiave.";
        }


        private string CompleteGuideAddendum(string container, string method)
        {
            return $@"

APPROFONDIMENTO DIDATTICO

1) COME RAGIONARE SUL METODO
Prima di usare {method}, chiediti sempre:
- il contenitore è vuoto?
- sto lavorando su un indice, su un iteratore, su una chiave o su un valore?
- il metodo modifica il contenitore oppure legge soltanto?
- può invalidare iteratori o riferimenti?

2) ESEMPI DI SCORRIMENTO
Range-based for:
for (auto x : contenitore) {{
    cout << x << "" "";
}}

Con iteratori:
for (auto it = contenitore.begin(); it != contenitore.end(); ++it) {{
    cout << *it << "" "";
}}

Con iteratori inversi, se disponibili:
for (auto it = contenitore.rbegin(); it != contenitore.rend(); ++it) {{
    cout << *it << "" "";
}}

3) ERRORI FREQUENTI
- Usare front(), back() o top() quando il contenitore è vuoto.
- Dereferenziare end(): end() non è un elemento reale.
- Usare un iteratore dopo erase().
- Pensare che list abbia l'accesso con indice: lista[0] non esiste.
- Pensare che set mantenga l'ordine di inserimento: set ordina automaticamente.
- Pensare che map[key] legga soltanto: se la chiave non esiste, la crea.
- Usare pop() pensando che restituisca il valore: pop rimuove soltanto.

4) CONTROLLO SICURO
if (!contenitore.empty()) {{
    // posso usare front/back/top a seconda del contenitore
}}

5) NOTA PER DEV-C++ / C++11
Alcuni metodi moderni, come contains(), sono C++20.
Se usi Dev-C++ vecchio, usa find() != end().";
        }


        private void GuideIterators_Click(object sender, RoutedEventArgs e) => ShowGuide("Iteratori", IteratorGuideForCurrent());
        private void GuideLoops_Click(object sender, RoutedEventArgs e) => ShowGuide("Scorrimento", LoopGuideForCurrent());
        private void GuideAccess_Click(object sender, RoutedEventArgs e) => ShowGuide("Accesso", AccessGuideForCurrent());
        private void GuideErrors_Click(object sender, RoutedEventArgs e) => ShowGuide("Errori", ErrorsGuideForCurrent());
        private void GuideOverloads_Click(object sender, RoutedEventArgs e) => ShowGuide("Overload", OverloadGuideForCurrent());
        private void GuideConstructors_Click(object sender, RoutedEventArgs e) => ShowGuide("Costruttori", ConstructorsGuideForCurrent());
        private void GuideDef_Click(object sender, RoutedEventArgs e) => ShowGuide("DEF", DefinitionGuideForCurrent());

        private string ContainerName() => current switch
        {
            "list" => "std::list",
            "vector" => "std::vector",
            "set" => "std::set",
            "stack" => "std::stack",
            "queue" => "std::queue",
            "map" => "std::map",
            _ => current
        };

        private string DefinitionGuideForCurrent() => current switch
        {
            "vector" => @"DEF DI std::vector

std::vector è un contenitore sequenziale dinamico: funziona come un array che può crescere.

COME È FATTO
- Gli elementi sono salvati in memoria contigua, uno dopo l'altro.
- Permette accesso veloce con indice: v[0], v[1], v.at(2).
- Inserire o cancellare in fondo è efficiente.
- Inserire o cancellare in mezzo può essere più costoso perché gli elementi successivi devono spostarsi.

QUANDO USARLO
Usalo quando ti serve una lista ordinata per posizione, con accesso rapido agli elementi.",
            "list" => @"DEF DI std::list

std::list è un contenitore sequenziale a lista doppiamente collegata.

COME È FATTO
- Ogni nodo contiene il valore e due collegamenti: al nodo precedente e al nodo successivo.
- Non ha accesso diretto con indice: lista[0] non esiste.
- Per trovare un elemento bisogna scorrere la lista.
- Inserire o eliminare un nodo è comodo quando hai già l'iteratore nella posizione giusta.

QUANDO USARLA
Usala quando devi fare molti inserimenti/cancellazioni in mezzo e non ti serve l'accesso con indice.",
            "set" => @"DEF DI std::set

std::set è un contenitore associativo che conserva valori unici e ordinati.

COME È FATTO
- Non accetta duplicati.
- Gli elementi vengono ordinati automaticamente.
- Di solito è implementato come albero bilanciato.
- Non si accede per indice: si usa find(), insert(), erase().

QUANDO USARLO
Usalo quando devi memorizzare valori senza doppioni e vuoi controllare velocemente se un valore esiste.",
            "stack" => @"DEF DI std::stack

std::stack è un adattatore LIFO: Last In, First Out.

COME È FATTO
- L'ultimo elemento inserito è il primo che esce.
- Si lavora solo dalla cima.
- Metodi principali: push(), pop(), top(), empty(), size().
- Non puoi scorrere liberamente tutti gli elementi come in vector o list.

QUANDO USARLO
Usalo per pile di elementi, annullamenti, parentesi, backtracking e situazioni in cui serve l'ultimo inserito.",
            "queue" => @"DEF DI std::queue

std::queue è un adattatore FIFO: First In, First Out.

COME È FATTO
- Il primo elemento inserito è il primo che esce.
- Si inserisce in fondo con push().
- Si legge davanti con front() e si rimuove con pop().
- Non permette accesso casuale agli elementi interni.

QUANDO USARLA
Usala per code, turni, processi in attesa e gestione ordine di arrivo.",
            "map" => @"DEF DI std::map

std::map è un contenitore associativo chiave-valore.

COME È FATTO
- Ogni elemento ha una chiave e un valore.
- Le chiavi sono uniche e ordinate automaticamente.
- Si accede con m[chiave], at(chiave), find(chiave).
- Attenzione: m[chiave] crea la chiave se non esiste.

QUANDO USARLA
Usala quando devi collegare un valore a una chiave, per esempio nome-voto, codice-prezzo, id-oggetto.",
            _ => "DEF del contenitore selezionato."
        };

        private string IteratorGuideForCurrent() => current switch
        {
            "vector" => @"ITERATORI DI std::vector

vector usa memoria contigua. Gli iteratori si comportano quasi come puntatori.

Esempio:
std::vector<int> v = {10, 20, 30};
for (auto it = v.begin(); it != v.end(); ++it) {
    std::cout << *it << "" "";
}

Attenzione: insert, erase, push_back e resize possono invalidare gli iteratori se il vector rialloca memoria.
Dopo erase usa sempre l'iteratore restituito:
it = v.erase(it);",
            "list" => @"ITERATORI DI std::list

list non ha accesso con indice. Per leggere gli elementi si usano soprattutto gli iteratori.

Esempio:
std::list<int> l = {10, 20, 30};
for (auto it = l.begin(); it != l.end(); ++it) {
    std::cout << *it << "" "";
}

Vantaggio: insert ed erase in una posizione non spostano tutti gli altri elementi.
Dopo erase usa l'iteratore restituito:
it = l.erase(it);",
            "set" => @"ITERATORI DI std::set

set è ordinato automaticamente. Gli iteratori visitano gli elementi in ordine crescente.

Esempio:
std::set<int> s = {30, 10, 20};
for (auto it = s.begin(); it != s.end(); ++it) {
    std::cout << *it << "" "";
}

Gli elementi non si modificano direttamente con *it, perché cambiare un valore romperebbe l'ordine del set.
Per cercare usa find():
auto it = s.find(20);",
            "map" => @"ITERATORI DI std::map

map contiene coppie chiave-valore. L'iteratore punta a una pair.

Esempio:
std::map<int, int> m;
m[1] = 100;
m[2] = 200;

for (auto it = m.begin(); it != m.end(); ++it) {
    std::cout << it->first << "" -> "" << it->second;
}

first è la chiave, second è il valore. Le chiavi sono ordinate automaticamente.",
            "stack" => @"ITERATORI DI std::stack

stack non espone iteratori. È un contenitore adattatore: puoi vedere solo l'elemento in cima con top().

Esempio:
std::stack<int> st;
st.push(10);
st.push(20);
std::cout << st.top();

Per scorrerlo devi copiarlo e fare pop() sulla copia.",
            "queue" => @"ITERATORI DI std::queue

queue non espone iteratori. È un contenitore adattatore: puoi vedere front() e back().

Esempio:
std::queue<int> q;
q.push(10);
q.push(20);
std::cout << q.front();

Per scorrerla devi copiarla e fare pop() sulla copia.",
            _ => "Guida iteratori per " + ContainerName()
        };

        private string LoopGuideForCurrent() => current switch
        {
            "vector" => "SCORRIMENTO DI std::vector\n\nPuoi usare range-for, indici o iteratori.\n\nfor (int x : v) cout << x;\n\nfor (size_t i = 0; i < v.size(); ++i) cout << v[i];",
            "list" => "SCORRIMENTO DI std::list\n\nNon usare indici: list[0] non esiste.\n\nfor (int x : l) cout << x;\n\nfor (auto it = l.begin(); it != l.end(); ++it) cout << *it;",
            "set" => "SCORRIMENTO DI std::set\n\nIl set viene scorso in ordine crescente, non in ordine di inserimento.\n\nfor (int x : s) cout << x;",
            "map" => "SCORRIMENTO DI std::map\n\nSi scorrono coppie chiave-valore.\n\nfor (auto p : m) cout << p.first << \" -> \" << p.second;",
            "stack" => "SCORRIMENTO DI std::stack\n\nstack non si scorre con iteratori. Copialo e svuota la copia.\n\nauto copia = st;\nwhile (!copia.empty()) { cout << copia.top(); copia.pop(); }",
            "queue" => "SCORRIMENTO DI std::queue\n\nqueue non si scorre con iteratori. Copiala e svuota la copia.\n\nauto copia = q;\nwhile (!copia.empty()) { cout << copia.front(); copia.pop(); }",
            _ => "Scorrimento di " + ContainerName()
        };

        private string AccessGuideForCurrent() => current switch
        {
            "vector" => "ACCESSO IN std::vector\n\nAccesso per indice: v[0].\nAccesso controllato: v.at(0).\nPrimo elemento: v.front().\nUltimo elemento: v.back().\n\nPrima di front/back controlla sempre empty().",
            "list" => "ACCESSO IN std::list\n\nlist non ha v[0].\nPuoi usare front() e back(), oppure iteratori.\n\nif (!l.empty()) cout << l.front();",
            "set" => "ACCESSO IN std::set\n\nset non ha indice. Si usa find().\n\nauto it = s.find(10);\nif (it != s.end()) cout << *it;",
            "map" => "ACCESSO IN std::map\n\nCon m[key] leggi o crei una chiave.\nCon at(key) leggi solo se esiste.\nCon find(key) cerchi senza creare.\n\nauto it = m.find(3);\nif (it != m.end()) cout << it->second;",
            "stack" => "ACCESSO IN std::stack\n\nPuoi accedere solo all'elemento in cima con top().\n\nif (!st.empty()) cout << st.top();",
            "queue" => "ACCESSO IN std::queue\n\nPuoi accedere al primo con front() e all'ultimo con back().\n\nif (!q.empty()) cout << q.front();",
            _ => "Accesso in " + ContainerName()
        };

        private string ErrorsGuideForCurrent() => current switch
        {
            "vector" => "ERRORI COMUNI DI std::vector\n\n- Usare front/back quando è vuoto.\n- Usare un indice fuori misura.\n- Tenere iteratori vecchi dopo insert/erase/push_back con riallocazione.\n- Confondere size() con capacity().",
            "list" => "ERRORI COMUNI DI std::list\n\n- Scrivere l[0]: list non ha accesso per indice.\n- Dereferenziare end().\n- Usare un iteratore cancellato dopo erase.\n- Dimenticare empty() prima di front/back.",
            "set" => "ERRORI COMUNI DI std::set\n\n- Pensare che mantenga l'ordine di inserimento.\n- Provare a modificare direttamente *it.\n- Cercare con indice.\n- Dimenticare che i duplicati non vengono inseriti.",
            "map" => "ERRORI COMUNI DI std::map\n\n- Usare m[key] per controllare se una chiave esiste: così la crei.\n- Confondere first e second.\n- Pensare che sia ordinata per valore: è ordinata per chiave.",
            "stack" => "ERRORI COMUNI DI std::stack\n\n- Chiamare top() se è vuoto.\n- Pensare che pop() restituisca il valore.\n- Cercare di scorrerlo con iteratori: stack non li espone.",
            "queue" => "ERRORI COMUNI DI std::queue\n\n- Chiamare front() o back() se è vuota.\n- Pensare che pop() restituisca il valore.\n- Cercare di scorrerla con iteratori: queue non li espone.",
            _ => "Errori comuni di " + ContainerName()
        };

        private string OverloadGuideForCurrent() => current switch
        {
            "vector" => "OVERLOAD PRINCIPALI DI std::vector\n\npush_back(value)\ninsert(pos, value)\ninsert(pos, count, value)\ninsert(pos, first, last)\nerase(pos)\nerase(first, last)\nresize(n)\nresize(n, value)",
            "list" => "OVERLOAD PRINCIPALI DI std::list\n\npush_back(value)\npush_front(value)\ninsert(pos, value)\ninsert(pos, count, value)\ninsert(pos, first, last)\nerase(pos)\nerase(first, last)\nsort()\nunique()",
            "set" => "OVERLOAD PRINCIPALI DI std::set\n\ninsert(value)\ninsert(first, last)\nerase(value)\nerase(iterator)\nerase(first, last)\nfind(value)",
            "map" => "OVERLOAD PRINCIPALI DI std::map\n\ninsert({key, value})\ninsert(make_pair(key, value))\nemplace(key, value)\nerase(key)\nerase(iterator)\noperator[]\nat(key)\nfind(key)",
            "stack" => "METODI PRINCIPALI DI std::stack\n\npush(value)\nemplace(value)\ntop()\npop()\nempty()\nsize()",
            "queue" => "METODI PRINCIPALI DI std::queue\n\npush(value)\nemplace(value)\nfront()\nback()\npop()\nempty()\nsize()",
            _ => "Overload di " + ContainerName()
        };

        private string ConstructorsGuideForCurrent() => current switch
        {
            "vector" => "COSTRUTTORI DI std::vector\n\nstd::vector<int> v;\nstd::vector<int> v(5);\nstd::vector<int> v(5, 10);\nstd::vector<int> v = {1, 2, 3};",
            "list" => "COSTRUTTORI DI std::list\n\nstd::list<int> l;\nstd::list<int> l(5);\nstd::list<int> l(5, 10);\nstd::list<int> l = {1, 2, 3};",
            "set" => "COSTRUTTORI DI std::set\n\nstd::set<int> s;\nstd::set<int> s = {3, 1, 2};\nstd::set<int, std::greater<int>> s2;",
            "map" => "COSTRUTTORI DI std::map\n\nstd::map<int, int> m;\nstd::map<int, int> m = {{1, 10}, {2, 20}};",
            "stack" => "COSTRUTTORI DI std::stack\n\nstd::stack<int> st;\nstd::stack<int, std::vector<int>> st2;",
            "queue" => "COSTRUTTORI DI std::queue\n\nstd::queue<int> q;\nstd::queue<int, std::list<int>> q2;",
            _ => ConstructorsGuide()
        };

        private void ToggleApiPanel_Click(object sender, RoutedEventArgs e) =>
            ApiPanel.Visibility = ApiPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        private void ShowApi_Click(object sender, RoutedEventArgs e)
        {
            if (ApiPasswordBox.Visibility == Visibility.Visible)
            {
                ApiVisibleBox.Text = ApiPasswordBox.Password;
                ApiPasswordBox.Visibility = Visibility.Collapsed;
                ApiVisibleBox.Visibility = Visibility.Visible;
            }
            else
            {
                ApiPasswordBox.Password = ApiVisibleBox.Text;
                ApiVisibleBox.Visibility = Visibility.Collapsed;
                ApiPasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void SaveApi_Click(object sender, RoutedEventArgs e)
        {
            var key = ApiPasswordBox.Visibility == Visibility.Visible ? ApiPasswordBox.Password : ApiVisibleBox.Text;
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["OpenRouterApiKey"] = key }, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show("API key salvata.", "OpenRouter");
        }

        private void LoadConfig()
        {
            if (!File.Exists(ConfigFile)) return;
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigFile));
                if (data != null && data.TryGetValue("OpenRouterApiKey", out var key))
                    ApiPasswordBox.Password = key;
            }
            catch { }
        }

        private async void GenerateCpp_Click(object sender, RoutedEventArgs e)
        {
            string key = ApiPasswordBox.Visibility == Visibility.Visible ? ApiPasswordBox.Password : ApiVisibleBox.Text;
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Inserisci prima la API key OpenRouter.", "OpenRouter");
                return;
            }

            GeneratedCodeBox.Document.Blocks.Clear();
            GeneratedCodeBox.Document.Blocks.Add(new Paragraph(new Run("Richiesta a OpenRouter in corso...")));

            try
            {
                string code = await CallOpenRouter(key, ExerciseTextBox.Text);
                SetHighlightedCode(code);
            }
            catch (Exception ex)
            {
                SetHighlightedCode("Errore OpenRouter:\n" + ex.Message);
            }
        }

        private async Task<string> CallOpenRouter(string apiKey, string exercise)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://www.alessandrobarazzuol.com");
            client.DefaultRequestHeaders.Add("X-Title", "STL Visual Modern WPF");

            var body = new
            {
                model = "openai/gpt-4o-mini",
                temperature = 0.2,
                messages = new[]
                {
                    new { role = "system", content = "Rispondi solo con codice C++ compilabile, senza markdown. Usa codice compatibile con Dev-C++ / gnu++11." },
                    new { role = "user", content = "Genera codice C++ per questo esercizio:\n" + exercise }
                }
            };

            var json = JsonSerializer.Serialize(body);
            var res = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", new StringContent(json, Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();
            var response = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private void SetHighlightedCode(string code)
        {
            GeneratedCodeBox.Document.Blocks.Clear();
            var p = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = 14 };

            var parts = Regex.Split(code, @"(\b#include\b|\busing\b|\bnamespace\b|\bint\b|\bdouble\b|\bfloat\b|\bchar\b|\bstring\b|\bbool\b|\bvoid\b|\bif\b|\belse\b|\bfor\b|\bwhile\b|\breturn\b|\bvector\b|\blist\b|\bset\b|\bmap\b|\bstack\b|\bqueue\b|//.*?$|""[^""]*""|\b\d+\b)", RegexOptions.Multiline);

            foreach (var part in parts)
            {
                var run = new Run(part);
                if (Regex.IsMatch(part, @"^\b(include|using|namespace|int|double|float|char|string|bool|void|if|else|for|while|return|vector|list|set|map|stack|queue)\b$"))
                    run.Foreground = Brushes.DeepSkyBlue;
                else if (Regex.IsMatch(part, "^//"))
                    run.Foreground = Brushes.LightGreen;
                else if (Regex.IsMatch(part, "^\""))
                    run.Foreground = Brushes.LightSalmon;
                else if (Regex.IsMatch(part, @"^\d+$"))
                    run.Foreground = Brushes.Khaki;
                else
                    run.Foreground = Brushes.LightGray;
                p.Inlines.Add(run);
            }
            GeneratedCodeBox.Document.Blocks.Add(p);
        }

        private string GetGeneratedCode()
        {
            return new TextRange(GeneratedCodeBox.Document.ContentStart, GeneratedCodeBox.Document.ContentEnd).Text;
        }

        private void OpenExercisesFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ExercisesFolder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = ExercisesFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Non riesco ad aprire la cartella degli esercizi:\n" + ex.Message, "Apri cartella esercizi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveGeneratedCpp_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "C++ source|*.cpp|All files|*.*", FileName = "codice_generato.cpp" };
            if (dlg.ShowDialog() == true)
                File.WriteAllText(dlg.FileName, GetGeneratedCode());
        }

        private void ClearExercise_Click(object sender, RoutedEventArgs e)
        {
            ExerciseTextBox.Clear();
            SetHighlightedCode("");
        }

        private void ExampleExercise_Click(object sender, RoutedEventArgs e)
        {
            ExerciseTextBox.Text = "Scrivi un programma C++ che usi una lista per inserire 5 numeri, aggiunga un numero in testa, ordini la lista e stampi tutti gli elementi.";
        }


        private class SummaryNote
        {
            public string Testo { get; set; } = "";
            public string DisegnoBase64 { get; set; } = "";
            public DateTime SalvatoIl { get; set; } = DateTime.Now;
        }

        private void LoadSummaries()
        {
            if (!File.Exists(SummariesFile)) return;
            try
            {
                containerSummaries = JsonSerializer.Deserialize<Dictionary<string, SummaryNote>>(File.ReadAllText(SummariesFile)) ?? new();
            }
            catch { containerSummaries = new(); }
        }

        private void SaveSummaries()
        {
            File.WriteAllText(SummariesFile, JsonSerializer.Serialize(containerSummaries, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string SaveStrokesToBase64(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return "";
            using var ms = new MemoryStream();
            strokes.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        private static StrokeCollection LoadStrokesFromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return new StrokeCollection();
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(base64));
                return new StrokeCollection(ms);
            }
            catch { return new StrokeCollection(); }
        }

        private void Summary_Click(object sender, RoutedEventArgs e)
        {
            var key = current;
            if (!containerSummaries.TryGetValue(key, out var saved))
                saved = new SummaryNote();

            var win = new Window
            {
                Title = "Riassunto personale - std::" + key,
                Owner = this,
                Width = 1040,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };

            var root = new DockPanel { Margin = new Thickness(12) };
            win.Content = root;

            var title = new TextBlock
            {
                Text = "Riassunto / appunti personali per std::" + key,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var saveBtn = new Button { Content = "💾 Salva riassunto", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), Foreground = Brushes.White };
            var penBtn = new Button { Content = "✏️ Penna", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)), Foreground = Brushes.White };
            var eraserBtn = new Button { Content = "🧽 Gomma", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), Foreground = Brushes.White };
            var undoBtn = new Button { Content = "↶ Annulla tratto", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0) };
            var clearDrawBtn = new Button { Content = "🧽 Pulisci disegno", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
            var closeBtn = new Button { Content = "Chiudi", Padding = new Thickness(14, 8, 14, 8) };
            buttons.Children.Add(saveBtn);
            buttons.Children.Add(penBtn);
            buttons.Children.Add(eraserBtn);
            buttons.Children.Add(undoBtn);
            buttons.Children.Add(clearDrawBtn);
            buttons.Children.Add(closeBtn);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(grid);

            var notes = new TextBox
            {
                Text = saved.Testo,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Padding = new Thickness(8)
            };
            Grid.SetRow(notes, 0);
            grid.Children.Add(notes);

            var drawBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4)
            };
            var colorBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetRow(colorBar, 2);
            grid.Children.Add(colorBar);

            TextBlock colorLabel = new TextBlock
            {
                Text = "Colore penna:",
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold
            };
            colorBar.Children.Add(colorLabel);

            InkCanvas ink = new InkCanvas();

            Button MakeColorButton(string label, Color color)
            {
                var b = new Button
                {
                    Content = label,
                    Width = 76,
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(color),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184))
                };
                b.Click += (_, __) =>
                {
                    ink.EditingMode = InkCanvasEditingMode.Ink;
                    ink.DefaultDrawingAttributes.Color = color;
                };
                return b;
            }

            Grid.SetRow(drawBorder, 4);
            grid.Children.Add(drawBorder);

            ink = new InkCanvas
            {
                Background = Brushes.White,
                EditingMode = InkCanvasEditingMode.Ink,
                Width = 1600,
                Height = 1100,
                MinWidth = 1600,
                MinHeight = 1100,
                Strokes = LoadStrokesFromBase64(saved.DisegnoBase64)
            };
            ink.DefaultDrawingAttributes.Width = 3;
            ink.DefaultDrawingAttributes.Height = 3;
            ink.DefaultDrawingAttributes.Color = Colors.Black;

            var drawingScroll = new ScrollViewer
            {
                Content = ink,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                Background = Brushes.White
            };
            drawBorder.Child = drawingScroll;

            colorBar.Children.Add(MakeColorButton("Nero", Colors.Black));
            colorBar.Children.Add(MakeColorButton("Blu", Colors.Blue));
            colorBar.Children.Add(MakeColorButton("Rosso", Colors.Red));
            colorBar.Children.Add(MakeColorButton("Verde", Colors.Green));
            colorBar.Children.Add(MakeColorButton("Viola", Colors.Purple));

            saveBtn.Click += async (_, __) =>
            {
                try
                {
                    containerSummaries[key] = new SummaryNote
                    {
                        Testo = notes.Text,
                        DisegnoBase64 = SaveStrokesToBase64(ink.Strokes),
                        SalvatoIl = DateTime.Now
                    };

                    // Salva localmente i riassunti e aggiorna anche il database unico.
                    SaveSummaries();

                    // Come per guide ed esercizi, esporta subito anche su Drive locale/OAuth.
                    string driveInfo = TryExportDatabaseToGoogleDriveFolder(false);
                    driveInfo += await TryUploadDatabaseToGoogleDriveOAuthAsync(false);

                    MessageBox.Show(
                        "Riassunto salvato per std::" + key + ".\n\n" +
                        "Il riassunto/disegno è stato incluso nel database unico e sincronizzato su Drive come guide ed esercizi." +
                        driveInfo,
                        "Riassunto",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore durante il salvataggio del riassunto:\n" + ex.Message, "Riassunto", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            penBtn.Click += (_, __) => ink.EditingMode = InkCanvasEditingMode.Ink;
            eraserBtn.Click += (_, __) => ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
            undoBtn.Click += (_, __) =>
            {
                if (ink.Strokes.Count > 0)
                    ink.Strokes.RemoveAt(ink.Strokes.Count - 1);
            };
            clearDrawBtn.Click += (_, __) => ink.Strokes.Clear();
            closeBtn.Click += (_, __) => win.Close();

            win.ShowDialog();
        }


        private class SavedExercise
        {
            public string Consegna { get; set; } = "";
            public string Soluzione { get; set; } = "";
            public DateTime SalvatoIl { get; set; } = DateTime.Now;
            public Dictionary<string, SummaryNote> RiassuntiContenitori { get; set; } = new();
        }

        private class BackupDatabase
        {
            public Dictionary<string, string> Guide { get; set; } = new();
            public Dictionary<string, SavedExercise> Esercizi { get; set; } = new();
            public List<string> Cartelle { get; set; } = new();
            public Dictionary<string, SummaryNote> Riassunti { get; set; } = new();
            public DateTime EsportatoIl { get; set; } = DateTime.Now;
        }

        private string SafeFolderName(string text)
        {
            text = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return "Generale";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                text = text.Replace(c, '_');
            return text;
        }

        private string? SelectedExercisePath()
        {
            if (ExerciseTree.SelectedItem is TreeViewItem item && item.Tag is string path)
                return path;
            return null;
        }

        private TextBlock MakeTreeHeader(string text, Brush foreground)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 14,
                TextWrapping = TextWrapping.NoWrap
            };
        }

        private void AddExerciseTreeItems(ItemsControl parent, string folder, Brush? foreground = null)
        {
            Brush brush = foreground ?? Brushes.Black;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => System.IO.Path.GetFileName(d)))
            {
                var node = new TreeViewItem
                {
                    Header = MakeTreeHeader("📁 " + System.IO.Path.GetFileName(dir), brush),
                    Tag = dir,
                    IsExpanded = true,
                    Foreground = brush
                };
                parent.Items.Add(node);
                AddExerciseTreeItems(node, dir, brush);
            }

            foreach (var file in Directory.GetFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTime))
            {
                parent.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader("📄 " + System.IO.Path.GetFileNameWithoutExtension(file), brush),
                    Tag = file,
                    Foreground = brush
                });
            }
        }

        private void RefreshSavedExercisesList()
        {
            ExerciseTree.Items.Clear();
            if (!Directory.Exists(ExercisesFolder)) Directory.CreateDirectory(ExercisesFolder);

            ExerciseTree.Background = Brushes.White;
            ExerciseTree.Foreground = Brushes.Black;
            var root = new TreeViewItem { Header = MakeTreeHeader("📁 esercizi_salvati", Brushes.Black), Tag = ExercisesFolder, IsExpanded = true, Foreground = Brushes.Black };
            ExerciseTree.Items.Add(root);
            AddExerciseTreeItems(root, ExercisesFolder, Brushes.Black);
        }

        private void RefreshExercises_Click(object sender, RoutedEventArgs e) => RefreshSavedExercisesList();

        private void ExerciseTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // La vista ad albero serve solo per selezionare esercizi/cartelle.
            // Il doppio clic su un file JSON carica direttamente consegna e soluzione.
        }

        private async void SaveExercise_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(ExercisesFolder);

            var dlg = new SaveFileDialog
            {
                Filter = "Esercizio STL salvato|*.json|All files|*.*",
                InitialDirectory = ExercisesFolder,
                FileName = "esercizio_stl_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json"
            };
            if (dlg.ShowDialog() != true) return;

            var data = new SavedExercise
            {
                Consegna = ExerciseTextBox.Text,
                Soluzione = GetGeneratedCode(),
                RiassuntiContenitori = new Dictionary<string, SummaryNote>(containerSummaries),
                SalvatoIl = DateTime.Now
            };
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            currentLoadedExerciseFile = dlg.FileName;
            RefreshSavedExercisesList();
            string driveInfo = TryExportDatabaseToGoogleDriveFolder(false);
            driveInfo += await TryUploadDatabaseToGoogleDriveOAuthAsync(false);
            MessageBox.Show("Esercizio salvato con consegna e soluzione, dentro la cartella scelta." + driveInfo, "Salva esercizio");
        }

        private async void UpdateExercise_Click(object sender, RoutedEventArgs e)
        {
            var file = currentLoadedExerciseFile;

            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                var selected = SelectedExercisePath();
                if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
                    file = selected;
            }

            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                MessageBox.Show("Prima carica o seleziona un esercizio già salvato. Se vuoi crearne uno nuovo usa 'Salva esercizio'.", "Aggiorna esercizio");
                return;
            }

            var result = MessageBox.Show(
                "Vuoi sovrascrivere l'esercizio caricato con il testo, il codice e i riassunti attuali?",
                "Aggiorna esercizio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var data = new SavedExercise
                {
                    Consegna = ExerciseTextBox.Text,
                    Soluzione = GetGeneratedCode(),
                    RiassuntiContenitori = new Dictionary<string, SummaryNote>(containerSummaries),
                    SalvatoIl = DateTime.Now
                };

                File.WriteAllText(file, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                currentLoadedExerciseFile = file;
                RefreshSavedExercisesList();
                string driveInfo = TryExportDatabaseToGoogleDriveFolder(false);
                driveInfo += await TryUploadDatabaseToGoogleDriveOAuthAsync(false);
                MessageBox.Show("Esercizio aggiornato e sovrascritto correttamente." + driveInfo, "Aggiorna esercizio");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante l\'aggiornamento:\n" + ex.Message, "Aggiorna esercizio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadExercise_Click(object sender, RoutedEventArgs e)
        {
            string? file = SelectedExercisePath();

            if (string.IsNullOrWhiteSpace(file) || Directory.Exists(file) || !File.Exists(file))
            {
                var dlg = new OpenFileDialog { Filter = "Esercizio STL salvato|*.json|All files|*.*", InitialDirectory = ExercisesFolder };
                if (dlg.ShowDialog() != true) return;
                file = dlg.FileName;
            }

            LoadExerciseFromFile(file, showMessage: true);
        }

        private void ExerciseTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ExerciseTree.SelectedItem is not TreeViewItem item || item.Tag is not string path) return;
            if (Directory.Exists(path)) return;
            if (!File.Exists(path)) return;

            LoadExerciseFromFile(path, showMessage: false);
            e.Handled = true;
        }

        private void LoadExerciseFromFile(string file, bool showMessage)
        {
            try
            {
                var data = JsonSerializer.Deserialize<SavedExercise>(File.ReadAllText(file));
                if (data == null) throw new InvalidOperationException("File non valido.");

                ExerciseTextBox.Text = data.Consegna;
                SetHighlightedCode(data.Soluzione);
                currentLoadedExerciseFile = file;

                if (data.RiassuntiContenitori != null && data.RiassuntiContenitori.Count > 0)
                {
                    foreach (var item in data.RiassuntiContenitori) containerSummaries[item.Key] = item.Value;
                    SaveSummaries();
                }

                if (showMessage)
                    MessageBox.Show("Esercizio caricato: consegna e soluzione sono state ripristinate.", "Carica esercizio");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore nel caricamento:\n" + ex.Message, "Carica esercizio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteExercise_Click(object sender, RoutedEventArgs e)
        {
            var path = SelectedExercisePath();
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                MessageBox.Show("Seleziona prima un esercizio o una cartella nella vista ad albero.", "Elimina esercizio");
                return;
            }

            bool isDir = Directory.Exists(path);
            string nome = isDir ? System.IO.Path.GetFileName(path) : System.IO.Path.GetFileName(path);
            var result = MessageBox.Show(
                isDir ? $"Vuoi eliminare la cartella '{nome}' con tutti gli esercizi contenuti?" : $"Vuoi eliminare l'esercizio '{nome}'?",
                "Conferma eliminazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (isDir)
                {
                    if (System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar) == System.IO.Path.GetFullPath(ExercisesFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar))
                    {
                        MessageBox.Show("Non posso eliminare la cartella principale degli esercizi.", "Elimina esercizio");
                        return;
                    }
                    Directory.Delete(path, true);
                }
                else
                    File.Delete(path);

                RefreshSavedExercisesList();
                MessageBox.Show("Eliminazione completata.", "Elimina esercizio");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante l'eliminazione:\n" + ex.Message, "Elimina esercizio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CppExample_Click(object sender, RoutedEventArgs e) => SetCppEditorText(DefaultCppExample());

        private string DefaultCppExample() =>
@"#include <iostream>
#include <vector>
#include <algorithm>
using namespace std;

int main() {
    vector<int> v = {5, 2, 9, 1};
    sort(v.begin(), v.end());

    for (int x : v) {
        cout << x << "" "";
    }

    return 0;
}";


        private string GetCppEditorText()
        {
            return new TextRange(CppEditorBox.Document.ContentStart, CppEditorBox.Document.ContentEnd).Text;
        }

        private void SetCppEditorText(string code)
        {
            CppEditorBox.Document.Blocks.Clear();
            CppEditorBox.Document.Blocks.Add(new Paragraph(new Run(code))
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14
            });
            HighlightCppEditor();
        }

        private void HighlightEditor_Click(object sender, RoutedEventArgs e)
        {
            HighlightCppEditor();
        }

        private void HighlightCppEditor()
        {
            string code = GetCppEditorText();

            CppEditorBox.Document.Blocks.Clear();
            var paragraph = new Paragraph
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Margin = new Thickness(0)
            };

            var regex = new Regex(@"(//.*?$|/\*.*?\*/|""[^""]*""|\b#include\b|\busing\b|\bnamespace\b|\bint\b|\bdouble\b|\bfloat\b|\bchar\b|\bstring\b|\bbool\b|\bvoid\b|\bif\b|\belse\b|\bfor\b|\bwhile\b|\bdo\b|\bswitch\b|\bcase\b|\bbreak\b|\bcontinue\b|\breturn\b|\bclass\b|\bstruct\b|\bpublic\b|\bprivate\b|\bprotected\b|\bvector\b|\blist\b|\bset\b|\bmap\b|\bstack\b|\bqueue\b|\bcout\b|\bcin\b|\bstd\b|\bsort\b|\breverse\b|\b\d+\b)", RegexOptions.Multiline | RegexOptions.Singleline);

            int last = 0;
            foreach (Match m in regex.Matches(code))
            {
                if (m.Index > last)
                    paragraph.Inlines.Add(new Run(code.Substring(last, m.Index - last)) { Foreground = Brushes.LightGray });

                var run = new Run(m.Value);

                if (m.Value.StartsWith("//") || m.Value.StartsWith("/*"))
                    run.Foreground = Brushes.LightGreen;
                else if (m.Value.StartsWith("\""))
                    run.Foreground = Brushes.LightSalmon;
                else if (Regex.IsMatch(m.Value, @"^\d+$"))
                    run.Foreground = Brushes.Khaki;
                else if (m.Value == "#include")
                    run.Foreground = Brushes.Violet;
                else
                    run.Foreground = Brushes.DeepSkyBlue;

                paragraph.Inlines.Add(run);
                last = m.Index + m.Length;
            }

            if (last < code.Length)
                paragraph.Inlines.Add(new Run(code.Substring(last)) { Foreground = Brushes.LightGray });

            CppEditorBox.Document.Blocks.Add(paragraph);
        }

        private async void CompileAndRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "STLVisualModernWPF_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);

                string cpp = System.IO.Path.Combine(temp, "main.cpp");
                string exe = System.IO.Path.Combine(temp, "main.exe");
                File.WriteAllText(cpp, GetCppEditorText());

                var psi = new ProcessStartInfo
                {
                    FileName = "g++",
                    Arguments = $"\"{cpp}\" -o \"{exe}\" -std=gnu++11",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = temp
                };

                var p = Process.Start(psi);
                if (p == null) return;

                string err = await p.StandardError.ReadToEndAsync();
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode != 0)
                {
                    CompilerOutputBox.Text =
                        "ERRORE COMPILAZIONE:\n\n" +
                        err + "\n" + output +
                        "\n\nNota: se compare 'cannot open output file', ora il programma usa una cartella temporanea nuova. " +
                        "Se l'errore continua, controlla antivirus/permessi o che g++ funzioni dal Prompt.";
                    return;
                }

                var runPsi = new ProcessStartInfo
                {
                    FileName = exe,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = temp
                };

                var run = Process.Start(runPsi);
                if (run == null) return;

                string runOut = await run.StandardOutput.ReadToEndAsync();
                string runErr = await run.StandardError.ReadToEndAsync();
                await run.WaitForExitAsync();

                CompilerOutputBox.Text =
                    "COMPILAZIONE OK\n\n" +
                    "File temporaneo:\n" + exe + "\n\n" +
                    "OUTPUT:\n" + runOut + "\n" + runErr;
            }
            catch (Exception ex)
            {
                CompilerOutputBox.Text =
                    "Errore: serve g++ installato e presente nel PATH.\n\n" +
                    "Se usi Dev-C++, aggiungi al PATH la cartella bin di MinGW, ad esempio:\n" +
                    "C:\\Program Files (x86)\\Dev-Cpp\\MinGW64\\bin\n\n" +
                    ex.Message;
            }
        }

        private void SaveSource_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "C++ source|*.cpp|All files|*.*", FileName = "programma.cpp" };
            if (dlg.ShowDialog() == true)
                File.WriteAllText(dlg.FileName, GetCppEditorText());
        }

        private TreeNodeDemo? treeRoot;
        private readonly Random treeRandom = new();
        private double treeZoom = 1.0;
        private List<TreeNodeDemo> manualVisitOrder = new();
        private List<TreeNodeDemo> manualVisitedNodes = new();
        private int manualVisitIndex = 0;
        private string manualVisitName = "";

        private int ReadTreeInt(TextBox box, int fallback)
        {
            return int.TryParse(box.Text, out int n) ? n : fallback;
        }

        private void TreeInsert_Click(object sender, RoutedEventArgs e)
        {
            int value = ReadTreeInt(TreeValueBox, 10);
            InsertTreeValue(value);
            DrawTree();
            TreeOutputBox.Text = $"Inserito nodo {value}.";
        }

        private void InsertTreeValue(int value)
        {
            treeRoot = InsertTreeValue(treeRoot, value);
        }

        private TreeNodeDemo InsertTreeValue(TreeNodeDemo? node, int value)
        {
            if (node == null) return new TreeNodeDemo(value);
            if (value < node.Value) node.Left = InsertTreeValue(node.Left, value);
            else node.Right = InsertTreeValue(node.Right, value);
            return node;
        }

        private void TreeDelete_Click(object sender, RoutedEventArgs e)
        {
            int value = ReadTreeInt(TreeValueBox, 10);
            treeRoot = DeleteTreeValue(treeRoot, value);
            DrawTree();
            TreeOutputBox.Text = $"Eliminato valore {value}, se presente.";
        }

        private TreeNodeDemo? DeleteTreeValue(TreeNodeDemo? node, int value)
        {
            if (node == null) return null;

            if (value < node.Value)
                node.Left = DeleteTreeValue(node.Left, value);
            else if (value > node.Value)
                node.Right = DeleteTreeValue(node.Right, value);
            else
            {
                if (node.Left == null) return node.Right;
                if (node.Right == null) return node.Left;

                TreeNodeDemo min = FindMin(node.Right);
                node.Value = min.Value;
                node.Right = DeleteTreeValue(node.Right, min.Value);
            }

            return node;
        }

        private TreeNodeDemo FindMin(TreeNodeDemo node)
        {
            while (node.Left != null)
                node = node.Left;
            return node;
        }

        private void TreeRandom_Click(object sender, RoutedEventArgs e)
        {
            int min = ReadTreeInt(TreeMinBox, 0);
            int max = ReadTreeInt(TreeMaxBox, 40);
            int count = ReadTreeInt(TreeCountBox, 12);
            if (max <= min) max = min + 1;

            treeRoot = null;
            for (int i = 0; i < count; i++)
                InsertTreeValue(treeRandom.Next(min, max));

            DrawTree();
            TreeOutputBox.Text = $"Creato albero casuale con {count} nodi.";
        }

        private void TreeBalanced_Click(object sender, RoutedEventArgs e)
        {
            int min = ReadTreeInt(TreeMinBox, 0);
            int max = ReadTreeInt(TreeMaxBox, 40);
            int count = ReadTreeInt(TreeCountBox, 12);
            if (max <= min) max = min + 1;

            var values = new List<int>();
            for (int i = 0; i < count; i++)
                values.Add(treeRandom.Next(min, max));

            values.Sort();
            treeRoot = BuildBalanced(values, 0, values.Count - 1);

            DrawTree();
            TreeOutputBox.Text = $"Creato albero bilanciato con {count} nodi.";
        }

        private TreeNodeDemo? BuildBalanced(List<int> values, int start, int end)
        {
            if (start > end) return null;

            int mid = (start + end) / 2;
            var node = new TreeNodeDemo(values[mid]);
            node.Left = BuildBalanced(values, start, mid - 1);
            node.Right = BuildBalanced(values, mid + 1, end);
            return node;
        }

        private void TreeClear_Click(object sender, RoutedEventArgs e)
        {
            treeRoot = null;
            DrawTree();
            TreeOutputBox.Text = "Albero cancellato.";
        }

        private void TreePreorder_Click(object sender, RoutedEventArgs e)
        {
            var result = new List<TreeNodeDemo>();
            PreorderNodes(treeRoot, result);
            PrepareManualVisit(result, "PREORDER (radice, sinistra, destra)");
        }

        private void TreeInorder_Click(object sender, RoutedEventArgs e)
        {
            var result = new List<TreeNodeDemo>();
            InorderNodes(treeRoot, result);
            PrepareManualVisit(result, "INORDER (sinistra, radice, destra)");
        }

        private void TreePostorder_Click(object sender, RoutedEventArgs e)
        {
            var result = new List<TreeNodeDemo>();
            PostorderNodes(treeRoot, result);
            PrepareManualVisit(result, "POSTORDER (sinistra, destra, radice)");
        }

        private void TreeCount_Click(object sender, RoutedEventArgs e)
        {
            int value = ReadTreeInt(TreeValueBox, 10);
            int count = CountValue(treeRoot, value);
            TreeOutputBox.Text = $"Il valore {value} compare {count} volte.";
        }

        private void TreeHeight_Click(object sender, RoutedEventArgs e)
        {
            TreeOutputBox.Text = $"Altezza albero: {TreeHeight(treeRoot)}\nNumero nodi: {TreeNodeCount(treeRoot)}";
        }

        private void TreeZoomIn_Click(object sender, RoutedEventArgs e)
        {
            treeZoom = Math.Min(2.0, treeZoom + 0.15);
            DrawTree();
        }

        private void TreeZoomOut_Click(object sender, RoutedEventArgs e)
        {
            treeZoom = Math.Max(0.35, treeZoom - 0.15);
            DrawTree();
        }

        private void TreeFit_Click(object sender, RoutedEventArgs e)
        {
            treeZoom = 1.0;
            DrawTree();
        }

        private void Preorder(TreeNodeDemo? node, List<int> result)
        {
            if (node == null) return;
            result.Add(node.Value);
            Preorder(node.Left, result);
            Preorder(node.Right, result);
        }

        private void PreorderNodes(TreeNodeDemo? node, List<TreeNodeDemo> result)
        {
            if (node == null) return;
            result.Add(node);
            PreorderNodes(node.Left, result);
            PreorderNodes(node.Right, result);
        }


        private void Inorder(TreeNodeDemo? node, List<int> result)
        {
            if (node == null) return;
            Inorder(node.Left, result);
            result.Add(node.Value);
            Inorder(node.Right, result);
        }

        private void InorderNodes(TreeNodeDemo? node, List<TreeNodeDemo> result)
        {
            if (node == null) return;
            InorderNodes(node.Left, result);
            result.Add(node);
            InorderNodes(node.Right, result);
        }


        private void Postorder(TreeNodeDemo? node, List<int> result)
        {
            if (node == null) return;
            Postorder(node.Left, result);
            Postorder(node.Right, result);
            result.Add(node.Value);
        }

        private void PostorderNodes(TreeNodeDemo? node, List<TreeNodeDemo> result)
        {
            if (node == null) return;
            PostorderNodes(node.Left, result);
            PostorderNodes(node.Right, result);
            result.Add(node);
        }


        private int CountValue(TreeNodeDemo? node, int value)
        {
            if (node == null) return 0;
            return (node.Value == value ? 1 : 0) + CountValue(node.Left, value) + CountValue(node.Right, value);
        }

        private int TreeHeight(TreeNodeDemo? node)
        {
            if (node == null) return 0;
            return 1 + Math.Max(TreeHeight(node.Left), TreeHeight(node.Right));
        }

        private int TreeNodeCount(TreeNodeDemo? node)
        {
            if (node == null) return 0;
            return 1 + TreeNodeCount(node.Left) + TreeNodeCount(node.Right);
        }


        private string NodeSequenceText(List<TreeNodeDemo> nodes)
        {
            return string.Join("  ", nodes.Select(n => n.Value));
        }

        private void PrepareManualVisit(List<TreeNodeDemo> order, string visitName)
        {
            manualVisitOrder = order;
            manualVisitedNodes = new List<TreeNodeDemo>();
            manualVisitIndex = 0;
            manualVisitName = visitName;

            if (manualVisitOrder.Count == 0)
            {
                TreeOutputBox.Text = "Albero vuoto.";
                DrawTree();
                return;
            }

            DrawTree();
            TreeOutputBox.Text =
                manualVisitName + "\n\n" +
                "Sequenza completa:\n" + NodeSequenceText(manualVisitOrder) + "\n\n" +
                "Nota: se due nodi hanno lo stesso valore, verranno comunque illuminati uno alla volta.\n\n" +
                "Premi 'Avanti visita' per illuminare il primo nodo.";
        }

        private void TreeNextVisit_Click(object sender, RoutedEventArgs e)
        {
            if (manualVisitOrder == null || manualVisitOrder.Count == 0)
            {
                TreeOutputBox.Text = "Prima scegli una visita: Preorder, Inorder oppure Postorder.";
                return;
            }

            if (manualVisitIndex >= manualVisitOrder.Count)
            {
                TreeOutputBox.Text =
                    manualVisitName + " già completata.\n\n" +
                    "Sequenza:\n" + NodeSequenceText(manualVisitOrder);
                DrawTree(manualVisitedNodes, null);
                return;
            }

            TreeNodeDemo currentNodeRef = manualVisitOrder[manualVisitIndex];
            manualVisitedNodes.Add(currentNodeRef);
            manualVisitIndex++;

            DrawTree(manualVisitedNodes, currentNodeRef);

            TreeOutputBox.Text =
                manualVisitName + "\n\n" +
                "Sequenza completa:\n" + NodeSequenceText(manualVisitOrder) + "\n\n" +
                "Visitati finora:\n" + NodeSequenceText(manualVisitedNodes) + "\n\n" +
                "Nodo corrente illuminato: " + currentNodeRef.Value + "\n" +
                "Passo: " + manualVisitIndex + " / " + manualVisitOrder.Count;

            if (manualVisitIndex >= manualVisitOrder.Count)
                TreeOutputBox.Text += "\n\nVisita completata.";
        }

        private void TreeResetVisit_Click(object sender, RoutedEventArgs e)
        {
            manualVisitedNodes = new List<TreeNodeDemo>();
            manualVisitIndex = 0;
            DrawTree();

            if (manualVisitOrder != null && manualVisitOrder.Count > 0)
            {
                TreeOutputBox.Text =
                    manualVisitName + "\n\n" +
                    "Visita resettata.\nPremi 'Avanti visita' per ricominciare.";
            }
            else
            {
                TreeOutputBox.Text = "Nessuna visita selezionata.";
            }
        }

        private async Task AnimateTreeVisit(List<int> order, string visitName)
        {
            // Metodo mantenuto solo per compatibilità interna.
            // La visita didattica attuale usa i riferimenti ai nodi e il pulsante 'Avanti visita',
            // così due nodi con lo stesso valore non vengono evidenziati insieme.
            TreeOutputBox.Text = visitName + "\n\nUsa la visita manuale con il pulsante 'Avanti visita'.";
            await Task.CompletedTask;
        }

        private void DrawTree(List<TreeNodeDemo>? visitedNodes = null, TreeNodeDemo? currentNode = null)
        {
            TreeCanvas.Children.Clear();

            if (treeRoot == null)
            {
                var empty = new TextBlock
                {
                    Text = "[ albero vuoto ]",
                    Foreground = Brushes.LightGray,
                    FontSize = 18
                };
                Canvas.SetLeft(empty, 30);
                Canvas.SetTop(empty, 30);
                TreeCanvas.Children.Add(empty);
                return;
            }

            int nodes = TreeNodeCount(treeRoot);
            int height = TreeHeight(treeRoot);

            // Cerca di far stare l'albero nell'area visibile. Se i nodi sono molti,
            // riduce automaticamente distanza e dimensione.
            TreeCanvas.Width = Math.Max(900, Math.Min(1500, nodes * 85));
            TreeCanvas.Height = Math.Max(560, Math.Min(900, 120 + height * 82));
            TreeCanvas.LayoutTransform = new ScaleTransform(treeZoom, treeZoom);

            double width = TreeCanvas.Width;
            double initialSpace = Math.Max(42, Math.Min(width / 4, 240));
            DrawTreeNode(treeRoot, width / 2, 48, initialSpace, visitedNodes ?? new List<TreeNodeDemo>(), currentNode);
        }

        private void DrawTreeNode(TreeNodeDemo? node, double x, double y, double space, List<TreeNodeDemo> visitedNodes, TreeNodeDemo? currentNode)
        {
            if (node == null) return;

            int totalNodes = TreeNodeCount(treeRoot);
            double diameter = totalNodes <= 12 ? 42 : totalNodes <= 20 ? 34 : 28;
            double radius = diameter / 2;
            double verticalStep = totalNodes <= 12 ? 72 : totalNodes <= 20 ? 58 : 48;
            double textTopOffset = totalNodes <= 12 ? 9 : totalNodes <= 20 ? 7 : 6;
            double fontSize = totalNodes <= 12 ? 12 : totalNodes <= 20 ? 10 : 9;

            if (node.Left != null)
            {
                double childX = x - space;
                double childY = y + verticalStep;

                TreeCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = y + radius,
                    X2 = childX,
                    Y2 = childY - radius,
                    Stroke = Brushes.SlateGray,
                    StrokeThickness = 2
                });

                DrawTreeNode(node.Left, childX, childY, Math.Max(38, space / 2), visitedNodes, currentNode);
            }

            if (node.Right != null)
            {
                double childX = x + space;
                double childY = y + verticalStep;

                TreeCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = y + radius,
                    X2 = childX,
                    Y2 = childY - radius,
                    Stroke = Brushes.SlateGray,
                    StrokeThickness = 2
                });

                DrawTreeNode(node.Right, childX, childY, Math.Max(38, space / 2), visitedNodes, currentNode);
            }

            bool visited = visitedNodes.Contains(node);
            bool isCurrent = currentNode != null && Object.ReferenceEquals(currentNode, node);

            Brush fill = isCurrent
                ? Brushes.Yellow
                : visited
                    ? Brushes.OrangeRed
                    : new SolidColorBrush(Color.FromRgb(0, 122, 204));

            Brush foreground = isCurrent ? Brushes.Black : Brushes.White;

            var ellipse = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, x - radius);
            Canvas.SetTop(ellipse, y - radius);
            TreeCanvas.Children.Add(ellipse);

            var text = new TextBlock
            {
                Text = node.Value.ToString(),
                Foreground = foreground,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = fontSize,
                Width = 54,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(text, x - radius);
            Canvas.SetTop(text, y - textTopOffset);
            TreeCanvas.Children.Add(text);
        }


        // =========================================================
        // DEBUGGER DIDATTICO C++ - variabili passo passo
        // Non modifica il programma: è una scheda separata che simula
        // codice semplice C++ e mostra le variabili a ogni pressione di Procedi.
        // =========================================================
        private class DebugStepInfo
        {
            public int LineIndex { get; set; }
            public string LineText { get; set; } = "";
            public string Description { get; set; } = "";
            public Dictionary<string, string> Variables { get; set; } = new();
        }

        private readonly List<DebugStepInfo> _debugSteps = new();
        private int _debugStepIndex = -1;
        private string[] _debugLines = Array.Empty<string>();

        private void DebugExample_Click(object sender, RoutedEventArgs e)
        {
            SetDebugCode(@"#include <iostream>
using namespace std;

int main() {
    int a = 3;
    int b = 5;
    int somma = a + b;

    for (int i = 0; i < 3; i++) {
        somma = somma + i;
    }

    cout << somma;
    return 0;
}");
        }

        private void DebugCopyFromEditor_Click(object sender, RoutedEventArgs e)
        {
            SetDebugCode(GetCppEditorText());
        }

        private void DebugCopyFromGenerated_Click(object sender, RoutedEventArgs e)
        {
            SetDebugCode(GetGeneratedCode());
        }

        private void DebugReset_Click(object sender, RoutedEventArgs e)
        {
            _debugStepIndex = -1;
            DebugVariablesBox.Text = "Premi Avvia debugger, poi Procedi.";
            DebugOutputBox.Text = "Debugger resettato.";
            HighlightDebugLine(-1);
        }

        private void DebugStart_Click(object sender, RoutedEventArgs e)
        {
            string code = GetDebugCode();
            _debugSteps.Clear();
            _debugStepIndex = -1;
            _debugLines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            try
            {
                BuildDebugSteps(_debugLines);
                if (_debugSteps.Count == 0)
                {
                    DebugVariablesBox.Text = "Nessuna variabile trovata.\nIl debugger è didattico: funziona meglio con int, double, string, bool, assegnazioni, ++, -- e for semplici.";
                    DebugOutputBox.Text = "Nessun passo generato.";
                    HighlightDebugLine(-1);
                    return;
                }

                DebugVariablesBox.Text = "Debugger pronto. Premi Procedi.";
                DebugOutputBox.Text = $"Passi trovati: {_debugSteps.Count}.";
                HighlightDebugLine(-1);
            }
            catch (Exception ex)
            {
                DebugVariablesBox.Text = "Errore nella simulazione.";
                DebugOutputBox.Text = ex.Message;
            }
        }

        private void DebugNext_Click(object sender, RoutedEventArgs e)
        {
            if (_debugSteps.Count == 0)
            {
                DebugStart_Click(sender, e);
                if (_debugSteps.Count == 0) return;
            }

            if (_debugStepIndex + 1 >= _debugSteps.Count)
            {
                DebugOutputBox.Text = "Fine programma: non ci sono altri passi.";
                return;
            }

            _debugStepIndex++;
            var step = _debugSteps[_debugStepIndex];
            HighlightDebugLine(step.LineIndex);
            DebugVariablesBox.Text = FormatVariables(step.Variables);
            DebugOutputBox.Text = $"Passo {_debugStepIndex + 1}/{_debugSteps.Count}\nRiga {step.LineIndex + 1}: {step.LineText.Trim()}\n\n{step.Description}";
        }

        private string GetDebugCode()
        {
            return new TextRange(DebugCodeBox.Document.ContentStart, DebugCodeBox.Document.ContentEnd).Text;
        }

        private void SetDebugCode(string code)
        {
            DebugCodeBox.Document.Blocks.Clear();
            DebugCodeBox.Document.Blocks.Add(new Paragraph(new Run(code ?? ""))
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Margin = new Thickness(0)
            });
        }

        private void HighlightDebugLine(int selectedLine)
        {
            string code = GetDebugCode();
            var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            DebugCodeBox.Document.Blocks.Clear();

            for (int i = 0; i < lines.Length; i++)
            {
                var p = new Paragraph { Margin = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 14 };
                var r = new Run(lines[i]);
                r.Foreground = Brushes.LightGray;
                if (i == selectedLine)
                {
                    p.Background = new SolidColorBrush(Color.FromRgb(124, 58, 237));
                    r.Foreground = Brushes.White;
                    r.FontWeight = FontWeights.Bold;
                }
                p.Inlines.Add(r);
                DebugCodeBox.Document.Blocks.Add(p);
            }
        }

        private void BuildDebugSteps(string[] lines)
        {
            var vars = new Dictionary<string, string>();
            var braceMatch = BuildBraceMatches(lines);
            ExecuteDebugBlock(0, lines.Length - 1, vars, lines, braceMatch, 0);
        }

        private Dictionary<int, int> BuildBraceMatches(string[] lines)
        {
            var stack = new Stack<int>();
            var map = new Dictionary<int, int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("{")) stack.Push(i);
                if (lines[i].Contains("}") && stack.Count > 0)
                {
                    int open = stack.Pop();
                    map[open] = i;
                    map[i] = open;
                }
            }
            return map;
        }

        private void ExecuteDebugBlock(int start, int end, Dictionary<string, string> vars, string[] lines, Dictionary<int, int> braceMatch, int depth)
        {
            if (_debugSteps.Count > 700 || depth > 20) return;

            for (int i = start; i <= end && i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = CleanDebugLine(raw);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (IsStructuralCppLine(line)) continue;

                if (line.StartsWith("for") && TryParseFor(line, out var init, out var cond, out var inc))
                {
                    ExecuteDebugStatement(init, vars, i, raw, "Inizializzazione del ciclo for");
                    int close = braceMatch.TryGetValue(i, out var c) ? c : FindNextClosingBrace(lines, i);
                    int guard = 0;
                    while (EvaluateCondition(cond, vars) && guard < 100 && _debugSteps.Count < 700)
                    {
                        AddDebugStep(i, raw, "Controllo del ciclo for: condizione vera. Entro nel corpo del ciclo.", vars);
                        ExecuteDebugBlock(i + 1, close - 1, vars, lines, braceMatch, depth + 1);
                        ExecuteDebugStatement(inc, vars, i, raw, "Incremento finale del ciclo for");
                        guard++;
                    }
                    AddDebugStep(i, raw, "Controllo del ciclo for: condizione falsa. Esco dal ciclo.", vars);
                    i = close;
                    continue;
                }

                if (line.StartsWith("while") && TryParseWhile(line, out var whileCond))
                {
                    int close = braceMatch.TryGetValue(i, out var c) ? c : FindNextClosingBrace(lines, i);
                    int guard = 0;
                    while (EvaluateCondition(whileCond, vars) && guard < 100 && _debugSteps.Count < 700)
                    {
                        AddDebugStep(i, raw, "Controllo del ciclo while: condizione vera. Entro nel corpo del ciclo.", vars);
                        ExecuteDebugBlock(i + 1, close - 1, vars, lines, braceMatch, depth + 1);
                        guard++;
                    }
                    AddDebugStep(i, raw, "Controllo del ciclo while: condizione falsa. Esco dal ciclo.", vars);
                    i = close;
                    continue;
                }

                ExecuteDebugStatement(line, vars, i, raw, null);
            }
        }

        private string CleanDebugLine(string raw)
        {
            string line = Regex.Replace(raw, @"//.*$", "").Trim();
            line = line.Trim('{', '}').Trim();
            if (line.EndsWith(";")) line = line[..^1].Trim();
            return line;
        }

        private bool IsStructuralCppLine(string line)
        {
            return line.StartsWith("#include") || line.StartsWith("using namespace") ||
                   line.StartsWith("int main") || line == "{" || line == "}" ||
                   line.StartsWith("return") || line.StartsWith("cout") || line.StartsWith("cin");
        }

        private void ExecuteDebugStatement(string statement, Dictionary<string, string> vars, int lineIndex, string rawLine, string? forcedDescription)
        {
            statement = CleanDebugLine(statement);
            if (string.IsNullOrWhiteSpace(statement) || IsStructuralCppLine(statement)) return;

            string before = FormatVariables(vars);
            bool changed = false;

            if (TryExecuteDeclaration(statement, vars)) changed = true;
            else if (TryExecuteIncrement(statement, vars)) changed = true;
            else if (TryExecuteAssignment(statement, vars)) changed = true;

            if (changed)
            {
                string after = FormatVariables(vars);
                string desc = forcedDescription ?? "Eseguo la riga e aggiorno le variabili.";
                AddDebugStep(lineIndex, rawLine, desc + "\n\nPrima:\n" + before + "\n\nDopo:\n" + after, vars);
            }
        }

        private bool TryExecuteDeclaration(string statement, Dictionary<string, string> vars)
        {
            var m = Regex.Match(statement, @"^(int|double|float|string|char|bool)\s+(.+)$");
            if (!m.Success) return false;
            string type = m.Groups[1].Value;
            foreach (var piece in SplitTopLevel(m.Groups[2].Value, ','))
            {
                var part = piece.Trim();
                if (string.IsNullOrWhiteSpace(part)) continue;
                var eq = part.Split('=', 2);
                string name = eq[0].Trim();
                if (name.Contains(" ")) name = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
                if (!Regex.IsMatch(name, @"^[A-Za-z_]\w*$")) continue;
                string value = DefaultValueFor(type);
                if (eq.Length == 2) value = EvaluateValue(eq[1].Trim(), vars, type);
                vars[name] = value;
            }
            return true;
        }

        private bool TryExecuteAssignment(string statement, Dictionary<string, string> vars)
        {
            var m = Regex.Match(statement, @"^([A-Za-z_]\w*)\s*(=|\+=|-=|\*=|/=)\s*(.+)$");
            if (!m.Success) return false;
            string name = m.Groups[1].Value;
            string op = m.Groups[2].Value;
            string expr = m.Groups[3].Value;
            string old = vars.TryGetValue(name, out var v) ? v : "0";
            string fullExpr = op switch
            {
                "+=" => old + "+(" + expr + ")",
                "-=" => old + "-(" + expr + ")",
                "*=" => old + "*(" + expr + ")",
                "/=" => old + "/(" + expr + ")",
                _ => expr
            };
            vars[name] = EvaluateValue(fullExpr, vars, GuessType(old));
            return true;
        }

        private bool TryExecuteIncrement(string statement, Dictionary<string, string> vars)
        {
            var m = Regex.Match(statement, @"^(\+\+|--)?\s*([A-Za-z_]\w*)\s*(\+\+|--)?$");
            if (!m.Success) return false;
            string name = m.Groups[2].Value;
            if (!vars.ContainsKey(name)) vars[name] = "0";
            double value = ToDouble(vars[name]);
            string op = !string.IsNullOrWhiteSpace(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[3].Value;
            value += op == "--" ? -1 : 1;
            vars[name] = Math.Abs(value % 1) < 0.000001 ? ((int)value).ToString() : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private string EvaluateValue(string expr, Dictionary<string, string> vars, string preferredType)
        {
            expr = expr.Trim();
            if (expr.StartsWith("\"") && expr.EndsWith("\"")) return expr;
            if (expr.StartsWith("'") && expr.EndsWith("'")) return expr;
            if (expr == "true" || expr == "false") return expr;

            string replaced = Regex.Replace(expr, @"\b[A-Za-z_]\w*\b", m =>
            {
                string key = m.Value;
                if (vars.TryGetValue(key, out var val)) return val.Trim('"', '\'');
                return key;
            });

            try
            {
                var result = new DataTable().Compute(replaced, "");
                if (preferredType == "int" && double.TryParse(result.ToString(), out var d)) return ((int)d).ToString();
                return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? "0";
            }
            catch
            {
                return expr;
            }
        }

        private bool EvaluateCondition(string cond, Dictionary<string, string> vars)
        {
            cond = cond.Trim();
            if (string.IsNullOrWhiteSpace(cond)) return false;
            string replaced = Regex.Replace(cond, @"\b[A-Za-z_]\w*\b", m => vars.TryGetValue(m.Value, out var val) ? val.Trim('"', '\'') : m.Value);
            replaced = replaced.Replace("&&", " AND ").Replace("||", " OR ");
            try
            {
                var result = new DataTable().Compute(replaced, "");
                if (result is bool b) return b;
                return Convert.ToDouble(result) != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseFor(string line, out string init, out string condition, out string increment)
        {
            init = condition = increment = "";
            var m = Regex.Match(line, @"for\s*\((.*?);(.*?);(.*?)\)");
            if (!m.Success) return false;
            init = m.Groups[1].Value.Trim();
            condition = m.Groups[2].Value.Trim();
            increment = m.Groups[3].Value.Trim();
            return true;
        }

        private bool TryParseWhile(string line, out string condition)
        {
            condition = "";
            var m = Regex.Match(line, @"while\s*\((.*?)\)");
            if (!m.Success) return false;
            condition = m.Groups[1].Value.Trim();
            return true;
        }

        private int FindNextClosingBrace(string[] lines, int from)
        {
            int level = 0;
            for (int i = from; i < lines.Length; i++)
            {
                if (lines[i].Contains("{")) level++;
                if (lines[i].Contains("}")) level--;
                if (level <= 0 && i > from) return i;
            }
            return from;
        }

        private List<string> SplitTopLevel(string text, char sep)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool inString = false;
            foreach (char ch in text)
            {
                if (ch == '"') inString = !inString;
                if (ch == sep && !inString)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(ch);
            }
            list.Add(sb.ToString());
            return list;
        }

        private void AddDebugStep(int lineIndex, string lineText, string description, Dictionary<string, string> vars)
        {
            _debugSteps.Add(new DebugStepInfo
            {
                LineIndex = Math.Max(0, lineIndex),
                LineText = lineText,
                Description = description,
                Variables = vars.ToDictionary(k => k.Key, v => v.Value)
            });
        }

        private string FormatVariables(Dictionary<string, string> vars)
        {
            if (vars.Count == 0) return "(nessuna variabile ancora creata)";
            return string.Join(Environment.NewLine, vars.OrderBy(v => v.Key).Select(v => v.Key + " = " + v.Value));
        }

        private string DefaultValueFor(string type) => type switch
        {
            "string" => "\"\"",
            "char" => "' '",
            "bool" => "false",
            _ => "0"
        };

        private string GuessType(string value)
        {
            if (value.StartsWith("\"")) return "string";
            if (value == "true" || value == "false") return "bool";
            if (value.Contains('.')) return "double";
            return "int";
        }

        private double ToDouble(string value)
        {
            double.TryParse(value.Trim('"', '\''), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d);
            return d;
        }

    }

    public class TreeNodeDemo
    {
        public int Value { get; set; }
        public TreeNodeDemo? Left { get; set; }
        public TreeNodeDemo? Right { get; set; }

        public TreeNodeDemo(int value)
        {
            Value = value;
        }
    }

    public class PasswordWindow : Window
    {
        private readonly PasswordBox box = new();
        private readonly TextBox visibleBox = new();
        private readonly CheckBox showPassword = new();

        public string PasswordValue => box.Visibility == Visibility.Visible ? box.Password : visibleBox.Text;

        public PasswordWindow()
        {
            Title = "Accesso - STL Visual";
            Width = 760;
            Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Foreground = Brushes.White;
            WindowStyle = WindowStyle.SingleBorderWindow;

            var root = new Grid();

            var bg = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/login_background.jpeg")),
                Effect = new BlurEffect { Radius = 4 }
            };
            root.Children.Add(bg);

            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(175, 0, 0, 0))
            });

            root.Children.Add(CreateStlCornerAnimation());

            var card = new Border
            {
                Width = 430,
                Padding = new Thickness(26),
                CornerRadius = new CornerRadius(18),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 145, 255)),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromArgb(205, 5, 12, 28)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "STL Visual",
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Visualizzatore STL e Debugger Didattico C++",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 235)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 24)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Password",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            box.Height = 36;
            box.FontSize = 16;
            box.Padding = new Thickness(8);
            box.Background = new SolidColorBrush(Color.FromRgb(10, 18, 35));
            box.Foreground = Brushes.White;
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 145, 255));
            box.BorderThickness = new Thickness(1.2);
            box.KeyDown += Password_KeyDown;
            box.PreviewKeyDown += Password_KeyDown;
            panel.Children.Add(box);

            visibleBox.Height = 36;
            visibleBox.FontSize = 16;
            visibleBox.Padding = new Thickness(8);
            visibleBox.Background = new SolidColorBrush(Color.FromRgb(10, 18, 35));
            visibleBox.Foreground = Brushes.White;
            visibleBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 145, 255));
            visibleBox.BorderThickness = new Thickness(1.2);
            visibleBox.Visibility = Visibility.Collapsed;
            visibleBox.KeyDown += Password_KeyDown;
            visibleBox.PreviewKeyDown += Password_KeyDown;
            panel.Children.Add(visibleBox);

            showPassword.Content = "Mostra password";
            showPassword.Foreground = Brushes.White;
            showPassword.Margin = new Thickness(0, 10, 0, 18);
            showPassword.Checked += (_, _) => { visibleBox.Text = box.Password; box.Visibility = Visibility.Collapsed; visibleBox.Visibility = Visibility.Visible; visibleBox.Focus(); visibleBox.CaretIndex = visibleBox.Text.Length; };
            showPassword.Unchecked += (_, _) => { box.Password = visibleBox.Text; visibleBox.Visibility = Visibility.Collapsed; box.Visibility = Visibility.Visible; box.Focus(); };
            panel.Children.Add(showPassword);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var ok = new Button
            {
                Content = "ACCEDI",
                Width = 130,
                Height = 38,
                Margin = new Thickness(6),
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0, 132, 255)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 170, 255))
            };
            var cancel = new Button
            {
                Content = "Esci",
                Width = 100,
                Height = 38,
                Margin = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 52)),
                Foreground = Brushes.White
            };
            ok.IsDefault = true;
            cancel.IsCancel = true;
            ok.Click += (_, _) => { DialogResult = true; Close(); };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            panel.Children.Add(row);

            card.Child = panel;
            root.Children.Add(card);

            root.Children.Add(new TextBlock
            {
                Text = "STL Visual • Versione 51 • © Alessandro Barazzuol",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 14, 10)
            });

            Content = root;
            Loaded += (_, _) => box.Focus();
            KeyDown += Password_KeyDown;
            PreviewKeyDown += Password_KeyDown;
        }


        private UIElement CreateStlCornerAnimation()
        {
            var canvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
                Opacity = 0.96
            };

            string[] names = { "std::list", "std::vector", "std::set", "std::stack", "std::queue", "std::map" };
            double[,] positions =
            {
                { 26, 26 }, { 548, 26 }, { 34, 344 },
                { 552, 344 }, { 86, 86 }, { 506, 286 }
            };

            for (int i = 0; i < names.Length; i++)
            {
                var badge = CreateContainerBadge(names[i], i);
                Canvas.SetLeft(badge, positions[i, 0]);
                Canvas.SetTop(badge, positions[i, 1]);
                canvas.Children.Add(badge);
            }

            for (int i = 0; i < 18; i++)
            {
                var dot = new Ellipse
                {
                    Width = 4 + (i % 3),
                    Height = 4 + (i % 3),
                    Fill = new SolidColorBrush(Color.FromArgb(130, 56, 189, 248)),
                    Effect = new DropShadowEffect { Color = Color.FromRgb(56, 189, 248), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7 }
                };
                Canvas.SetLeft(dot, 120 + (i * 31) % 520);
                Canvas.SetTop(dot, 70 + (i * 47) % 280);
                canvas.Children.Add(dot);

                var pulse = new DoubleAnimation
                {
                    From = 0.25,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(1.4 + i * 0.05),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 90)
                };
                dot.Loaded += (_, _) => dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            }

            return canvas;
        }

        private static Border CreateContainerBadge(string text, int index)
        {
            var badge = new Border
            {
                Width = text.Length > 8 ? 126 : 104,
                Height = 38,
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(Color.FromArgb(210, 8, 18, 42), Color.FromArgb(190, 25, 50, 95), 0),
                BorderBrush = new LinearGradientBrush(Color.FromRgb(34, 211, 238), Color.FromRgb(168, 85, 247), 0),
                BorderThickness = new Thickness(1.2),
                Effect = new DropShadowEffect { Color = Color.FromRgb(34, 211, 238), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.32 },
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                RenderTransform = new ScaleTransform(1, 1),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            badge.Loaded += (_, _) =>
            {
                var pulseX = new DoubleAnimation
                {
                    From = 0.92,
                    To = 1.06,
                    Duration = TimeSpan.FromSeconds(1.9),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 180)
                };
                var pulseY = new DoubleAnimation
                {
                    From = 0.92,
                    To = 1.06,
                    Duration = TimeSpan.FromSeconds(1.9),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 180)
                };
                var transform = (ScaleTransform)badge.RenderTransform;
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseX);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseY);
            };
            return badge;
        }

        private void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
            }
        }
    }
}