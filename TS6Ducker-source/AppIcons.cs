namespace TS6Ducker;

public static class AppIcons
{
    public static Icon Load(string name)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream($"TS6Ducker.Assets.{name}.ico")
            ?? throw new IOException("Missing application icon.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
