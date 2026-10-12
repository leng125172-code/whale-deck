using System.Globalization;

namespace WhaleDeck.Worker;

public static class WorkerHeartbeat
{
    private const string DefaultPath = "/tmp/whaledeck-worker.heartbeat";

    private static string PathName =>
        Environment.GetEnvironmentVariable("WHALEDECK_WORKER_HEARTBEAT_PATH") ?? DefaultPath;

    public static void Report() => Report(PathName);

    public static void Report(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        File.Move(temporary, path, true);
    }

    public static bool IsFresh(TimeSpan maximumAge) => IsFresh(PathName, maximumAge, DateTimeOffset.UtcNow);

    public static bool IsFresh(string path, TimeSpan maximumAge, DateTimeOffset now)
    {
        try
        {
            var value = File.ReadAllText(path).Trim();
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
            var observedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            var age = now - observedAt;
            return age >= TimeSpan.Zero && age <= maximumAge;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
