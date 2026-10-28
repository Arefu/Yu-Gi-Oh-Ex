using savegame;
using WolfX;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The compatibility report reads Windows Config.ini settings.");
string root = Path.Combine(Path.GetTempPath(), "WolfX-EditorSafety-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action run) { run(); passed++; Console.WriteLine("PASS " + name); }
byte[] Fixture()
{
    var b = new byte[SaveFile.Size];
    BitConverter.GetBytes(SaveFile.Magic).CopyTo(b, 0);
    BitConverter.GetBytes(SaveFile.CurrentVersion).CopyTo(b, 4);
    BitConverter.GetBytes((uint)SaveFile.Size).CopyTo(b, 8);
    return new SaveFile(b).ToBytes();
}
try
{
    Test("Atomic replacement writes the save and leaves no backup or temporary file", () =>
    {
        string target = Path.Combine(root, "profile.dat");
        File.WriteAllBytes(target, Fixture());
        var save = SaveFile.Load(target); uint counter = save.SaveCounter;
        save.Player.Wallet = 12345; save.Save(target);
        Check(SaveFile.Load(target).ChecksumIsValid && save.SaveCounter == counter + 1, "Successful save must update counter and checksum.");
        save.Player.Wallet = 54321; save.Save(target);
        Check(SaveFile.Load(target).Player.Wallet == 54321, "Repeated saves must write the latest edits.");
        Check(Directory.EnumerateFiles(root).Select(Path.GetFileName).SequenceEqual(["profile.dat"]), "Saving must not leave backups or temporary files.");
    });
    Test("Locked target failure preserves disk bytes and in-memory header, then allows retry", () =>
    {
        string target = Path.Combine(root, "locked.dat"); File.WriteAllBytes(target, Fixture());
        var save = SaveFile.Load(target); uint counter = save.SaveCounter, checksum = save.Checksum;
        byte[] previous = File.ReadAllBytes(target); save.Player.Wallet = 6789;
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false; try { save.Save(target); } catch (IOException) { failed = true; }
            Check(failed, "Saving a locked target must fail.");
            Check(save.SaveCounter == counter && save.Checksum == checksum, "Failed write must not advance header.");
        }
        Check(File.ReadAllBytes(target).SequenceEqual(previous), "Failed write must preserve old file.");
        Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Failed write must clean temporary file.");
        save.Save(target); Check(SaveFile.Load(target).Player.Wallet == 6789, "Retry must save pending edits.");
    });
    Test("New file has valid checksum", () =>
    {
        var save = new SaveFile(Fixture()); string target = Path.Combine(root, "new.dat"); save.Save(target);
        Check(SaveFile.Load(target).ChecksumIsValid, "New target must be valid.");
    });
    Test("Wrong magic, version and declared size are rejected without overwriting", () =>
    {
        foreach (int offset in new[] { 0, 4, 8 })
        {
            byte[] invalid = Fixture(); invalid[offset] ^= 1;
            string source = Path.Combine(root, "invalid.dat"); File.WriteAllBytes(source, invalid);
            bool rejected = false; try { SaveFile.Load(source); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid header must not load.");
            rejected = false; try { new SaveFile(invalid).Save(source); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(source).SequenceEqual(invalid), "Invalid header must not overwrite its source.");
        }
    });
    Test("Profile labels identify Steam account, configured custom slots and unknown files", () =>
    {
        string game = Path.Combine(root, "game"), steam = Path.Combine(root, "Steam");
        Check(SaveProfileIdentity.Describe(Path.Combine(steam, "userdata", "123", "1150640", "remote", "savegame.dat"), game, "custom.dat", steam).StartsWith("Steam save — account 123"), "Steam account label missing.");
        Check(SaveProfileIdentity.Describe(Path.Combine(game, "custom-3.dat"), game, "custom.dat", steam).Contains("slot 3"), "Configured custom slot missing.");
        Check(SaveProfileIdentity.Describe(Path.Combine(root, "outside.dat"), game, "custom.dat", steam).StartsWith("Standalone"), "Unknown save must not be classified as Steam.");
        Check(SaveProfileIdentity.Describe(Path.Combine(game, "savegame-ex.dat"), game, "custom.dat", steam).Contains("not a slot"), "Inactive profile must not be labelled configured.");
    });
    string gameFolder = Path.Combine(root, "compatibility");
    string exFolder = Path.Combine(gameFolder, "Yu-Gi-Oh-Ex"), pluginsFolder = Path.Combine(gameFolder, "Plugins");
    Directory.CreateDirectory(exFolder); Directory.CreateDirectory(pluginsFolder);
    void Dll(string name) => File.WriteAllBytes(Path.Combine(pluginsFolder, name + ".dll"), []);
    Dll("Yu-Gi-Oh-Core"); Dll("Yu-Gi-Oh-MoreCards");
    Test("Compiled card effects require the effect plugin, ordinary text does not", () =>
    {
        string path = Path.Combine(exFolder, "cards.json");
        File.WriteAllText(path, "{\"cards\":[{\"name\":\"effectClone\"}]}");
        Check(!ContentCompatibility.Check(gameFolder, exFolder).Single().Problem, "Name text must not require Effects.");
        File.WriteAllText(path, "{\"cards\":[{\"effectClone\":{}}]}");
        var entry = ContentCompatibility.Check(gameFolder, exFolder).Single();
        Check(entry.Problem && entry.Status.Contains("Yu-Gi-Oh-Effects"), "Compiled effect must report missing engine.");
        Dll("Yu-Gi-Oh-Effects"); Check(!ContentCompatibility.Check(gameFolder, exFolder).Single().Problem, "Installed engine should satisfy content.");
    });
    Test("Unsupported exports and malformed JSON are reported", () =>
    {
        File.WriteAllText(Path.Combine(exFolder, "cardlinks.json"), "{}");
        File.WriteAllText(Path.Combine(exFolder, "cards.json"), "{broken");
        var entries = ContentCompatibility.Check(gameFolder, exFolder);
        Check(entries.Single(e => e.Content == "cardlinks.json").Status.Contains("No plugin"), "Unsupported export must be explicit.");
        Check(entries.Single(e => e.Content == "cards.json").Status.Contains("Cannot read cards.json"), "Malformed JSON must be reported.");
    });
    Test("Transitive manifest dependencies and companion DLLs are checked", () =>
    {
        File.WriteAllText(Path.Combine(exFolder, "cards.json"), "{}");
        File.WriteAllText(Path.Combine(pluginsFolder, "Yu-Gi-Oh-MoreCards.json"), "{\"requires\":[\"Helper\"]}");
        Dll("Helper"); File.WriteAllText(Path.Combine(pluginsFolder, "Helper.json"), "{\"dlls\":[\"Companion.dll\"]}");
        Check(ContentCompatibility.Check(gameFolder, exFolder).Single(e => e.Content == "cards.json").Status.Contains("Companion"), "Missing nested companion must be reported.");
        Dll("Companion"); Check(!ContentCompatibility.Check(gameFolder, exFolder).Single(e => e.Content == "cards.json").Problem, "Installed dependencies must satisfy report.");
    });
    Test("Configured missing plugin directory is not replaced by another installed directory", () =>
    {
        File.WriteAllText(Path.Combine(gameFolder, "Config.ini"), "[Yu-Gi-Oh-Core]\nPluginsPath=MissingPlugins\n");
        Check(ContentManifest.PluginsFolder(gameFolder) == Path.Combine(gameFolder, "MissingPlugins"), "Report must use actual configured directory.");
        Check(ContentCompatibility.Check(gameFolder, exFolder).Single(e => e.Content == "cards.json").Status.Contains("Missing plugin"), "Configured missing DLLs must be reported.");
    });
    Console.WriteLine($"{passed} regression checks passed.");
}
finally { Directory.Delete(root, recursive: true); }
