using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebAPIVP.Models
{
    public class ChecklistItemModel
    {
        public int SlNo { get; set; }
        public string ChecklistItem { get; set; }
        public string Answer { get; set; }
        public string Remarks { get; set; }
    }
}