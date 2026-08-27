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

        var window = new PhotinoWindow()
            .SetTitle("BrawlEngine")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 800)
            .SetMinSize(800, 560)
            .Center()
            .SetResizable(true)
            .RegisterWebMessageReceivedHandler((sender, message) =>
            {
                IpcRouter.Handle((PhotinoWindow)sender!, message);
            })
            .Load(uiPath);

        window.WaitForClose();
    }
}
