using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using NoradrenalineUltra.Models;
using NoradrenalineUltra.Services;

namespace NoradrenalineUltra;

public partial class MainWindow : Window
{
    private readonly IDriverInventoryService _inventoryService = new DriverInventoryService();
    private readonly IGameLibraryService _gameLibraryService = new GameLibraryService();
    private readonly GameLibraryStore _gameLibraryStore = new();
    private readonly NoraX1ProbeService _noraX1ProbeService = new();
    private readonly ObservableCollection<DriverDevice> _devices = [];
    private readonly ObservableCollection<GameEntry> _games = [];
    private readonly ObservableCollection<GraphicsAdapter> _graphicsAdapters = [];
    private readonly List<GameEntry> _manualGames = [];
    private ICollectionView? _deviceView;
    private bool _isBusy;
    private bool _isScanningGames;
    private Process? _launchedGameProcess;

    public MainWindow()
    {
        InitializeComponent();
        _deviceView = CollectionViewSource.GetDefaultView(_devices);
        _deviceView.Filter = MatchesSearch;
        DeviceGrid.ItemsSource = _deviceView;
        GameGrid.ItemsSource = _games;
        GraphicsAdapterGrid.ItemsSource = _graphicsAdapters;
        ExportButton.IsEnabled = false;
        ManufacturerButton.IsEnabled = false;
        GamePriorityBox.SelectionChanged += GamePriorityBox_SelectionChanged;
        GameProfileSummaryText.Text = "Normal priority";
        ShowGames_Click(this, new RoutedEventArgs());
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanGameLibraryAsync();
    }

    private void ShowGames_Click(object sender, RoutedEventArgs e)
    {
        GamePage.Visibility = Visibility.Visible;
        DriverPage.Visibility = Visibility.Collapsed;
        GraphicsPage.Visibility = Visibility.Collapsed;
        GameNavButton.Style = (Style)FindResource("ActionButtonStyle");
        DriverNavButton.Style = (Style)FindResource("QuietButtonStyle");
        GraphicsNavButton.Style = (Style)FindResource("QuietButtonStyle");
    }

    private void ShowDrivers_Click(object sender, RoutedEventArgs e)
    {
        GamePage.Visibility = Visibility.Collapsed;
        DriverPage.Visibility = Visibility.Visible;
        GraphicsPage.Visibility = Visibility.Collapsed;
        GameNavButton.Style = (Style)FindResource("QuietButtonStyle");
        DriverNavButton.Style = (Style)FindResource("ActionButtonStyle");
        GraphicsNavButton.Style = (Style)FindResource("QuietButtonStyle");
    }

    private void ShowGraphics_Click(object sender, RoutedEventArgs e)
    {
        GamePage.Visibility = Visibility.Collapsed;
        DriverPage.Visibility = Visibility.Collapsed;
        GraphicsPage.Visibility = Visibility.Visible;
        GameNavButton.Style = (Style)FindResource("QuietButtonStyle");
        DriverNavButton.Style = (Style)FindResource("QuietButtonStyle");
        GraphicsNavButton.Style = (Style)FindResource("ActionButtonStyle");
    }

    private async void ScanDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ConnectionStatus.Text = "Scanning Windows devices…";
        ConnectionIndicator.Fill = System.Windows.Media.Brushes.Goldenrod;
        ActivityText.Text = "Querying Windows Plug and Play inventory.";

        try
        {
            var devices = await _inventoryService.ScanAsync();
            ApplyScanResults(devices);
            ActivityText.Text = $"Windows returned {devices.Count:N0} signed-driver device records. Inventory is read-only.";
        }
        catch (Exception exception)
        {
            ConnectionStatus.Text = "Scan failed";
            ConnectionIndicator.Fill = System.Windows.Media.Brushes.IndianRed;
            ActivityText.Text = "Windows device inventory could not be read.";
            MessageBox.Show(
                this,
                $"The Windows driver inventory could not be read.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Driver scan failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a signed Windows driver INF file",
            Filter = "Driver setup information (*.inf)|*.inf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true);
        ActivityText.Text = "Verifying the selected INF package with Windows SetupAPI.";

        try
        {
            var verification = await Task.Run(() => DriverPackageInstaller.VerifyInfSignature(dialog.FileName));
            if (!verification.IsTrusted)
            {
                ActivityText.Text = "Installation blocked: Windows did not verify the package signature.";
                MessageBox.Show(
                    this,
                    $"Windows could not verify this INF package. The installation was blocked.{Environment.NewLine}{Environment.NewLine}{verification.Error}",
                    "Unverified driver package",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                $"Windows verified the package signature.{Environment.NewLine}{Environment.NewLine}" +
                $"Signer: {verification.Signer}{Environment.NewLine}" +
                $"Catalog: {verification.CatalogFile}{Environment.NewLine}{Environment.NewLine}" +
                "A valid signature does not guarantee that the package is compatible with this device. " +
                "Confirm the manufacturer and hardware ID before continuing." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Continue to the Windows administrator approval prompt and ask PnPUtil to install this package?",
                "Confirm driver installation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                ActivityText.Text = "Driver installation cancelled by the user.";
                return;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("The application executable path could not be determined.");
            }

            var helper = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--install-driver \"{dialog.FileName}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            using var elevatedProcess = Process.Start(helper)
                ?? throw new InvalidOperationException("The elevated installation helper could not be started.");
            ActivityText.Text = "Waiting for the Windows driver installation result.";
            await elevatedProcess.WaitForExitAsync();

            if (elevatedProcess.ExitCode == 0)
            {
                ActivityText.Text = "PnPUtil completed. Run a fresh scan to verify the current device state.";
                await ScanDrivers_ClickAsync();
            }
            else
            {
                ActivityText.Text = $"Driver installation failed with exit code {elevatedProcess.ExitCode}.";
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            ActivityText.Text = "Administrator approval was cancelled. No installation was started.";
        }
        catch (Exception exception)
        {
            ActivityText.Text = "Driver installation could not be completed.";
            MessageBox.Show(
                this,
                $"The driver package was not installed.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Driver installation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanDrivers_ClickAsync()
    {
        try
        {
            ApplyScanResults(await _inventoryService.ScanAsync());
        }
        catch (Exception exception)
        {
            ConnectionStatus.Text = "Scan failed";
            ConnectionIndicator.Fill = System.Windows.Media.Brushes.IndianRed;
            ActivityText.Text = $"Installation finished, but the follow-up scan failed: {exception.Message}";
        }
    }

    private void ApplyScanResults(IReadOnlyList<DriverDevice> devices)
    {
        _devices.Clear();
        foreach (var device in devices)
        {
            _devices.Add(device);
        }

        _deviceView?.Refresh();
        DeviceCountText.Text = devices.Count.ToString("N0");
        ProblemCountText.Text = devices.Count(device => device.ProblemCode is > 0).ToString("N0");
        UnsignedCountText.Text = devices.Count(device => device.IsSigned is false).ToString("N0");
        ExportButton.IsEnabled = devices.Count > 0;
        ConnectionStatus.Text = $"Scanned {DateTime.Now:T}";
        ConnectionIndicator.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        await ScanGameLibraryAsync();
    }

    private async Task ScanGameLibraryAsync()
    {
        if (_isScanningGames)
        {
            return;
        }

        _isScanningGames = true;
        GameStatusText.Text = "Scanning local libraries…";
        GameStatusIndicator.Fill = System.Windows.Media.Brushes.Goldenrod;
        GameActivityText.Text = "Reading installed-game manifests. No account data is accessed or uploaded.";

        try
        {
            var scan = await _gameLibraryService.ScanInstalledGamesAsync();
            var savedGames = await _gameLibraryStore.LoadAsync();
            _manualGames.Clear();
            _manualGames.AddRange(savedGames.Where(game =>
                string.Equals(game.Platform, "Manual", StringComparison.Ordinal) &&
                game.ExecutablePath is not null &&
                File.Exists(game.ExecutablePath)));

            var combinedGames = scan.Games
                .Concat(_manualGames)
                .GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            _games.Clear();
            foreach (var game in combinedGames)
            {
                _games.Add(game);
            }

            GameCountText.Text = _games.Count.ToString("N0");
            GameStatusText.Text = $"Scanned {DateTime.Now:T}";
            GameStatusIndicator.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
            UpdateGameSelection();

            if (scan.Warnings.Count > 0)
            {
                GameActivityText.Text =
                    $"{_games.Count:N0} games found. {scan.Warnings.Count:N0} library item(s) could not be read: {scan.Warnings[0]}";
            }
            else if (_games.Count == 0)
            {
                GameActivityText.Text =
                    "No installed games were found in the standard Steam/Epic libraries. Add a game executable to your library.";
            }
            else
            {
                GameActivityText.Text = $"Found {_games.Count:N0} game(s). Select one to open its library folder or launch it.";
            }
        }
        catch (Exception exception)
        {
            GameStatusText.Text = "Library scan failed";
            GameStatusIndicator.Fill = System.Windows.Media.Brushes.IndianRed;
            GameActivityText.Text = "The game library could not be read.";
            MessageBox.Show(
                this,
                $"The game library could not be scanned.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Game scan failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isScanningGames = false;
        }
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a game executable",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var executablePath = Path.GetFullPath(dialog.FileName);
            if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Select a Windows .exe game executable.", nameof(dialog));
            }

            var game = new GameEntry(
                $"manual:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executablePath.ToUpperInvariant())))}",
                Path.GetFileNameWithoutExtension(executablePath),
                "Manual",
                Path.GetDirectoryName(executablePath)
                    ?? throw new InvalidOperationException("The selected game's folder could not be determined."),
                executablePath,
                executablePath);
            if (_games.Any(existing => string.Equals(existing.Id, game.Id, StringComparison.OrdinalIgnoreCase)))
            {
                GameActivityText.Text = "That executable is already in your game library.";
                return;
            }

            _manualGames.Add(game);
            try
            {
                await _gameLibraryStore.SaveAsync(_manualGames);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _manualGames.Remove(game);
                throw;
            }

            _games.Add(game);
            GameCountText.Text = _games.Count.ToString("N0");
            GameGrid.SelectedItem = game;
            GameGrid.ScrollIntoView(game);
            GameActivityText.Text = $"Added {game.Name} to this Windows user’s local game library.";
            UpdateGameSelection();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The game could not be added.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Add game failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not GameEntry { Platform: "Manual" } game)
        {
            return;
        }

        _manualGames.RemoveAll(entry => string.Equals(entry.Id, game.Id, StringComparison.OrdinalIgnoreCase));
        _games.Remove(game);
        GameCountText.Text = _games.Count.ToString("N0");
        try
        {
            await _gameLibraryStore.SaveAsync(_manualGames);
            GameActivityText.Text = $"Removed {game.Name} from the local library. The game files were not deleted.";
        }
        catch (Exception exception)
        {
            _manualGames.Add(game);
            _games.Add(game);
            GameCountText.Text = _games.Count.ToString("N0");
            MessageBox.Show(
                this,
                $"The local library could not be updated.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Remove game failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        UpdateGameSelection();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not GameEntry game)
        {
            return;
        }

        if (!Directory.Exists(game.InstallPath))
        {
            GameActivityText.Text = "The recorded game folder no longer exists. Rescan the game library.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(game.InstallPath) { UseShellExecute = true });
            GameActivityText.Text = $"Opened the {game.Name} installation folder.";
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(
                this,
                $"The game folder could not be opened.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Open folder failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not GameEntry game)
        {
            return;
        }

        var aboveNormal = GamePriorityBox.SelectedItem is System.Windows.Controls.ComboBoxItem
        {
            Tag: "AboveNormal"
        };

        try
        {
            var result = GameLauncher.Launch(game, aboveNormal);
            if (result.IsPlatformLauncher)
            {
                result.Process.Dispose();
                GameActivityText.Text = $"Opened {game.Name} in {game.Platform}. Platform-launch games use normal scheduling priority.";
                GameStatusText.Text = $"Launch sent to {game.Platform}";
                return;
            }

            TrackLaunchedGame(result.Process, game.Name);
            if (aboveNormal && !result.PriorityApplied)
            {
                GameActivityText.Text =
                    $"{game.Name} launched, but Windows did not apply above-normal priority. The game is still running at its default priority.";
            }
            else
            {
                GameActivityText.Text = result.PriorityApplied
                    ? $"Launched {game.Name} with above-normal priority for this process only."
                    : $"Launched {game.Name} at its normal process priority.";
            }
        }
        catch (Exception exception)
        {
            GameActivityText.Text = "The selected game could not be launched.";
            MessageBox.Show(
                this,
                $"The selected game could not be launched.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Game launch failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TrackLaunchedGame(Process process, string gameName)
    {
        if (_launchedGameProcess is { } previousProcess)
        {
            previousProcess.Dispose();
        }

        _launchedGameProcess = process;
        GameStatusText.Text = $"Running: {gameName}";
        GameStatusIndicator.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_launchedGameProcess, process))
            {
                _launchedGameProcess = null;
                GameStatusText.Text = "Game process ended";
                GameStatusIndicator.Fill = System.Windows.Media.Brushes.DimGray;
                GameActivityText.Text = $"{gameName} has exited.";
                process.Dispose();
            }
        });

        if (process.HasExited)
        {
            GameStatusText.Text = "Game process ended";
            GameStatusIndicator.Fill = System.Windows.Media.Brushes.DimGray;
        }
    }

    private void GameGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateGameSelection();
    }

    private void GamePriorityBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var aboveNormal = GamePriorityBox.SelectedItem is System.Windows.Controls.ComboBoxItem
        {
            Tag: "AboveNormal"
        };
        GameProfileSummaryText.Text = aboveNormal ? "Above normal · game only" : "Normal priority";
        UpdateGameSelection();
    }

    private void UpdateGameSelection()
    {
        var selectedGame = GameGrid.SelectedItem as GameEntry;
        OpenGameFolderButton.IsEnabled = selectedGame is not null;
        RemoveGameButton.IsEnabled = selectedGame?.Platform == "Manual";
        var aboveNormal = GamePriorityBox.SelectedItem is System.Windows.Controls.ComboBoxItem
        {
            Tag: "AboveNormal"
        };
        LaunchGameButton.IsEnabled = selectedGame is not null &&
            (!aboveNormal || selectedGame.ExecutablePath is not null);
    }

    private async void ScanGraphics_Click(object sender, RoutedEventArgs e)
    {
        if (!GraphicsScanButton.IsEnabled)
        {
            return;
        }

        GraphicsScanButton.IsEnabled = false;
        GraphicsStatusText.Text = "Querying DXGI…";
        GraphicsStatusIndicator.Fill = System.Windows.Media.Brushes.Goldenrod;
        GraphicsActivityText.Text = "Reading adapter capabilities with the NORA X1 user-mode probe.";

        try
        {
            var adapters = await _noraX1ProbeService.ScanAsync();
            _graphicsAdapters.Clear();
            foreach (var adapter in adapters)
            {
                _graphicsAdapters.Add(adapter);
            }

            GraphicsStatusText.Text = $"Scanned {DateTime.Now:T}";
            GraphicsStatusIndicator.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
            GraphicsActivityText.Text = adapters.Count == 0
                ? "DXGI returned no display adapters."
                : $"DXGI reported {adapters.Count:N0} adapter(s). No driver or graphics settings were changed.";
        }
        catch (Exception exception)
        {
            GraphicsStatusText.Text = "Probe unavailable";
            GraphicsStatusIndicator.Fill = System.Windows.Media.Brushes.IndianRed;
            GraphicsActivityText.Text = "NORA X1 graphics capability scan failed.";
            MessageBox.Show(
                this,
                $"NORA X1 could not query graphics capabilities.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Graphics scan failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GraphicsScanButton.IsEnabled = true;
        }
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_devices.Count == 0)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "The report includes hardware IDs and driver details. Save and share it only with people you trust.",
            "Export privacy notice",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export driver inventory",
            Filter = "JSON report (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"nora-driver-report-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await DriverReportExporter.ExportJsonAsync(dialog.FileName, _devices.ToArray());
            ActivityText.Text = $"Driver report saved to {dialog.FileName}";
        }
        catch (Exception exception)
        {
            ActivityText.Text = "The driver report could not be saved.";
            MessageBox.Show(
                this,
                $"The report could not be saved.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Report export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenManufacturer_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DriverDevice device)
        {
            return;
        }

        var address = GetManufacturerAddress(device.Manufacturer);
        if (address is null)
        {
            MessageBox.Show(
                this,
                $"No verified manufacturer support link is configured for “{device.Manufacturer}”.",
                "Manufacturer link unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            ActivityText.Text = $"Opened the official {device.Manufacturer} support page.";
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(
                this,
                $"The manufacturer page could not be opened.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Could not open website",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _deviceView?.Refresh();
    }

    private void DeviceGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ManufacturerButton.IsEnabled =
            DeviceGrid.SelectedItem is DriverDevice device &&
            GetManufacturerAddress(device.Manufacturer) is not null;
    }

    private bool MatchesSearch(object item)
    {
        if (item is not DriverDevice device || string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            return true;
        }

        var query = SearchBox.Text.Trim();
        return device.DeviceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               device.Manufacturer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               device.DeviceId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               device.DriverVersion.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ScanButton.IsEnabled = !isBusy;
        InstallButton.IsEnabled = !isBusy;
        ExportButton.IsEnabled = !isBusy && _devices.Count > 0;
        ManufacturerButton.IsEnabled = !isBusy &&
            DeviceGrid.SelectedItem is DriverDevice device &&
            GetManufacturerAddress(device.Manufacturer) is not null;
    }

    private static string? GetManufacturerAddress(string manufacturer)
    {
        var value = manufacturer.Trim();
        if (value.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.nvidia.com/Download/index.aspx";
        }

        if (value.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.amd.com/en/support/download/drivers.html";
        }

        if (value.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.intel.com/content/www/us/en/download-center/home.html";
        }

        if (value.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.realtek.com/en/downloads";
        }

        if (value.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return "https://support.lenovo.com/";
        }

        if (value.Contains("Dell", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.dell.com/support/home/";
        }

        if (value.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.asus.com/support/download-center/";
        }

        if (value.Contains("Acer", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.acer.com/support";
        }

        if (value.Contains("HP", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase))
        {
            return "https://support.hp.com/drivers";
        }

        return null;
    }
}