using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

// Zip-root stub: Explorer shows this file's icon on any PC.
// A .lnk cannot (it stores an absolute icon path from the build machine).
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var exe = Path.Combine(dir, "app", "BrawlEngine.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show(
                "Keep this file next to the app folder, then double-click it.\r\n\r\nExtract the whole zip, do not move this file alone.",
                "BrawlEngine");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = true
        });
    }
}
