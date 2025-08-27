using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Parameters
{
    public class DisplayParameters
    {
        public string Name { get; set; }
        public DateTime DateTime { get; set; }
        public string Live { get; set; }
        public double Price { get; set; }
        public double OrderPrice { get; set; }
        public int OrderQty { get; set; }
        public double FillPrice { get; set; }
        public int Position { get; set; }

        public DisplayParameters(string name)
        {
            this.Name = name;
            Live = "OFF";
        }
    }
}
