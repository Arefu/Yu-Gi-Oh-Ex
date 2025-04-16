using Types;
using WolfX.Types;
using Packdefdata;
using System.Diagnostics;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PACKDEF_BTN_OpenPackDEF_Click(object sender, EventArgs e)
        {
            var File = "";

            if (String.IsNullOrEmpty(State.Path))
                File = Utility.Get_UserSelectedFile("Open Packdefdata File", "packdefdata_#.bin (*.bin)|*.bin");

            var Packs = PACKDEFDATA.Load(File);
            foreach (var Pack in Packs)
            {
                Debug.WriteLine("------------");
                Debug.WriteLine($"UNK_01: {Pack.UNK_01}");
                Debug.WriteLine($"Index: {Pack.Index}");
                Debug.WriteLine($"UNK_02: {Pack.UNK_02}");
                Debug.WriteLine($"Cost: {Pack.Cost}");
                Debug.WriteLine($"UNK_03: {Pack.UNK_03}");
                Debug.WriteLine($"Pack_Name_Start: {Pack.Pack_Name_Start}");
                Debug.WriteLine($"UNK_04: {Pack.UNK_04}");
                Debug.WriteLine($"Name_Start: {Pack.Name_Start}");
                Debug.WriteLine($"UNK_05: {Pack.UNK_05}wb");
            }
        }
    }
}
