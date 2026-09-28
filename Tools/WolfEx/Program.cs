using System.Text.Json;

namespace WolfEx
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Headless check of the EffectScript compiler (no window): WolfEx.exe --compile <script file> <result file> [Yu-Gi-Oh-Ex folder]
            if (args.Length >= 3 && args[0] == "--compile")
            {
                if (args.Length > 3)
                    ArchetypeCatalog.Load(args[3]);
                var result = EffectScriptCompiler.Compile(File.ReadAllText(args[1]));
                File.WriteAllText(args[2], result.Ok
                    ? "OK " + result.Compiled!.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
                    : "ERROR " + result.Error);
                return;
            }

            // Anything that goes wrong is also written to %APPDATA%\WolfEx\crash.log (a crash on launch otherwise leaves nothing to read).
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfEx", "crash.log");
            void Record(object? error)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {error}{Environment.NewLine}{Environment.NewLine}");
                }
                catch { /* nothing more can be done */ }
            }
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject);
            Application.ThreadException += (_, e) => Record(e.Exception);

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception e)
            {
                Record(e);
                MessageBox.Show("WolfEx could not start: " + e.Message + Environment.NewLine + "Details: " + log, "WolfEx", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
