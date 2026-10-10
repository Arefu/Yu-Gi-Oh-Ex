namespace Wolf.Editors
{
    /// <summary>
    /// The status-line note for language files that don't match (deckdata, chardata, dueldata, scriptdata, taginfo): they still load - a mod
    /// may only edit English - and the first warning says what is off.
    /// </summary>
    public static class LanguageWarnings
    {
        public static string Describe(IReadOnlyList<string> warnings) =>
            warnings.Count == 0 ? "" : $" {warnings.Count} language warning(s): {warnings[0]}";
    }
}
