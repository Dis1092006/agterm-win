using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SoundCatalogTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-sounds-").FullName;
    private string Media => Path.Combine(directory, "Media");
    private string Sounds => Path.Combine(directory, "sounds");

    public SoundCatalogTests()
    {
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Sounds);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Catalog_preserves_stems_orders_names_and_resolves_user_overrides_case_insensitively()
    {
        File.WriteAllBytes(Path.Combine(Media, "Windows Ding.wav"), []);
        File.WriteAllBytes(Path.Combine(Media, "ding.wav"), []);
        File.WriteAllBytes(Path.Combine(Media, "chimes.WAV"), []);
        File.WriteAllBytes(Path.Combine(Media, "ignore.mp3"), []);
        File.WriteAllBytes(Path.Combine(Sounds, "DING.wav"), []);
        File.WriteAllBytes(Path.Combine(Sounds, "Build done.wav"), []);
        var catalog = new SoundCatalog(directory, Media);
        Assert.Equal(["chimes", "ding", "Windows Ding"], catalog.StandardNames);
        Assert.Equal("unknown sound: Missing (use 'default', 'beep', or one of: chimes, ding, Windows Ding)",
            catalog.UnknownSoundError("Missing"));
        Assert.Equal(Path.Combine(Sounds, "DING.wav"), catalog.Resolve("ding")!.FilePath);
        Assert.Equal("DING", catalog.Resolve("DiNg.WaV")!.Name);
        Assert.Equal(Path.Combine(Media, "Windows Ding.wav"), catalog.Resolve("windows ding")!.FilePath);
        Assert.Equal(Path.Combine(Sounds, "Build done.wav"), catalog.Resolve("Build done")!.FilePath);
        Assert.True(catalog.Resolve("default")!.IsSystemDefault);
        Assert.True(catalog.Resolve("BEEP")!.IsSystemDefault);
    }

    [Fact]
    public void Resolution_refreshes_files_rejects_paths_and_does_not_trim_nonempty_values()
    {
        var catalog = new SoundCatalog(directory, Media);
        Assert.Null(catalog.Resolve("new"));
        File.WriteAllBytes(Path.Combine(Sounds, "new.wav"), []);
        Assert.NotNull(catalog.Resolve("new"));
        File.Delete(Path.Combine(Sounds, "new.wav"));
        Assert.Null(catalog.Resolve("new"));
        Assert.Null(catalog.Resolve(" "));
        Assert.Null(catalog.Resolve(""));
        Assert.Null(catalog.Resolve("../outside"));
        Assert.Null(catalog.Resolve(@"..\outside"));
        Assert.Null(catalog.Resolve(Path.Combine(Media, "ding.wav")));
        Assert.Null(catalog.Resolve(" default "));
    }

    [Fact]
    public void A_stem_ending_in_wav_is_not_mistaken_for_an_extension()
    {
        File.WriteAllBytes(Path.Combine(Sounds, "Result.wav.wav"), []);
        var catalog = new SoundCatalog(directory, Media);
        Assert.Equal("Result.wav", catalog.Resolve("Result.wav")?.Name);
    }

    [Fact]
    public async Task Editor_accepts_named_sounds_and_preserves_a_missing_stored_sound()
    {
        var store = new SettingsStore(directory);
        store.Save(new AppSettings { NotificationSoundName = "Deleted sound" });
        var coordinator = new SettingsCoordinator(store);
        Assert.Equal(directory, coordinator.StateDirectory);
        var editor = new SettingsEditor(coordinator);
        Assert.Equal("Deleted sound", editor.Draft.NotificationSoundName);
        await editor.SetAsync("notificationSoundName", "Windows Notify Calendar");
        await editor.SetAsync("blockedStatusSoundName", "Build done");
        Assert.Equal("Windows Notify Calendar", store.Load().NotificationSoundName);
        Assert.Equal("Build done", store.Load().BlockedStatusSoundName);
    }
}
