namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3FilePicker
{
    public static string? PickMp3()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickCore();
        }

        string? result = null;
        var thread = new Thread(() => result = PickCore());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string? PickCore()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an MP3 file",
            Filter = "MP3 (*.mp3)|*.mp3",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
