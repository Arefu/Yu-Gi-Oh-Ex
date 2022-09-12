using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WolfX.Handlers
{
    internal static class Card_Loader
    {
        internal static void Load()
        {
            MessageBox.Show(WolfX.WolfX_UI_State.Working_Directory);
            if (!File.Exists($"{WolfX.WolfX_UI_State.Working_Directory}\\2020.full.illust_a.jpg.zib") || !File.Exists($"{WolfX.WolfX_UI_State.Working_Directory}\\2020.full.illust_j.jpg.zib"))
            {
                MessageBox.Show("fuck");
            }
        }
    }
}