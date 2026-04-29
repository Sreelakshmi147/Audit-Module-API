using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebAPIVP.Models
{
    public class ScopeModel
    {
        public string AuditId { get; set; }

        public string ScopeText { get; set; }

        public string Status { get; set; }

        public string EmpCode { get; set; }

        public string RejectionRemark { get; set; }

        public List<ChecklistItemModel> Checklist { get; set; }
    }
}