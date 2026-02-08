namespace LearnObserve;

public static class DotEnv
{
    public static void LoadIfPresent(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
            {
                val = val[1..^1];
            }
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}
