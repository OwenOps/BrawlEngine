namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3FilePicker
{
    public static string? PickAudio(string targetFileName)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickCore(targetFileName);
        }

        string? result = null;
        var thread = new Thread(() => result = PickCore(targetFileName));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string? PickCore(string targetFileName)
    {
        var wem = Path.GetExtension(targetFileName).Equals(".wem", StringComparison.OrdinalIgnoreCase);
        using var dialog = new OpenFileDialog
        {
            Title = wem ? "Choose audio to convert, or a .wem" : "Choose an MP3 file",
            Filter = wem
                ? "Audio|*.mp3;*.wav;*.ogg;*.flac;*.wem|Wwise (*.wem)|*.wem|MP3 (*.mp3)|*.mp3"
                : "MP3 (*.mp3)|*.mp3",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
