using Yami_Yugi.Workers;

namespace Yami_Yugi
{
    internal class Program
    {
        private static int Main(string[] Args)
        {
            Console.Title = "Yami Yugi";
            if (Args.Length != 1)
            {
                Console.Out.WriteLine("Yami-Yugi.exe <PATH TO YGO_DATA.toc/YGO_2020.toc>");
                Console.Out.WriteLine(@"E.G: Yami-Yugi.exe C:\Program Files (x86)\Steam\SteamApps\Common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YGO_DATA.toc");
                Console.Out.WriteLine("Yami-Yugi.exe <PATH TO YGO_DATA FOLDER>");
                Console.Out.WriteLine(@"E.G: Yami-Yugi.exe YGO_DATA");
                return 0x2;
            }
            var Data = File.GetAttributes(Args[0]);
            if (Data.HasFlag(FileAttributes.Directory))
                Packer.Pack(Args);
            else
                Unpacker.Unpack(Args);
            return 0x0;
        }
    }
}