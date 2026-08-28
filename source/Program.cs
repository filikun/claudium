namespace Claudium;

/// <summary>
/// Hand-written entry point (replaces WinUI's source-generated Main —
/// see DISABLE_XAML_GENERATED_MAIN in Claudium.csproj). Velopack requires
/// VelopackApp.Build().Run() to run as the very first line, before
/// Application.Start(), so it can intercept install/update/uninstall
/// hooks on a freshly installed or updated app.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
