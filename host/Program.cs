using BrawlEngine.Host.Presentation.Ipc;
using Photino.NET;

namespace BrawlEngine.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var uiPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        if (!File.Exists(uiPath))
        {
            throw new FileNotFoundException(
                "UI not found. From the repo root run: npx ng build (in ui/)",
                uiPath);
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

        var window = new PhotinoWindow()
            .SetTitle("BrawlEngine")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 800)
            .SetMinSize(800, 560)
            .Center()
            .SetResizable(true)
            .SetIconFile(iconPath)
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                var window = (PhotinoWindow)sender!;
                _ = Task.Run(() =>
                {
                    try
                    {
                        IpcRouter.Handle(window, message);
                    }
                    catch
                    {
                        // One failed IPC must not close the window.
                    }
                });
            })
            .Load(uiPath);

        window.WaitForClose();
    }
}
