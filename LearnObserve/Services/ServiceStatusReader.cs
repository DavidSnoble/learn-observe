using System.Text;

namespace LearnObserve.Services;

public sealed record UnitStatus(
    string Unit,
    string ActiveState,
    string SubState,
    string? Result,
    int? NRestarts,
    string? ExecMainStatus,
    string? MainPid,
    DateTimeOffset? ActiveEnterTimestamp);

public sealed class ServiceStatusReader
{
    private readonly ILogger<ServiceStatusReader> _logger;

    public ServiceStatusReader(ILogger<ServiceStatusReader> logger)
    {
        _logger = logger;
    }

    public async Task<UnitStatus?> GetStatusAsync(string unit, CancellationToken ct)
    {
        // systemctl --user show -p ActiveState -p SubState ... unit
        var props = new[]
        {
            "Id",
            "ActiveState",
            "SubState",
            "Result",
            "NRestarts",
            "ExecMainStatus",
            "MainPID",
            "ActiveEnterTimestamp"
        };

        var args = new List<string>
        {
            "--user",
            "show",
            "--no-pager",
        };
        foreach (var p in props)
        {
            args.Add("-p");
            args.Add(p);
        }
        args.Add(unit);

        var (code, stdout, stderr) = await Proc.RunAsync("systemctl", args, ct);
        if (code != 0)
        {
            _logger.LogWarning("systemctl show failed for {Unit} (code {Code}): {Err}", unit, code, stderr);
            return null;
        }

        var dict = ParseKeyValues(stdout);
        dict.TryGetValue("ActiveState", out var active);
        dict.TryGetValue("SubState", out var sub);
        dict.TryGetValue("Result", out var result);
        dict.TryGetValue("NRestarts", out var nrestartsRaw);
        dict.TryGetValue("ExecMainStatus", out var execStatus);
        dict.TryGetValue("MainPID", out var mainPid);
        dict.TryGetValue("ActiveEnterTimestamp", out var activeEnter);

        int? nRestarts = int.TryParse(nrestartsRaw, out var nr) ? nr : null;
        DateTimeOffset? activeEnterTs = TryParseSystemdTimestamp(activeEnter);

        return new UnitStatus(
            unit,
            active ?? "unknown",
            sub ?? "unknown",
            string.IsNullOrWhiteSpace(result) ? null : result,
            nRestarts,
            string.IsNullOrWhiteSpace(execStatus) ? null : execStatus,
            string.IsNullOrWhiteSpace(mainPid) ? null : mainPid,
            activeEnterTs);
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            dict[key] = val;
        }
        return dict;
    }

    private static DateTimeOffset? TryParseSystemdTimestamp(string? raw)
    {
        // Example: "Sat 2026-02-07 20:53:28 MST"
        if (string.IsNullOrWhiteSpace(raw) || raw == "n/a") return null;

        // We don't want to fight timezones parsing here; keep best-effort.
        if (DateTimeOffset.TryParse(raw, out var dto)) return dto;
        if (DateTime.TryParse(raw, out var dt)) return new DateTimeOffset(dt);
        return null;
    }
}

internal static class Proc
{
    public static async Task<(int code, string stdout, string stderr)> RunAsync(
        string file,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return (-1, "", "Failed to start process");

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);

        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (p.ExitCode, stdout, stderr);
    }
}
