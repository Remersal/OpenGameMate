namespace OpenGameMate.Configuration;

public enum AppDataMode
{
    Installed,
    Portable
}

public sealed record AppDataPaths(
    AppDataMode Mode,
    string RootDirectory,
    string SettingsFile,
    string LogsDirectory,
    string WebViewUserDataDirectory,
    string TemporaryDirectory,
    string AdapterRulesDirectory)
{
    public static AppDataPaths ForInstalled(string? localAppDataRoot = null)
    {
        var localAppData = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;
        return Create(AppDataMode.Installed, Path.Combine(localAppData, "OpenGameMate"));
    }

    public static AppDataPaths ForPortable(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        return Create(AppDataMode.Portable, Path.Combine(executableDirectory, "data"));
    }

    public static AppDataPaths Resolve(IEnumerable<string> arguments, string executableDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument => string.Equals(argument, "--portable", StringComparison.OrdinalIgnoreCase))
            ? ForPortable(executableDirectory)
            : ForInstalled();
    }

    private static AppDataPaths Create(AppDataMode mode, string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        return new AppDataPaths(
            mode,
            root,
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "WebView2"),
            Path.Combine(root, "temp"),
            Path.Combine(root, "adapter-rules"));
    }
}
