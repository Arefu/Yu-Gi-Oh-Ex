namespace DuelIt
{
    internal static class Program
    {
        /// <summary>DuelIt [path to a duel log]. Without a path it opens Duels.log in the game folder.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
