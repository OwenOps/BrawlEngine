using BrawlEngine.Host.Infrastructure.Processes;
using Photino.NET;

namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// Apply / Reset wait until Brawlhalla exits. Do not write game files while it is open.
/// </summary>
public static class PendingGameWrites
{
    public const string QueuedReason = "Queued. Will apply when Brawlhalla closes.";

    private static readonly object Gate = new();
    private static readonly Queue<Action> Jobs = new();
    private static PhotinoWindow? Window;
    private static bool Watching;

    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return Jobs.Count;
            }
        }
    }

    public static void Enqueue(PhotinoWindow window, Action run)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(run);

        lock (Gate)
        {
            Window = window;
            Jobs.Enqueue(run);
            if (Watching)
            {
                return;
            }

            Watching = true;
            _ = Task.Run(Watch);
        }
    }

    private static void Watch()
    {
        try
        {
            while (true)
            {
                lock (Gate)
                {
                    if (Jobs.Count == 0)
                    {
                        return;
                    }
                }

                while (BrawlhallaProcess.Check().Running)
                {
                    Thread.Sleep(750);
                    lock (Gate)
                    {
                        if (Jobs.Count == 0)
                        {
                            return;
                        }
                    }
                }

                Action run;
                lock (Gate)
                {
                    if (Jobs.Count == 0)
                    {
                        return;
                    }

                    if (BrawlhallaProcess.Check().Running)
                    {
                        continue;
                    }

                    run = Jobs.Dequeue();
                }

                try
                {
                    run();
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            lock (Gate)
            {
                if (Jobs.Count > 0)
                {
                    _ = Task.Run(Watch);
                }
                else
                {
                    Watching = false;
                }
            }
        }
    }
}
