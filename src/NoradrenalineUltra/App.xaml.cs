using System.Windows;
using NoradrenalineUltra.Services;

namespace NoradrenalineUltra;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs();
        if (args.Length == 3 && args[1] == "--install-driver")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var result = await DriverPackageInstaller.InstallVerifiedDriverAsync(args[2]);
                MessageBox.Show(
                    $"Windows Driver Store operation completed successfully.\n\n{result}",
                    "NORA Driver Center",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"The driver package was not installed.\n\n{exception.Message}",
                    "NORA Driver Center",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }

            return;
        }

        new MainWindow().Show();
    }
}
