using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebAPIVP.Models
{
    public class AuditPlanModel
    {
        public int DepartmentId { get; set; }
        public string FinancialPeriod { get; set; }
        public int AuditorId { get; set; }
        public string CreatedBy { get; set; }
        public string ScopeText { get; set; }
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public string CompletionDate { get; set; }
    }
}