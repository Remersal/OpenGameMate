using OpenGameMate.Configuration;

namespace OpenGameMate.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void AppDataPaths_SeparateInstalledAndPortableRoots()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"ogm-paths-{Guid.NewGuid():N}");

        var installed = AppDataPaths.ForInstalled(baseDirectory);
        var portable = AppDataPaths.ForPortable(baseDirectory);

        Assert.Equal(AppDataMode.Installed, installed.Mode);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "OpenGameMate")), installed.RootDirectory);
        Assert.Equal(AppDataMode.Portable, portable.Mode);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "data")), portable.RootDirectory);
        Assert.EndsWith("settings.json", portable.SettingsFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonStore_RoundTripsValidatedSettings()
    {
        var testDirectory = CreateTestDirectory();
        var settingsFile = Path.Combine(testDirectory, "settings.json");
        try
        {
            var store = new JsonAppSettingsStore(settingsFile);
            var expected = new OpenGameMateSettings
            {
                Language = AppLanguage.ChineseSimplified,
                CheckRemoteAdapterRules = false,
                ShowPrivacyWarningOnFirstStart = false
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            DeleteSingleFile(settingsFile);
            DeleteSingleFile(settingsFile + ".tmp");
            DeleteEmptyDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task JsonStore_RejectsUnknownFields()
    {
        var testDirectory = CreateTestDirectory();
        var settingsFile = Path.Combine(testDirectory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(
                settingsFile,
                """{"schemaVersion":1,"language":"system","unknown":true}""");
            var store = new JsonAppSettingsStore(settingsFile);

            await Assert.ThrowsAsync<ConfigurationValidationException>(() => store.LoadAsync());
        }
        finally
        {
            DeleteSingleFile(settingsFile);
            DeleteEmptyDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task JsonStore_RejectsUnknownSchemaVersion()
    {
        var testDirectory = CreateTestDirectory();
        var settingsFile = Path.Combine(testDirectory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(
                settingsFile,
                """{"schemaVersion":2,"language":"system","checkRemoteAdapterRules":true,"showPrivacyWarningOnFirstStart":true}""");
            var store = new JsonAppSettingsStore(settingsFile);

            await Assert.ThrowsAsync<ConfigurationValidationException>(() => store.LoadAsync());
        }
        finally
        {
            DeleteSingleFile(settingsFile);
            DeleteEmptyDirectory(testDirectory);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ogm-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteSingleFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }
}
