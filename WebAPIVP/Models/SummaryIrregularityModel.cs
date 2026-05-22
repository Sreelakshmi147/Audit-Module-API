using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebAPIVP.Models
{
    public class SummaryIrregularityModel
    {
        public string Category { get; set; }

        public int High { get; set; }

        public int Medium { get; set; }

        public int Low { get; set; }

        public int Total { get; set; }
    }
}