using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Yami_Yugi.Workers
{
    internal static class Packer
    {
        internal static void Pack(string[] Args)
        {
            if (File.Exists("YGO_2020.dat"))
                File.Delete("YGO_2020.dat");
            if (File.Exists("YGO_2020.toc"))
                File.Delete("YGO_2020.toc");

            var Game = GetInstallDir();
            Console.Write($"Locating Game: {Game}");

            List<String> ExistingFiles = new();
            StreamReader Reader = new($"{Game}\\YGO_2020.TOC");
            Reader.ReadLine(); //Dispose First Line.
            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                if (Line == null) continue;

                Line = Line.TrimStart(' '); //Trim Starting Spaces.
                Line = Regex.Replace(Line, @"  +", " ", RegexOptions.Compiled); //Remove All Extra Spaces.
                var LineData = Line.Split(' ', 3); //Split Into Chunks.
                ExistingFiles.Add(LineData[2]); //Add To List For Manip.
            }
            File.AppendAllText("YGO_2020.toc", "UT\n");

            using var Writer = new BinaryWriter(File.Open("YGO_2020.dat", FileMode.Append, FileAccess.Write));
            var FilesToPack = Directory.GetFiles($"{Args[0]}", "*.*", SearchOption.AllDirectories);
            foreach (var Item in ExistingFiles)
            {
                var CurrentFileName = FilesToPack?.First(File => File.Contains(Item));

                Console.Out.WriteLine($"Packing File: {Path.GetFileName(CurrentFileName)}.");
                var CurrentFileNameLength = CurrentFileName?.Split(new[] { "YGO_2020" }, StringSplitOptions.None)
                    .Last().TrimStart('\\').Length.ToString("x");
                var CurrentFileSize = new FileInfo($"{CurrentFileName}").Length.ToString("x");

                while (CurrentFileSize.Length != 12)
                    CurrentFileSize = CurrentFileSize.Insert(0, " ");
                while (CurrentFileNameLength.Length != 2)
                    CurrentFileNameLength = CurrentFileNameLength.Insert(0, " ");

                var BReader = new BinaryReader(File.Open(CurrentFileName, FileMode.Open, FileAccess.Read));
                var NewSize = new FileInfo(CurrentFileName).Length;
                while (NewSize % 4 != 0)
                    NewSize = NewSize + 1;

                var BufferSize = NewSize - new FileInfo(CurrentFileName).Length;
                Writer.Write(BReader.ReadBytes((int)new FileInfo(CurrentFileName).Length));

                if (BufferSize > 0)
                    while (BufferSize != 0)
                    {
                        Writer.Write(new byte[] { 00 });
                        BufferSize = BufferSize - 1;
                    }

                File.AppendAllText("YGO_2020.toc",
                    $"{CurrentFileSize} {CurrentFileNameLength} {CurrentFileName.Split(new[] { "YGO_2020\\" }, StringSplitOptions.None).Last()}\n");
            }
        }

        public static string GetInstallDir()
        {
            try
            {
                using (var Root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var Key =
                        Root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1150640"))
                    {
                        return Key?.GetValue("InstallLocation").ToString();
                    }
                }
            }
            catch (Exception)
            {
                throw new Exception("Game Not Found");
            }
        }
    }
}