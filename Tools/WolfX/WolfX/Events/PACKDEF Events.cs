using Packdefdata;
using System.Diagnostics;

namespace WolfX
{
    public partial class WolfUI
    {
        private void PACKDEF_BTN_OpenPackDEF_Click(object sender, EventArgs e)
        {
            var File = "";
            if (State.Path == null || State.Path == "")
            {
                File = Utility.Get_UserSelectedFile("Open Packdefdata File", "packdefdata_#.bin (*.bin)|*.bin");
            }

            if (File == "")
                File = $"{State.Path}\\main\\packdefdata_{State.Language.ToString()[0]}.bin";

            var Packs = PACKDEFDATA.Load(File);
            foreach (var Pack in Packs)
            {
                Debug.WriteLine("------------");
                Debug.WriteLine($"Index: {Pack.Index}");
                Debug.WriteLine($"Series: {Pack.Series}");
                Debug.WriteLine($"Cost: {Pack.Cost}");
                Debug.WriteLine($"Type: {Pack.Type}");
                Debug.WriteLine($"Code_Name: {Pack.Code_Name}");
                Debug.WriteLine($"Name: {Pack.Name}");
            }
        }
    }
}
