using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WolfX.Types
{
    static class Dfymoo
    {
        internal static void Load()
        {
            var Archives = new List<string>();
            Archives.AddRange(Directory.GetFiles(WolfUI.State.WorkingDirectory!, "*.dfymoo", SearchOption.AllDirectories));

            foreach (var Archive in Archives)
            {
                WolfUI.Form.lv_DfymooItems.Items.Add(new FileInfo(Archive).Name, 0);
            }

            WolfUI.Form.lbl_Dfymoo_NumOfItems.Text = WolfUI.Form.lv_DfymooItems.Items.Count.ToString();
        }

        internal static string FindDfyMooFromName(string Name)
        {
            var Archives = new List<string>();
            Archives.AddRange(Directory.GetFiles(WolfUI.State.WorkingDirectory!, "*.dfymoo", SearchOption.AllDirectories));

            foreach (var Archive in Archives)
            {
                if (new FileInfo(Archive).Name == Name)
                {
                    return new FileInfo(Archive).FullName;
                }
            }
            return "";
        }
    }
}
