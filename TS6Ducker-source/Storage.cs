using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TS6Ducker;

public record Settings(int Port = 5899, int RemainingPercent = 25, int HoldMs = 650,
    int FadeMs = 400, bool OnSelf = true, bool OnOthers = true, bool Enabled = true,
    string ExcludedProcesses = "", bool IncludeWhispers = true, bool StartStopWithTeamSpeak = false);

public static class Storage
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TS6Ducker");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string FilePath(string name) => Path.Combine(Root, name);
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Root);
        string path = FilePath(name), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
    public static T Read<T>(string name, T fallback)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath(name)), Json) ?? fallback; }
        catch { return fallback; }
    }
    public static Settings LoadSettings()
    {
        var s = Read("settings.json", new Settings());
        return s with { Port = Math.Clamp(s.Port, 1, 65535), RemainingPercent = Math.Clamp(s.RemainingPercent, 0, 100),
            HoldMs = Math.Clamp(s.HoldMs, 0, 5000), FadeMs = Math.Clamp(s.FadeMs, 0, 3000), ExcludedProcesses = s.ExcludedProcesses ?? "" };
    }
    public static string ReadKey()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath("auth.dat")), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
    public static void WriteKey(string key)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(FilePath("auth.dat"), ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
    private static readonly object LogLock = new();
    public static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Root);
                var path = FilePath("app.log");
                if (System.IO.File.Exists(path) && new FileInfo(path).Length > 1_000_000) System.IO.File.Move(path, path + ".previous", true);
                System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
