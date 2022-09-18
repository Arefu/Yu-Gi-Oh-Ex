using Microsoft.VisualBasic;
using System.Collections.Specialized;
using System.Text;
using WolfX.Extensions;
using WolfX.Handler.Tools;
using WolfX.Types;

namespace WolfX.Handlers
{
    internal static class CardLoader
    {
        internal static Dictionary<uint, string> NameDict = new();
        internal static Dictionary<uint, string> DescDict = new();

        internal static void Load()
        {
            var CardIndxFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Indx_{(char)WolfX_UI_State.Language}.bin";
            var CardNameFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Name_{(char)WolfX_UI_State.Language}.bin";
            var CardDescFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Desc_{(char)WolfX_UI_State.Language}.bin";
            var CardPropFile = $"{WolfX_UI_State.WorkingDirectory}\\bin\\CARD_Prop.bin";

            if (!File.Exists(CardIndxFile) || !File.Exists(CardNameFile) || !File.Exists(CardDescFile))
                return;

            using var IndxReader =
                new BinaryReader(File.Open(CardIndxFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var NameReader =
                new BinaryReader(File.Open(CardNameFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var DescReader =
                new BinaryReader(File.Open(CardDescFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            using var PropReader =
                new BinaryReader(File.Open(CardPropFile, FileMode.Open, FileAccess.Read, FileShare.Read));

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

            while (true)
            {
                var NameOffset = IndxReader.ReadUInt32();
                var DescOffset = IndxReader.ReadUInt32();

                if (IndxReader.BaseStream.Position >= IndxReader.BaseStream.Length)
                    break;

                var Bit1 = new BitVector32((int)PropReader.ReadUInt32());
                var bit1_mrk = BitVector32.CreateSection(16383);
                var bit1_attack = BitVector32.CreateSection(511, bit1_mrk);
                var bit1_defence = BitVector32.CreateSection(511, bit1_attack);

                var bit2 = new BitVector32((int)PropReader.ReadUInt32());
                var bit2_exist = BitVector32.CreateSection(1);
                var bit2_kind = BitVector32.CreateSection(63, bit2_exist);
                var bit2_attr = BitVector32.CreateSection(15, bit2_kind);
                var bit2_level = BitVector32.CreateSection(15, bit2_attr);
                var bit2_icon = BitVector32.CreateSection(7, bit2_level);
                var bit2_type = BitVector32.CreateSection(31, bit2_icon);
                var bit2_scaleL = BitVector32.CreateSection(15, bit2_type);
                var bit2_scaleR = BitVector32.CreateSection(15, bit2_scaleL);

                var bit2_unused = BitVector32.CreateSection(1, bit2_scaleR);

                //card.CardType = (CardType)bit2[bit2_kind];
                //card.SpellType = (SpellType)bit2[bit2_icon];
                //card.MonsterType = (MonsterType)bit2[bit2_type];
                //card.PendulumScale1 = (byte)bit2[bit2_scaleL];
                //card.PendulumScale2 = (byte)bit2[bit2_scaleR];

                var Card = new Card
                {
                    _Id = (short)Bit1[bit1_mrk],
                    _Name = NameDict[NameOffset],
                    _Description = DescDict[DescOffset],
                    _Atk = (int)(Bit1[bit1_attack] * 10),
                    _Def = (int)(Bit1[bit1_defence] * 10),
                    _Level = (byte)bit2[bit2_level],
                    _Attribute = (Card.Attribute)bit2[bit2_attr],
                    _Type = (Card.Type)bit2[bit2_kind]
                };

                WolfX_UI_State.Cards.Add(Card);
                WolfUI.Form.CB_CardID.Items.Add($"{Card._Id} - ({Card._Name})");
                WolfUI.Form.TB_CardName.AutoCompleteCustomSource.Add(Card._Name);
            }

            WolfUI.Form.TB_CardName.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Name;
            WolfUI.Form.TB_CardDesc.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Description;
            WolfUI.Form.CB_CardID.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Id.ToString();
            WolfUI.Form.TB_CardAtk.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Atk.ToString();
            WolfUI.Form.TB_CardDef.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Def.ToString();
            WolfUI.Form.Nud_CardLevel.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Level.ToString();
            WolfUI.Form.CB_CardTypes.Text = WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Type.ToString();
            WolfUI.Form.PB_CardPicture.Image =
                Preview_Generator.Get_ImageFromArchive("2020.full.illust_j.jpg.zib", WolfX_UI_State.Cards[WolfX_UI_State.CardIndex]._Id.ToString());
        }
    }
}