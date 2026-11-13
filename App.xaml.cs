using System.Windows;

namespace TempWidget;

public partial class App : Application
{
    static Mutex? single; // held for the life of the process

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // TempWidget.exe --captura <folder>: draw every style to PNG sheets and leave, without opening a window.
        if (e.Args is ["--captura", var folder])
        {
            new MainWindow().RenderSheets(folder);
            Shutdown();
            return;
        }

        // One widget at a time. Opening it again brings the running one back, even from the tray.
        EventWaitHandle? showMe = null;
        bool mine;
        try
        {
            showMe = new EventWaitHandle(false, EventResetMode.AutoReset, "TempWidget.Mostrar");
            single = new Mutex(false, "TempWidget.UnaSolaInstancia");
            // The wait covers an instance that is closing to relaunch itself as administrator.
            try { mine = single.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { mine = true; } // the previous owner ended without releasing it
        }
        catch (UnauthorizedAccessException) { mine = false; } // created by an instance running as administrator
        if (!mine)
        {
            showMe?.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        new Thread(() => { while (showMe!.WaitOne()) window.Dispatcher.BeginInvoke(() => window.SetShown(true)); }) { IsBackground = true }.Start();
        window.Start();
    }
}
