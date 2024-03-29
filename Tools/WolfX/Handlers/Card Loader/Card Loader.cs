﻿using System.Collections.Specialized;
using System.Text;
using WolfX.Extensions;
using WolfX.Handler.Tools;

namespace WolfX.Handlers
{
    internal static class Card_Loader
    {
        internal static Dictionary<uint, string> NameDict = new();
        internal static Dictionary<uint, string> DescDict = new();

        internal static void Load()
        {
            WolfUI.Form.LBL_GameStatusLabel.Text = @"Opening Cards...";
            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Orange;
            var CardIndxFile = $"{WolfUI.State.WorkingDirectory}\\bin\\CARD_Indx_{(char)WolfUI.State.Language}.bin";
            var CardNameFile = $"{WolfUI.State.WorkingDirectory}\\bin\\CARD_Name_{(char)WolfUI.State.Language}.bin";
            var CardDescFile = $"{WolfUI.State.WorkingDirectory}\\bin\\CARD_Desc_{(char)WolfUI.State.Language}.bin";
            var CardPropFile = $"{WolfUI.State.WorkingDirectory}\\bin\\CARD_Prop.bin";

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

                var FirstQuadChunk = new BitVector32((int)PropReader.ReadUInt32());
                var SecondQuadChunk = new BitVector32((int)PropReader.ReadUInt32());

                var CardId = BitVector32.CreateSection(16383);
                var CardAtk = BitVector32.CreateSection(511, CardId);
                var QuadCardDef = BitVector32.CreateSection(511, CardAtk);

                var SecondQuadUnk = BitVector32.CreateSection(1);
                var Kind = BitVector32.CreateSection(63, SecondQuadUnk);
                var Attribute = BitVector32.CreateSection(15, Kind);
                var MonsterLevel = BitVector32.CreateSection(15, Attribute);
                var Ico = BitVector32.CreateSection(7, MonsterLevel);
                var Type = BitVector32.CreateSection(31, Ico);
                var LeftScale = BitVector32.CreateSection(15, Type);
                var RightScale = BitVector32.CreateSection(15, LeftScale);

                // var unused = BitVector32.CreateSection(1, RightScale);

                var Card = new Types.Card
                {
                    _Bit1 = FirstQuadChunk,
                    _Bit2 = SecondQuadChunk,
                    _Id = (short)FirstQuadChunk[CardId],
                    _Name = NameDict[NameOffset],
                    _Description = DescDict[DescOffset],
                    _Atk = (int)(FirstQuadChunk[CardAtk] * 10),
                    _Def = (int)(FirstQuadChunk[QuadCardDef] * 10),
                    _Level = (byte)SecondQuadChunk[MonsterLevel],
                    _Attribute = (Types.Card.Attribute)SecondQuadChunk[Attribute],
                    _Type = (Types.Card.Type)SecondQuadChunk[Kind],
                    _PendulumScale1 = (byte)SecondQuadChunk[LeftScale],
                    _PendulumScale2 = (byte)SecondQuadChunk[RightScale]
                };

                if (Card.IsCardEmpty() == false)
                {
                    WolfUI.State.Cards.Add(Card);
                    WolfUI.Form.CB_CardID.Items.Add($"{Card._Id}");
                    WolfUI.Form.CB_CardName.Items.Add($"{Card._Name}");
                    WolfUI.Form.CB_CardTypes.Items.Add($"{Card._Type}");
                }
            }

            WolfUI.Form.CB_CardName.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Name;
            WolfUI.Form.TB_CardDesc.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Description;
            WolfUI.Form.CB_CardID.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Id.ToString();
            WolfUI.Form.TB_CardAtk.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Atk.ToString();
            WolfUI.Form.TB_CardDef.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Def.ToString();
            WolfUI.Form.Nud_CardLevel.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Level.ToString();
            WolfUI.Form.CB_CardTypes.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Type.ToString();
            WolfUI.Form.CB_CardAttribute.Text = WolfUI.State.Cards[WolfUI.State.CardIndex]._Attribute.ToString();

            WolfUI.Form.PB_CardPicture.Image = Preview_Generator.Get_CardImageFromArchive(WolfUI.State.Cards[WolfUI.State.CardIndex]._Id.ToString(), WolfUI.Form.CB_LoadCensoredCards.Checked);

            WolfUI.Form.LBL_GameStatusLabel.Text = @"Ready";
            WolfUI.Form.LBL_GameStatusLabel.ForeColor = Color.Green;
        }
    }
}