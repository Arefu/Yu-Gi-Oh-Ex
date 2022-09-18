using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WolfX.Extensions;
using WolfX.Handler.Tools;

namespace WolfX.Handlers
{
    internal static class Card_Loader
    {
        internal static Dictionary<uint, string> NameDict = new Dictionary<uint, string>();
        internal static Dictionary<uint, string> DescDict = new Dictionary<uint, string>();

        internal static void Load()
        {
            var CardIndxFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Indx_{(char)WolfX_UI_State.Language}.bin";
            var CardNameFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Name_{(char)WolfX_UI_State.Language}.bin";
            var CardDescFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Desc_{(char)WolfX_UI_State.Language}.bin";

            if (!File.Exists(CardIndxFile) || !File.Exists(CardNameFile) || !File.Exists(CardDescFile))
                return;

            using var IndxReader =
                new BinaryReader(File.Open(CardIndxFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var NameReader =
                new BinaryReader(File.Open(CardNameFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var DescReader =
                new BinaryReader(File.Open(CardDescFile, FileMode.Open, FileAccess.Read, FileShare.Read));

            while (NameReader.BaseStream.Position != NameReader.BaseStream.Length)
            {
                var Offset = (uint)NameReader.BaseStream.Position;
                var CardName = NameReader.ReadNullTerminatedString(Encoding.Unicode);

                NameDict.Add(Offset, CardName);
            }

            while (DescReader.BaseStream.Position != DescReader.BaseStream.Length)
            {
                var Offset = (uint)DescReader.BaseStream.Position;
                var CardDesc = DescReader.ReadNullTerminatedString(Encoding.Unicode);

                DescDict.Add(Offset, CardDesc);
            }
        }
    }
}