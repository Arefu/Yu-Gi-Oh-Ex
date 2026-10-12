using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

internal static class Program
{
    static Assembly editor = null!;
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static object New(string name, params object[] args) => Activator.CreateInstance(editor.GetType(name, true)!, Flags & ~BindingFlags.Static, null, args, null)!;
    static object? Field(object o, string name) => o.GetType().GetField(name, Flags)!.GetValue(o);
    static void Set(object o, string name, object? value) => o.GetType().GetField(name, Flags)!.SetValue(o, value);
    static object? Call(object o, string name, params object[] args)
    {
        try { return (o is Type t ? t : o.GetType()).GetMethod(name, Flags)!.Invoke(o is Type ? null : o, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static object? Prop(object o, string name) => o.GetType().GetProperty(name, Flags)!.GetValue(o);
    static void PSet(object o, string name, object? value) => o.GetType().GetProperty(name, Flags)!.SetValue(o, value);
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Fails(Action action, string label)
    {
        try { action(); } catch (IOException) { Check(true, label); return; }
        catch (UnauthorizedAccessException) { Check(true, label); return; }
        catch (InvalidDataException) { Check(true, label); return; }
        catch (System.Text.Json.JsonException) { Check(true, label); return; }
        throw new Exception("Expected save failure: " + label);
    }
    [STAThread]
    static void Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(Path.GetDirectoryName(dll)!, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(dll)!, name.Name + ".dll")) : null;
        editor = Assembly.LoadFrom(dll);
        string first = Path.Combine(root, "rollback-first.txt"), second = Path.Combine(root, "rollback-second.txt");
        File.WriteAllText(first, "first original"); File.WriteAllText(second, "second original");
        IEnumerable<string> BecomeReadOnlyAfterStaging()
        {
            yield return second;
            File.SetAttributes(second, FileAttributes.ReadOnly);
        }
        try
        {
            Fails(() => Call(editor.GetType("WolfEx.ContentSaveBatch", true)!, "Write",
                new Dictionary<string, byte[]> { [first] = System.Text.Encoding.UTF8.GetBytes("changed") }, BecomeReadOnlyAfterStaging()), "late commit failure triggers rollback");
            Check(File.ReadAllText(first) == "first original" && File.ReadAllText(second) == "second original", "rollback restores an already replaced file");
        }
        finally { File.SetAttributes(second, FileAttributes.Normal); }
        string cardsDir = Path.Combine(root, "cards"); Directory.CreateDirectory(cardsDir);
        string source = Path.Combine(root, "art.jpg");
        using (var bitmap = new System.Drawing.Bitmap(600, 600)) { using var g = System.Drawing.Graphics.FromImage(bitmap); g.Clear(System.Drawing.Color.RoyalBlue); bitmap.Save(source, System.Drawing.Imaging.ImageFormat.Jpeg); }
        string json = Path.Combine(cardsDir, "cards.json"), old = Path.Combine(cardsDir, "15300.png"), uncensored = Path.Combine(cardsDir, "15300_uncensored.png");
        File.WriteAllText(json, "[]"); File.WriteAllText(old, "original art"); File.WriteAllText(uncensored, "original alternate");
        var cards = New("WolfEx.CardsPanel"); var card = New("WolfEx.CardsPanel+CardModel");
        Set(card, "Name", "Editor Safety Test"); Set(card, "Kind", "Normal"); Set(card, "Atk", 1700); Set(card, "Def", 1100);
        Set(card, "Description", "Saved together with its blue artwork."); Set(card, "Image", "15300.png"); Set(card, "PendingArt", source);
        Set(card, "ImageUncensored", "15300_uncensored.png"); Set(card, "DropUncensored", true);
        ((IList)Field(cards, "_cards")!).Add(card);
        using (var locked = new FileStream(json, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fails(() => Call(cards, "SaveFiles", cardsDir), "locked JSON rejects artwork batch");
        Check(File.ReadAllText(json) == "[]" && File.ReadAllText(old) == "original art" && File.ReadAllText(uncensored) == "original alternate", "failed card save preserves all originals");
        Check((string?)Field(card, "PendingArt") == source && (bool)Field(card, "DropUncensored")!, "failed card save preserves pending artwork and removal");
        Call(cards, "SaveFiles", cardsDir);
        Check(File.Exists(Path.Combine(cardsDir, "15300.jpg")) && !File.Exists(old) && !File.Exists(uncensored), "retry commits artwork and removals");
        Check(JsonNode.Parse(File.ReadAllText(json))!["cards"]![0]!["image"]!.GetValue<string>() == "15300.jpg" && Field(card, "PendingArt") == null, "retry commits JSON and pending state together");
        Check(Directory.GetFiles(cardsDir, "*.bak").Length >= 3, "card originals remain backed up");

        var catalog = editor.GetType("WolfEx.ArchetypeCatalog", true)!;
        string archetypePath = Path.Combine(cardsDir, "Archetypes.json");
        foreach (string bad in new[] { "{broken", "{}", "{\"archetypes\":[{\"code\":419,\"name\":\"A\"},{\"code\":419,\"name\":\"B\"}]}" })
        {
            File.WriteAllText(archetypePath, bad); Call(catalog, "Load", cardsDir);
            Check(catalog.GetProperty("LoadError", Flags)!.GetValue(null) != null, "malformed archetype catalog reports load error");
            Fails(() => Call(catalog, "Add", "Do not overwrite"), "malformed archetype catalog rejects add");
            Check(File.ReadAllText(archetypePath) == bad, "malformed archetype catalog remains intact");
        }
        File.WriteAllText(archetypePath, "{\"metadata\":\"keep\",\"archetypes\":[{\"code\":419,\"name\":\"Existing\",\"extra\":true}]}");
        Call(catalog, "Load", cardsDir);
        using (var locked = new FileStream(archetypePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fails(() => Call(catalog, "Add", "Editor Safety Archetype"), "locked archetype catalog rejects add");
        Check((int)Call(catalog, "Add", "Editor Safety Archetype")! == 420, "failed archetype add does not consume an ID");
        var savedCatalog = JsonNode.Parse(File.ReadAllText(archetypePath))!;
        Check(savedCatalog["metadata"]!.GetValue<string>() == "keep" && savedCatalog["archetypes"]![0]!["extra"]!.GetValue<bool>(), "archetype add preserves unknown data");
        PSet(card, "Archetypes", new List<int> { 420 });

        var manager = New("WolfX.CardManager", New("Wolf.Editors.CardGenreEditor"), New("Wolf.Editors.RelatedCardsEditor"), New("Wolf.Editors.CardLinkEditor"), cards);
        Set(manager, "_files", New("Wolf.Editors.GameFolderFiles", root));
        Call(cards, "MarkSaved"); Set(card, "Description", "Saved together with its blue artwork. Retry verified.");
        var changed = Field(manager, "_changedCards")!; Call(changed, "Add", 15300);
        PSet(cards, "SaveRequested", new Func<bool>(() => false));
        Check(!(bool)Call(manager, "Save")!, "Card Manager propagates custom save failure");
        Check((int)Prop(changed, "Count")! == 1 && (bool)Prop(cards, "Dirty")!, "Card Manager retains edits after failure");
        PSet(cards, "SaveRequested", null);
        Check(!(bool)Call(manager, "Save")!, "missing custom save callback cannot report success");
        PSet(cards, "SaveRequested", new Func<bool>(() => { Call(cards, "SaveFiles", cardsDir); Call(cards, "MarkSaved"); return true; }));
        Check((bool)Call(manager, "Save")! && (int)Prop(changed, "Count")! == 0, "Card Manager retry reports success and clears changes");

        var pages = New("WolfEx.Designer.PagesPanel"); string pagesDir = Path.Combine(root, "pages"); Directory.CreateDirectory(pagesDir); Set(pages, "_folder", pagesDir);
        var document = Prop(Field(pages, "_canvas")!, "Document")!;
        PSet(document, "Name", "original"); PSet(document, "Header", "Original"); Call(pages, "SaveDocument", false);
        string originalPage = Path.Combine(pagesDir, "original.json"), originalText = File.ReadAllText(originalPage);
        PSet(document, "Name", "editor-safety-test"); PSet(document, "Header", "Editor Save Safety Verified");
        var text = New("WolfEx.Designer.PageElement");
        PSet(text, "Id", "verificationText"); PSet(text, "Kind", "text"); PSet(text, "Text", "Saved editor content loaded successfully");
        PSet(text, "X", 400f); PSet(text, "Y", 260f); PSet(text, "Width", 1120f); PSet(text, "Height", 150f); PSet(text, "Align", "center"); PSet(text, "TextSize", (float?)40);
        ((IList)Prop(document, "Elements")!).Add(text);
        using (var locked = new FileStream(originalPage, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fails(() => Call(pages, "SaveDocument", true), "locked original rejects page rename batch");
        Check(!File.Exists(Path.Combine(pagesDir, "editor-safety-test.json")) && File.ReadAllText(originalPage) == originalText && (bool)Prop(pages, "Dirty")!, "failed rename preserves page and dirty state");
        Call(pages, "SaveDocument", true);
        Check(!File.Exists(originalPage) && File.Exists(Path.Combine(pagesDir, "editor-safety-test.json")) && !(bool)Prop(pages, "Dirty")!, "page rename retry commits and marks saved");
        Check(Directory.GetFiles(pagesDir, "*.bak").Length > 0, "renamed page original remains backed up");
        Console.WriteLine($"{checks} checks passed. Game fixtures: {root}");
    }
}
