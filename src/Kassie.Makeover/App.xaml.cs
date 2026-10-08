using System.IO;
using System.Threading;
using System.Windows;
using Kassie.Makeover.Infrastructure;

namespace Kassie.Makeover;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.Ensure();
        AppLog.Write($"Kassie Makeover {GetType().Assembly.GetName().Version} starting.");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write($"UNHANDLED UI EXCEPTION: {args.Exception}");
            try
            {
                MessageBox.Show(
                    $"Kassie hit an error but has been kept open.\n\n{args.Exception.Message}\n\nDetails were written to D:\\Kassie\\Makeover\\logs\\app.log.",
                    "Kassie Makeover",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write($"FATAL PROCESS EXCEPTION: {args.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write($"UNOBSERVED TASK EXCEPTION: {args.Exception}");
            args.SetObserved();
        };

        _instanceMutex = new Mutex(true, @"Local\KassieMakeover-58B1C6D5-9BB4-4B4E-BD39-5E72E76CC84C", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Kassie Makeover is already running.", "Kassie Makeover", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("Kassie Makeover exiting.");
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
