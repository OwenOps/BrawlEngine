using BrawlEngine.Host.Infrastructure.Storage;
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
            .SetContextMenuEnabled(false)
#if DEBUG
            .SetDevToolsEnabled(true)
#else
            .SetDevToolsEnabled(false)
#endif
            .RegisterCustomSchemeHandler("app", (object sender, string scheme, string url, out string contentType) =>
                CatalogLibrary.OpenThumb(url, out contentType))
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                var window = (PhotinoWindow)sender!;
                _ = Task.Run(() =>
                {
                    try
                    {
                        IpcRouter.Handle(window, message);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            IpcRouter.SendFailure(window, message, "Host failed: " + ex.Message);
                        }
                        catch
                        {
                            // Last resort: never take down the window.
                        }
                    }
                });
            })
            .Load(uiPath);

        window.WaitForClose();
    }
}
