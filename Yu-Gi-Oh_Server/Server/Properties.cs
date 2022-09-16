using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yu_Gi_Oh_Server.Server
{
    internal class Settings
    {
        public bool Pure { get; set; }
        public int Lifepoints { get; set; }
        public int MaxCardsInHand { get; set; }
        public int Rules { get; set; }
        public int MaxNumberOfDuelists { get; set; }

        //Impliment ToString using reflection to read properties in this class
        public override string ToString()
        {
            var Sb = new StringBuilder();
            foreach (var Property in GetType().GetProperties())
            {
                Sb.AppendLine($"Property: {Property.Name}\nValue: {Property.GetValue(this)}");
            }
            return Sb.ToString();
        }
    }
}