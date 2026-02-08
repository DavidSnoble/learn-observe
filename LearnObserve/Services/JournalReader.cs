namespace LearnObserve.Services;

public sealed record LogLine(
    DateTimeOffset? Timestamp,
    string Unit,
    string Message,
    string? Priority);

public sealed class JournalReader
{
    private readonly ILogger<JournalReader> _logger;

    public JournalReader(ILogger<JournalReader> logger)
    {
        _logger = logger;
    }

    public async Task<List<LogLine>> TailAsync(
        string unit,
        int lines,
        string minPriority,
        CancellationToken ct)
    {
        // journalctl --user -u UNIT -n LINES -o short-iso --no-pager -p warning
        lines = Math.Clamp(lines, 1, 2000);

        var args = new List<string>
        {
            "--user",
            "-u",
            unit,
            "-n",
            lines.ToString(),
            "-o",
            "short-iso",
            "--no-pager",
            "-p",
            minPriority
        };

        var (code, stdout, stderr) = await Proc.RunAsync("journalctl", args, ct);
        if (code != 0)
        {
            _logger.LogWarning("journalctl failed for {Unit} (code {Code}): {Err}", unit, code, stderr);
            return new List<LogLine>();
        }

        var result = new List<LogLine>();
        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // short-iso: "2026-02-08T03:54:36.272567Z host app[pid]: msg"
            var msg = raw.TrimEnd();
            result.Add(new LogLine(null, unit, msg, minPriority));
        }
        return result;
    }
}
