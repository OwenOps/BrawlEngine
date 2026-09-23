using System.Drawing;
using System.Windows.Forms;
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
        using var boot = ShowBoot(iconPath);

        var scale = DpiScale();
        var size = DefaultWindowSize(scale);
        var window = new PhotinoWindow()
            .SetTitle("BrawlEngine")
            .SetUseOsDefaultSize(false)
            .SetSize(size.Width, size.Height)
            .SetMinSize(
                Math.Min((int)Math.Round(800 * scale), size.Width),
                Math.Min((int)Math.Round(560 * scale), size.Height))
            .Center()
            .SetResizable(true)
            .SetIconFile(iconPath)
            // Stable id so Windows does not keep a first-run blank taskbar icon keyed on the title.
            .SetNotificationRegistrationId("3f8a2c61-9d4e-4b17-8a90-7c1e5d2f4a08")
            .SetNotificationsEnabled(false)
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

        boot.Hide();
        window.WaitForClose();
    }

    // Photino SetSize is physical pixels. WinForms WorkingArea is logical on a DPI-aware process.
    private static float DpiScale()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        var scale = graphics.DpiX / 96f;
        return scale > 0.25f ? scale : 1f;
    }

    private static Size DefaultWindowSize(float scale)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var maxW = Math.Max(1, (int)Math.Round(area.Width * scale));
        var maxH = Math.Max(1, (int)Math.Round(area.Height * scale));
        var minW = Math.Min((int)Math.Round(800 * scale), maxW);
        var minH = Math.Min((int)Math.Round(560 * scale), maxH);
        return new Size(
            Math.Clamp((int)Math.Round(area.Width * scale * 0.6), minW, maxW),
            Math.Clamp((int)Math.Round(area.Height * scale * 0.6), minH, maxH));
    }

    // Native bundle extract (single-file) happens before Main. This covers Photino / WebView startup.
    private static Form ShowBoot(string iconPath)
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(360, 180),
            BackColor = Color.FromArgb(13, 15, 19),
            ShowInTaskbar = false,
            TopMost = true,
            Text = "BrawlEngine",
        };
        if (File.Exists(iconPath))
        {
            form.Icon = new Icon(iconPath);
        }

        form.Controls.Add(new Label
        {
            Text = "Starting…",
            ForeColor = Color.FromArgb(236, 238, 241),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
        });
        form.Show();
        Application.DoEvents();
        return form;
    }
}
