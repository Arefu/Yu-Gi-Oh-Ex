using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WolfX
{
    internal static class WolfX_UI_State
    {
        internal static string? WorkingDirectory { get; set; }
        internal static bool? IsLoaded { get; set; }
        public static bool HasArchiveOpen { get; set; }
    }
}