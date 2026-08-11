using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dujahit.Models.UI
{

    /*
     
        Reuse this for the modular map view

        This system will scale and manage all maps that the users input into the program (this is out of scope, but I'll do it anyway whatever)

     */

    public class ModularScreenManager
    {

    }

    public class GridCell
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsOccupied { get; set; }
    }

    public class WidgetTemplate
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int[] GridSize { get; set; }
        public string Content { get; set; }
    }
}
