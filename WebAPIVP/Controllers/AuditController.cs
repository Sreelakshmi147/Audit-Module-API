using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using WebAPIVP.ConnectionString;
using WebAPIVP.Models;
using System.Configuration;


namespace WebAPIVP.Controllers
{
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    [RoutePrefix("api/audit")]
    public class AuditController : ApiController
    {
        private readonly string _connectionString;

        public AuditController()
        {
            MaafinDbHelper db = new MaafinDbHelper();
            db.Connection();
            _connectionString = db.conStr1;
        }

        // 🔹 GET EMPLOYEES BY DEPARTMENT
        // Example:
        // https://localhost:44386/api/audit/getemployeesbydept/4
        [HttpGet]
        [Route("GetDepartments")]
        public IHttpActionResult GetDepartments()
        {
            try
            {
                List<object> departments = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                SELECT DISTINCT d.DEP_ID,
                                d.DEP_NAME
                FROM DEPARTMENT_MST d
                JOIN EMPLOYEE_MASTER e
                     ON d.DEP_ID = e.DEPARTMENT_ID
                WHERE e.FIRM_ID = 4
                  AND e.BRANCH_ID = 0
                  AND e.STATUS_ID = 1
                ORDER BY d.DEP_NAME";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            departments.Add(new
                            {
                                DepId = dr["DEP_ID"].ToString(),
                                DepName = dr["DEP_NAME"].ToString()
                            });
                        }
                    }
                }

                return Ok(departments);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }



        [HttpGet]
        [Route("getauditors")]
        public IHttpActionResult GetAuditors()
        {
            try
            {
                List<object> auditors = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                SELECT EMP_CODE, EMP_NAME
                FROM EMPLOYEE_MASTER
                WHERE FIRM_ID = 4
                  AND BRANCH_ID = 0
                  AND STATUS_ID = 1
                  AND DEPARTMENT_ID = 4
                  AND POST_ID != 750
                ORDER BY EMP_NAME";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            auditors.Add(new
                            {
                                EmpCode = dr["EMP_CODE"].ToString(),
                                EmpName = dr["EMP_NAME"].ToString()
                            });
                        }
                    }
                }

                return Ok(auditors);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }



        [HttpPost]
        [Route("saveauditplan")]
        public IHttpActionResult SaveAuditPlan(AuditPlanModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.CreatedBy))
                return BadRequest("Invalid request");

            try
            {
                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string roleQuery = @"
SELECT post_id, department_id, branch_id, firm_id, status_id
FROM employee_master
WHERE emp_code = :empCode";

                    OracleCommand roleCmd = new OracleCommand(roleQuery, con);
                    roleCmd.Parameters.Add(":empCode", model.CreatedBy);

                    int postId = 0;
                    int departmentId = 0;
                    int branchId = 0;
                    int firmId = 0;
                    int statusId = 0;

                    using (OracleDataReader dr = roleCmd.ExecuteReader())
                    {
                        if (!dr.Read())
                            return Unauthorized();

                        postId = Convert.ToInt32(dr["POST_ID"]);
                        departmentId = Convert.ToInt32(dr["DEPARTMENT_ID"]);
                        branchId = Convert.ToInt32(dr["BRANCH_ID"]);
                        firmId = Convert.ToInt32(dr["FIRM_ID"]);
                        statusId = Convert.ToInt32(dr["STATUS_ID"]);
                    }

                    if (!(departmentId == 4 &&
      (postId == 750 || postId == 843) &&
      branchId == 0 &&
      firmId == 4 &&
      statusId == 1))
                    {
                        return Content(System.Net.HttpStatusCode.Forbidden,
                            "Only Audit Head can create audit plans.");
                    }

                    string currentYear = DateTime.Now.Year.ToString();

                    string query = @"
SELECT NVL(MAX(
    TO_NUMBER(SUBSTR(AUDIT_ID, -3))
), 0)
FROM MAAF_INT_HOAUDIT_PLAN
WHERE AUDIT_ID LIKE :pattern";

                    OracleCommand cmdMax = new OracleCommand(query, con);
                    cmdMax.Parameters.Add(":pattern", "AUD-HO-" + currentYear + "-%");

                    int lastNumber = Convert.ToInt32(cmdMax.ExecuteScalar());
                    int nextNumber = lastNumber + 1;
                    string formattedNumber = nextNumber.ToString("D3");

                    string auditId = "AUD-HO-" + currentYear + "-" + formattedNumber;

                    // 🔥 UPDATED INSERT (WITH COMPLETION_DATE ADDED)
                    string insertQuery = @"
INSERT INTO MAAF_INT_HOAUDIT_PLAN
(AUDIT_ID, DEPARTMENT_ID, FINANCIAL_PERIOD, AUDITOR_ID, START_DATE, END_DATE, COMPLETION_DATE, STATUS)
VALUES
(:AUDIT_ID, :DEPARTMENT_ID, :FINANCIAL_PERIOD, :AUDITOR_ID, :START_DATE, :END_DATE, :COMPLETION_DATE, 'Planned')";

                    OracleCommand cmdInsert = new OracleCommand(insertQuery, con);

                    cmdInsert.Parameters.Add(":AUDIT_ID", auditId);
                    cmdInsert.Parameters.Add(":DEPARTMENT_ID", model.DepartmentId);
                    cmdInsert.Parameters.Add(":FINANCIAL_PERIOD", model.FinancialPeriod);
                    cmdInsert.Parameters.Add(":AUDITOR_ID", model.AuditorId);

                    // ✅ USING FROM, TO, COMPLETION DATE
                    cmdInsert.Parameters.Add(":START_DATE", DateTime.Parse(model.FromDate));
                    cmdInsert.Parameters.Add(":END_DATE", DateTime.Parse(model.ToDate));
                    cmdInsert.Parameters.Add(":COMPLETION_DATE", DateTime.Parse(model.CompletionDate));

                    cmdInsert.ExecuteNonQuery();

                    // ✅ EXISTING SCOPE CODE (UNCHANGED)
                    if (!string.IsNullOrEmpty(model.ScopeText))
                    {
                        string scopeInsert = @"
INSERT INTO MAAF_INT_HOAUDIT_SCOPE
(SCOPE_ID, AUDIT_ID, SCOPE_TEXT, STATUS, APPROVAL_LEVEL, CREATED_BY, CREATED_DATE)
VALUES
(SEQ_MAAF_INT_HOAUDIT_SCOPE.NEXTVAL,
 :auditId,
 :scopeText,
 'Draft',
 0,
 :createdBy,
 SYSDATE)";

                        OracleCommand scopeCmd = new OracleCommand(scopeInsert, con);

                        scopeCmd.Parameters.Add(":auditId", auditId);
                        scopeCmd.Parameters.Add(":scopeText", model.ScopeText);
                        scopeCmd.Parameters.Add(":createdBy", model.CreatedBy);

                        scopeCmd.ExecuteNonQuery();
                    }
                }

                return Ok("Saved Successfully");
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("getauditplans")]
        public IHttpActionResult GetAuditPlans()
        {
            try
            {
                List<object> auditPlans = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT a.audit_id,
       d.dep_name,
       a.financial_period,
       e.emp_name,
       a.status,
       s.scope_text,
       a.start_date,
       a.end_date,
       a.completion_date
FROM MAAF_INT_HOAUDIT_PLAN a
LEFT JOIN DEPARTMENT_MST d
  ON a.department_id = d.dep_id
LEFT JOIN EMPLOYEE_MASTER e
  ON a.auditor_id = e.emp_code
LEFT JOIN (
    SELECT audit_id, scope_text
    FROM MAAF_INT_HOAUDIT_SCOPE
    WHERE ROWID IN (
        SELECT MAX(ROWID)
        FROM MAAF_INT_HOAUDIT_SCOPE
        GROUP BY audit_id
    )
) s
  ON a.audit_id = s.audit_id
WHERE a.status IN (
      'Planned',
      'Scope Sent for Approval',
      'Scope Rejected',
      'Scope Approved',
      'Response Pending',
      'Response Sent – Audit Verification Pending',
      'Response Rejected',
      'Resolved',
      'Audit Head Rejected',
      'Closed'
)
ORDER BY a.audit_id DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            auditPlans.Add(new
                            {
                                AuditId = dr["AUDIT_ID"].ToString(),
                                Department = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                Period = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                Auditor = dr["EMP_NAME"] == DBNull.Value ? "" : dr["EMP_NAME"].ToString(),
                                Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),
                                StartDate = dr["START_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["START_DATE"]).ToString("dd-MMM-yyyy"),
                                EndDate = dr["END_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["END_DATE"]).ToString("dd-MMM-yyyy"),
                                CompletionDate = dr["COMPLETION_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["COMPLETION_DATE"]).ToString("dd-MMM-yyyy"),
                                Scope = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString()
                            });
                        }
                    }
                }

                return Ok(auditPlans);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("getauditplansbyperiod")]
        public IHttpActionResult GetAuditPlansByPeriod(string fromPeriod, string toPeriod)
        {
            try
            {
                List<object> auditPlans = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT a.audit_id,
       d.dep_name,
       a.financial_period,
       e.emp_name,
       a.status,
       s.scope_text,
       a.start_date,
       a.end_date,
       a.completion_date
FROM MAAF_INT_HOAUDIT_PLAN a
LEFT JOIN DEPARTMENT_MST d
  ON a.department_id = d.dep_id
LEFT JOIN EMPLOYEE_MASTER e
  ON a.auditor_id = e.emp_code
LEFT JOIN (
    SELECT audit_id, scope_text
    FROM MAAF_INT_HOAUDIT_SCOPE
    WHERE ROWID IN (
        SELECT MAX(ROWID)
        FROM MAAF_INT_HOAUDIT_SCOPE
        GROUP BY audit_id
    )
) s
  ON a.audit_id = s.audit_id
WHERE TO_DATE(a.financial_period, 'MON-RR')
      BETWEEN TO_DATE(:fromPeriod, 'MON-RR')
          AND TO_DATE(:toPeriod, 'MON-RR')
  AND a.status IN (
        'Planned',
        'Scope Rejected',
        'Scope Sent for Approval',
        'Scope Checked',
        'Scope Approved',
        'Response Pending',
        'Response Sent – Audit Verification Pending',
        'Response Rejected',
        'Resolved',
        'Audit Head Rejected',
        'Closed'
  )
ORDER BY a.audit_id DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":fromPeriod", OracleDbType.Varchar2).Value = fromPeriod;
                        cmd.Parameters.Add(":toPeriod", OracleDbType.Varchar2).Value = toPeriod;

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                auditPlans.Add(new
                                {
                                    AuditId = dr["AUDIT_ID"].ToString(),
                                    Department = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                    Period = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                    Auditor = dr["EMP_NAME"] == DBNull.Value ? "" : dr["EMP_NAME"].ToString(),
                                    Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),
                                    StartDate = dr["START_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["START_DATE"]).ToString("dd-MMM-yyyy"),
                                    EndDate = dr["END_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["END_DATE"]).ToString("dd-MMM-yyyy"),
                                    CompletionDate = dr["COMPLETION_DATE"] == DBNull.Value ? "" : Convert.ToDateTime(dr["COMPLETION_DATE"]).ToString("dd-MMM-yyyy"),
                                    Scope = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(auditPlans);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("getdashboardsummary")]
        public IHttpActionResult GetDashboardSummary()
        {
            try
            {
                int closedCount = 0;
                int plannedCount = 0;

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                SELECT 
                    SUM(CASE WHEN STATUS = 'Closed' THEN 1 ELSE 0 END) AS CLOSED_COUNT,
                    SUM(CASE WHEN STATUS <> 'Closed' THEN 1 ELSE 0 END) AS PLANNED_COUNT
                FROM MAAF_INT_HOAUDIT_PLAN";

                    OracleCommand cmd = new OracleCommand(query, con);
                    OracleDataReader dr = cmd.ExecuteReader();

                    if (dr.Read())
                    {
                        closedCount = dr["CLOSED_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(dr["CLOSED_COUNT"]);
                        plannedCount = dr["PLANNED_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(dr["PLANNED_COUNT"]);
                    }
                }

                int total = closedCount + plannedCount;

                double completionPercentage = 0;

                if (total > 0)
                {
                    completionPercentage = (double)closedCount / total * 100;
                }

                return Ok(new
                {
                    Planned = plannedCount,
                    Completed = closedCount,
                    CompletionPercentage = Math.Round(completionPercentage, 2)
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("getactiveaudits")]
        public IHttpActionResult GetActiveAudits()
        {
            try
            {
                List<object> activeAudits = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                SELECT a.audit_id,
                       d.dep_name,
                       a.financial_period,
                       a.status
                FROM MAAF_INT_HOAUDIT_PLAN a
                JOIN DEPARTMENT_MST d
                  ON a.department_id = d.dep_id
                WHERE a.status IN (
                      'Scope Approved',
                      'Response Pending',
                      'Response Sent – Audit Verification Pending',
                      'Resolved'
                )
                ORDER BY a.audit_id desc";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            activeAudits.Add(new
                            {
                                AuditId = dr["AUDIT_ID"].ToString(),
                                Department = dr["DEP_NAME"].ToString(),
                                Period = dr["FINANCIAL_PERIOD"].ToString(),
                                Status = dr["STATUS"].ToString()
                            });
                        }
                    }
                }

                return Ok(activeAudits);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("getMyScopeAudits")]
        public IHttpActionResult GetMyScopeAudits(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return BadRequest("empCode is required.");

            try
            {
                List<object> auditList = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT 
    a.AUDIT_ID,
    d.DEP_NAME,
    a.FINANCIAL_PERIOD,
    a.STATUS,
    s.SCOPE_TEXT,
    s.REJECTION_REMARK,
    a.START_DATE,
    a.END_DATE,
    a.COMPLETION_DATE
FROM MAAF_INT_HOAUDIT_PLAN a
LEFT JOIN DEPARTMENT_MST d
    ON a.DEPARTMENT_ID = d.DEP_ID
LEFT JOIN MAAF_INT_HOAUDIT_SCOPE s
    ON a.AUDIT_ID = s.AUDIT_ID
WHERE TRIM(a.AUDITOR_ID) = TRIM(:empCode)
  AND TRIM(UPPER(a.STATUS)) = 'PLANNED'
ORDER BY a.AUDIT_ID DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":empCode", OracleDbType.Varchar2).Value = empCode;

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                auditList.Add(new
                                {
                                    AuditId = dr["AUDIT_ID"].ToString(),
                                    DepartmentName = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                    FinancialPeriod = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                    Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),
                                    ScopeText = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString(),
                                    RejectionRemark = dr["REJECTION_REMARK"] == DBNull.Value ? "" : dr["REJECTION_REMARK"].ToString(),

                                    StartDate = dr["START_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["START_DATE"]).ToString("dd-MMM-yyyy"),

                                    EndDate = dr["END_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["END_DATE"]).ToString("dd-MMM-yyyy"),

                                    CompletionDate = dr["COMPLETION_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["COMPLETION_DATE"]).ToString("dd-MMM-yyyy")
                                });
                            }
                        }
                    }
                }

                return Ok(auditList);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }



        [HttpPost]
        [Route("saveScope")]
        public IHttpActionResult SaveScope(ScopeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.AuditId))
                return BadRequest("AuditId is required");

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                using (OracleTransaction transaction = con.BeginTransaction())
                {
                    try
                    {
                        string checkQuery = @"SELECT COUNT(*) 
                  FROM MAAF_INT_HOAUDIT_SCOPE 
                  WHERE AUDIT_ID = :auditId";

                        int count = 0;

                        using (OracleCommand checkCmd = new OracleCommand(checkQuery, con))
                        {
                            checkCmd.Transaction = transaction;
                            checkCmd.BindByName = true;
                            checkCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                            count = Convert.ToInt32(checkCmd.ExecuteScalar());
                        }

                        if (count > 0)
                        {
                            // ✅ If scope text is empty, keep existing scope text
                            string updateQuery = @"
UPDATE MAAF_INT_HOAUDIT_SCOPE
SET SCOPE_TEXT = NVL(:scopeText, SCOPE_TEXT),
    UPDATED_DATE = SYSDATE,
    STATUS = 'Draft',
    APPROVAL_LEVEL = 0,
    REJECTION_REMARK = NULL
WHERE AUDIT_ID = :auditId";

                            using (OracleCommand updateCmd = new OracleCommand(updateQuery, con))
                            {
                                updateCmd.Transaction = transaction;
                                updateCmd.BindByName = true;

                                updateCmd.Parameters.Add(":scopeText", OracleDbType.Clob).Value =
                                    string.IsNullOrWhiteSpace(model.ScopeText) ? (object)DBNull.Value : model.ScopeText;

                                updateCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                                updateCmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // ✅ Insert new scope
                            string insertQuery = @"
INSERT INTO MAAF_INT_HOAUDIT_SCOPE
(SCOPE_ID, AUDIT_ID, SCOPE_TEXT, STATUS, APPROVAL_LEVEL, CREATED_BY, CREATED_DATE)
VALUES
(SEQ_MAAF_INT_HOAUDIT_SCOPE.NEXTVAL,
 :auditId,
 :scopeText,
 'Draft',
 0,
 :createdBy,
 SYSDATE)";

                            using (OracleCommand insertCmd = new OracleCommand(insertQuery, con))
                            {
                                insertCmd.Transaction = transaction;
                                insertCmd.BindByName = true;

                                insertCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                                insertCmd.Parameters.Add(":scopeText", OracleDbType.Clob).Value =
                                    string.IsNullOrWhiteSpace(model.ScopeText) ? (object)DBNull.Value : model.ScopeText;

                                insertCmd.Parameters.Add(":createdBy", OracleDbType.Varchar2).Value =
                                    string.IsNullOrEmpty(model.EmpCode) ? "SYSTEM" : model.EmpCode;

                                insertCmd.ExecuteNonQuery();
                            }
                        }

                        // ✅ Update plan table also to Draft
                        string updatePlanQuery = @"
UPDATE MAAF_INT_HOAUDIT_PLAN
SET STATUS = 'Draft'
WHERE AUDIT_ID = :auditId";

                        using (OracleCommand planCmd = new OracleCommand(updatePlanQuery, con))
                        {
                            planCmd.Transaction = transaction;
                            planCmd.BindByName = true;
                            planCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                            planCmd.ExecuteNonQuery();
                        }

                        // ✅ Save checklist
                        if (model.Checklist != null && model.Checklist.Count > 0)
                        {
                            string deleteQuery = "DELETE FROM MAAF_INT_HOAUDIT_CHECKLIST WHERE AUDIT_ID = :auditId";

                            using (OracleCommand delCmd = new OracleCommand(deleteQuery, con))
                            {
                                delCmd.Transaction = transaction;
                                delCmd.BindByName = true;
                                delCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;
                                delCmd.ExecuteNonQuery();
                            }

                            foreach (var item in model.Checklist)
                            {
                                string insertChecklist = @"
INSERT INTO MAAF_INT_HOAUDIT_CHECKLIST
(CHECKLIST_ID, AUDIT_ID, SL_NO, CHECKLIST_ITEM, ANSWER, REMARKS)
VALUES
(SEQ_CHECKLIST.NEXTVAL, :auditId, :slNo, :item, :answer, :remarks)";

                                using (OracleCommand cmd = new OracleCommand(insertChecklist, con))
                                {
                                    cmd.Transaction = transaction;
                                    cmd.BindByName = true;

                                    cmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;
                                    cmd.Parameters.Add(":slNo", OracleDbType.Int32).Value = item.SlNo;
                                    cmd.Parameters.Add(":item", OracleDbType.Varchar2).Value = item.ChecklistItem;
                                    cmd.Parameters.Add(":answer", OracleDbType.Varchar2).Value = item.Answer ?? "";
                                    cmd.Parameters.Add(":remarks", OracleDbType.Varchar2).Value = item.Remarks ?? "";

                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();

                        return Ok("Scope Saved Successfully");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return InternalServerError(ex);
                    }
                }
            }
        }
        [HttpGet]
        [Route("getScopesForApproval")]
        public IHttpActionResult GetScopesForApproval(string role, string empCode = "")
        {
            if (string.IsNullOrEmpty(role))
                return BadRequest("Role is required");

            List<object> scopeList = new List<object>();

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                string query = "";

                if (role.Equals("Auditor", StringComparison.OrdinalIgnoreCase))
                {
                    query = @"
SELECT s.AUDIT_ID,
       s.SCOPE_TEXT,
       p.FINANCIAL_PERIOD,
       d.DEP_NAME,
       TO_CHAR(p.START_DATE,'DD-MON-YYYY') AS START_DATE,
       TO_CHAR(p.END_DATE,'DD-MON-YYYY') AS END_DATE,
       TO_CHAR(p.COMPLETION_DATE,'DD-MON-YYYY') AS COMPLETION_DATE,
       p.STATUS
FROM MAAF_INT_HOAUDIT_SCOPE s
JOIN MAAF_INT_HOAUDIT_PLAN p 
    ON s.AUDIT_ID = p.AUDIT_ID
JOIN DEPARTMENT_MST d 
    ON p.DEPARTMENT_ID = d.DEP_ID
WHERE TRIM(p.AUDITOR_ID) = TRIM(:empCode)
  AND TRIM(TO_CHAR(s.APPROVAL_LEVEL)) = '0'
ORDER BY s.CREATED_DATE DESC";
                }
                else if (role.Equals("AuditHead", StringComparison.OrdinalIgnoreCase))
                {
                    query = @"
SELECT s.AUDIT_ID,
       s.SCOPE_TEXT,
       p.FINANCIAL_PERIOD,
       d.DEP_NAME,
       TO_CHAR(p.START_DATE,'DD-MON-YYYY') AS START_DATE,
TO_CHAR(p.END_DATE,'DD-MON-YYYY') AS END_DATE,
TO_CHAR(p.COMPLETION_DATE,'DD-MON-YYYY') AS COMPLETION_DATE,
       p.STATUS
FROM MAAF_INT_HOAUDIT_SCOPE s
JOIN MAAF_INT_HOAUDIT_PLAN p 
    ON s.AUDIT_ID = p.AUDIT_ID
JOIN DEPARTMENT_MST d 
    ON p.DEPARTMENT_ID = d.DEP_ID
WHERE TRIM(TO_CHAR(s.APPROVAL_LEVEL)) = '2'
ORDER BY s.CREATED_DATE DESC";
                }
                else
                {
                    return BadRequest("Invalid role");
                }

                using (OracleCommand cmd = new OracleCommand(query, con))
                {
                    cmd.BindByName = true;

                    if (role.Equals("Auditor", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.Parameters.Add(":empCode", OracleDbType.Varchar2).Value = empCode ?? "";
                    }

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            scopeList.Add(new
                            {
                                AuditId = dr["AUDIT_ID"].ToString(),
                                ScopeText = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString(),
                                Period = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                Department = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),

                                StartDate = dr["START_DATE"].ToString(),
                                EndDate = dr["END_DATE"].ToString(),
                                CompletionDate = dr["COMPLETION_DATE"].ToString()
                            });
                        }
                    }
                }
            }

            return Ok(scopeList);
        }
        [HttpPost]
        [Route("approveScope")]
        public IHttpActionResult ApproveScope(ScopeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.AuditId))
                return BadRequest("Invalid data");

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                using (OracleTransaction transaction = con.BeginTransaction())
                {
                    try
                    {
                        string updateScope = @"
                UPDATE MAAF_INT_HOAUDIT_SCOPE
                SET STATUS = 'Scope Approved',
                    APPROVAL_LEVEL = 3,
                    UPDATED_DATE = SYSDATE
                WHERE AUDIT_ID = :auditId";

                        OracleCommand scopeCmd = new OracleCommand(updateScope, con);
                        scopeCmd.Transaction = transaction;
                        scopeCmd.BindByName = true;
                        scopeCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                        scopeCmd.ExecuteNonQuery();


                        string updatePlan = @"
                UPDATE MAAF_INT_HOAUDIT_PLAN
                SET STATUS = 'Scope Approved'
                WHERE AUDIT_ID = :auditId";

                        OracleCommand planCmd = new OracleCommand(updatePlan, con);
                        planCmd.Transaction = transaction;
                        planCmd.BindByName = true;
                        planCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                        planCmd.ExecuteNonQuery();


                        transaction.Commit();

                        return Ok("Scope Approved Successfully");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return InternalServerError(ex);
                    }
                }
            }
        }
        [HttpPost]
        [Route("rejectScope")]
        public IHttpActionResult RejectScope(ScopeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.AuditId))
                return BadRequest("Invalid data");

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                using (OracleTransaction transaction = con.BeginTransaction())
                {
                    try
                    {
                        string updateScope = @"
        UPDATE MAAF_INT_HOAUDIT_SCOPE
        SET STATUS = 'Scope Rejected',
            APPROVAL_LEVEL = 3,
            REJECTION_REMARK = :remark,
            UPDATED_DATE = SYSDATE
        WHERE AUDIT_ID = :auditId";

                        OracleCommand scopeCmd = new OracleCommand(updateScope, con);
                        scopeCmd.Transaction = transaction;
                        scopeCmd.BindByName = true;
                        scopeCmd.Parameters.Add(":remark", OracleDbType.Varchar2).Value = model.RejectionRemark;
                        scopeCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                        scopeCmd.ExecuteNonQuery();

                        string updatePlan = @"
        UPDATE MAAF_INT_HOAUDIT_PLAN
        SET STATUS = 'Scope Rejected'
        WHERE AUDIT_ID = :auditId";

                        OracleCommand planCmd = new OracleCommand(updatePlan, con);
                        planCmd.Transaction = transaction;
                        planCmd.BindByName = true;
                        planCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;

                        planCmd.ExecuteNonQuery();

                        transaction.Commit();

                        return Ok("Scope Rejected Successfully");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return InternalServerError(ex);
                    }
                }
            }
        }
        [HttpPost]
        [Route("submitScopeToHead")]
        public IHttpActionResult SubmitScopeToHead(ScopeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.AuditId))
                return BadRequest("AuditId is required");

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                using (OracleTransaction transaction = con.BeginTransaction())
                {
                    try
                    {
                        string auditId = model.AuditId.Trim();

                        // Check if scope exists
                        string checkQuery = @"
            SELECT COUNT(*)
            FROM MAAF_INT_HOAUDIT_SCOPE
            WHERE AUDIT_ID = :auditId";

                        int count = 0;

                        using (OracleCommand checkCmd = new OracleCommand(checkQuery, con))
                        {
                            checkCmd.Transaction = transaction;
                            checkCmd.BindByName = true;
                            checkCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = auditId;

                            count = Convert.ToInt32(checkCmd.ExecuteScalar());
                        }

                        if (count == 0)
                        {
                            transaction.Rollback();
                            return BadRequest("Please save scope before submitting for approval.");
                        }

                        // ✅ Allow submit only if scope is in Draft / Recheck stage
                        string updateScopeQuery = @"
            UPDATE MAAF_INT_HOAUDIT_SCOPE
            SET STATUS = 'Scope Sent for Approval',
                UPDATED_DATE = SYSDATE,
                APPROVAL_LEVEL = 2
            WHERE AUDIT_ID = :auditId
              AND APPROVAL_LEVEL IN (0, 1)";

                        using (OracleCommand scopeCmd = new OracleCommand(updateScopeQuery, con))
                        {
                            scopeCmd.Transaction = transaction;
                            scopeCmd.BindByName = true;
                            scopeCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = auditId;

                            int rows = scopeCmd.ExecuteNonQuery();

                            if (rows == 0)
                            {
                                transaction.Rollback();
                                return BadRequest("Scope is not in a valid stage for submission.");
                            }
                        }

                        // Update Plan Table
                        string updatePlanQuery = @"
            UPDATE MAAF_INT_HOAUDIT_PLAN
            SET STATUS = 'Scope Sent for Approval'
            WHERE AUDIT_ID = :auditId";

                        using (OracleCommand planCmd = new OracleCommand(updatePlanQuery, con))
                        {
                            planCmd.Transaction = transaction;
                            planCmd.BindByName = true;
                            planCmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = auditId;

                            planCmd.ExecuteNonQuery();
                        }

                        transaction.Commit();

                        return Ok("Scope submitted to Audit Head successfully.");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return InternalServerError(ex);
                    }
                }
            }
        }
        [HttpPost]
        [Route("checkScope")]
        public IHttpActionResult CheckScope(ScopeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.AuditId))
                return BadRequest("AuditId is required");

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                using (OracleTransaction transaction = con.BeginTransaction())
                {
                    try
                    {
                        string scopeQuery = @"
UPDATE MAAF_INT_HOAUDIT_SCOPE
SET STATUS = 'Scope Checked',
    APPROVAL_LEVEL = 1,
    UPDATED_DATE = SYSDATE
WHERE AUDIT_ID = :auditId";

                        using (OracleCommand cmd = new OracleCommand(scopeQuery, con))
                        {
                            cmd.Transaction = transaction;
                            cmd.BindByName = true;
                            cmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;
                            cmd.ExecuteNonQuery();
                        }

                        string planQuery = @"
UPDATE MAAF_INT_HOAUDIT_PLAN
SET STATUS = 'Scope Checked'
WHERE AUDIT_ID = :auditId";

                        using (OracleCommand cmd2 = new OracleCommand(planQuery, con))
                        {
                            cmd2.Transaction = transaction;
                            cmd2.BindByName = true;
                            cmd2.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = model.AuditId;
                            cmd2.ExecuteNonQuery();
                        }

                        transaction.Commit();

                        return Ok("Scope Checked Successfully");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return InternalServerError(ex);
                    }
                }
            }
        }

        [HttpPost]
        [Route("auditpolicy/upload")]
        public async Task<IHttpActionResult> UploadPolicy()
        {
            try
            {
                var httpRequest = HttpContext.Current.Request;

                if (httpRequest.Files.Count == 0)
                    return BadRequest("No file uploaded.");

                var file = httpRequest.Files[0];

                if (file == null || file.ContentLength == 0)
                    return BadRequest("Invalid file.");

                // Limit file size (5MB)
                if (file.ContentLength > 5 * 1024 * 1024)
                    return BadRequest("File size must be less than 5MB.");

                string extension = Path.GetExtension(file.FileName);

                // Only allow PDF
                if (string.IsNullOrEmpty(extension) || extension.ToLower() != ".pdf")
                    return BadRequest("Only PDF files are allowed.");

                string folderPath = HttpContext.Current.Server.MapPath("~/uploads/policy/");

                // Create folder if it doesn't exist
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // Unique file name
                string newFileName = "AuditPolicy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + extension;

                string fullPath = Path.Combine(folderPath, newFileName);

                // Save file
                file.SaveAs(fullPath);

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    string query = @"INSERT INTO MAAF_INT_AUDIT_POLICY
                             (POLICY_ID, FILE_NAME, FILE_PATH, UPLOADED_DATE)
                             VALUES
                             (MAAF_INT_AUDIT_POLICY_SEQ.NEXTVAL, :fileName, :filePath, SYSDATE)";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add(":fileName", OracleDbType.Varchar2).Value = newFileName;
                        cmd.Parameters.Add(":filePath", OracleDbType.Varchar2).Value = "/uploads/policy/" + newFileName;

                        con.Open();
                        cmd.ExecuteNonQuery();
                    }
                }

                await Task.CompletedTask;

                return Ok("Policy Uploaded Successfully");
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("auditpolicy/list")]
        public IHttpActionResult GetPolicies()
        {
            try
            {
                List<object> policies = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    string query = @"SELECT FILE_NAME,
                                    FILE_PATH,
                                    UPLOADED_DATE
                             FROM MAAF_INT_AUDIT_POLICY
                             ORDER BY UPLOADED_DATE DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        con.Open();

                        using (OracleDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                policies.Add(new
                                {
                                    FileName = reader["FILE_NAME"].ToString(),
                                    FilePath = reader["FILE_PATH"].ToString(),
                                    UploadedDate = Convert.ToDateTime(reader["UPLOADED_DATE"])
                                });
                            }
                        }
                    }
                }

                return Ok(policies);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpOptions]
        [Route("{*path}")]
        public IHttpActionResult Options()
        {
            return Ok();
        }


        [HttpGet]
        [Route("getChecklist")]
        public IHttpActionResult GetChecklist()
        {
            List<object> checklist = new List<object>();

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                string query = "SELECT SL_NO, CHECKLIST_ITEM FROM MAAF_INT_CHECKLIST_MASTER ORDER BY SL_NO";

                using (OracleCommand cmd = new OracleCommand(query, con))
                using (OracleDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        checklist.Add(new
                        {
                            SlNo = dr["SL_NO"],
                            Item = dr["CHECKLIST_ITEM"].ToString()
                        });
                    }
                }
            }

            return Ok(checklist);
        }
        [HttpGet]
        [Route("getrecheckaudits")]
        public IHttpActionResult GetRecheckAudits(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return BadRequest("empCode is required.");

            try
            {
                List<object> auditPlans = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT a.audit_id,
       d.dep_name,
       a.financial_period,
       e.emp_name,
       a.status,
       s.scope_text,
       a.start_date,
       a.end_date,
       a.completion_date
FROM MAAF_INT_HOAUDIT_PLAN a
LEFT JOIN DEPARTMENT_MST d
  ON a.department_id = d.dep_id
LEFT JOIN EMPLOYEE_MASTER e
  ON a.auditor_id = e.emp_code
LEFT JOIN MAAF_INT_HOAUDIT_SCOPE s
  ON a.audit_id = s.audit_id
WHERE TRIM(a.AUDITOR_ID) = TRIM(:empCode)
  AND TRIM(UPPER(a.status)) = 'DRAFT'
ORDER BY a.audit_id DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":empCode", OracleDbType.Varchar2).Value = empCode;

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                auditPlans.Add(new
                                {
                                    AuditId = dr["AUDIT_ID"].ToString(),
                                    Department = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                    Period = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                    Auditor = dr["EMP_NAME"] == DBNull.Value ? "" : dr["EMP_NAME"].ToString(),
                                    Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),

                                    StartDate = dr["START_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["START_DATE"]).ToString("dd-MMM-yyyy"),

                                    EndDate = dr["END_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["END_DATE"]).ToString("dd-MMM-yyyy"),

                                    CompletionDate = dr["COMPLETION_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["COMPLETION_DATE"]).ToString("dd-MMM-yyyy"),

                                    Scope = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(auditPlans);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpPost]
        [Route("submitforapproval")]
        public IHttpActionResult SubmitForApproval([FromBody] dynamic data)
        {
            try
            {
                string auditId = data.auditId;

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
UPDATE MAAF_INT_HOAUDIT_PLAN
SET STATUS = 'Scope Sent for Approval'
WHERE AUDIT_ID = :auditId";

                    OracleCommand cmd = new OracleCommand(query, con);
                    cmd.Parameters.Add(":auditId", auditId);
                    cmd.ExecuteNonQuery();
                }

                return Ok("Submitted Successfully");
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpPost]
        [Route("rejectaudit")]
        public IHttpActionResult RejectAudit([FromBody] dynamic data)
        {
            try
            {
                string auditId = data.auditId;

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
UPDATE MAAF_INT_HOAUDIT_PLAN
SET STATUS = 'Scope Rejected'
WHERE AUDIT_ID = :auditId";

                    OracleCommand cmd = new OracleCommand(query, con);
                    cmd.Parameters.Add(":auditId", auditId);
                    cmd.ExecuteNonQuery();
                }

                return Ok("Rejected Successfully");
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("getSavedChecklist")]
        public IHttpActionResult GetSavedChecklist(string auditId)
        {
            try
            {
                List<object> checklist = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT SL_NO, CHECKLIST_ITEM, ANSWER, REMARKS
FROM MAAF_INT_HOAUDIT_CHECKLIST
WHERE AUDIT_ID = :auditId
ORDER BY SL_NO";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":auditId", OracleDbType.Varchar2).Value = auditId;

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {

                                string answer = dr["ANSWER"] == DBNull.Value
                                    ? ""
                                    : dr["ANSWER"].ToString().Trim().ToUpper();

                                if (answer == "N/A")
                                    answer = "NA";

                                checklist.Add(new
                                {
                                    SlNo = dr["SL_NO"],
                                    Item = dr["CHECKLIST_ITEM"].ToString(),
                                    Answer = answer,
                                    Remarks = dr["REMARKS"] == DBNull.Value ? "" : dr["REMARKS"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(checklist);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("getRejectedScopes")]
        public IHttpActionResult GetRejectedScopes(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return BadRequest("empCode is required.");

            try
            {
                List<object> rejectedScopes = new List<object>();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT a.audit_id,
       d.dep_name,
       a.financial_period,
       e.emp_name,
       a.status,
       s.scope_text,
       s.rejection_remark,
       a.start_date,
       a.end_date,
       a.completion_date
FROM MAAF_INT_HOAUDIT_PLAN a
LEFT JOIN DEPARTMENT_MST d
  ON a.department_id = d.dep_id
LEFT JOIN EMPLOYEE_MASTER e
  ON a.auditor_id = e.emp_code
LEFT JOIN MAAF_INT_HOAUDIT_SCOPE s
  ON a.audit_id = s.audit_id
WHERE TRIM(a.AUDITOR_ID) = TRIM(:empCode)
  AND TRIM(UPPER(a.status)) = 'SCOPE REJECTED'
ORDER BY a.audit_id DESC";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":empCode", OracleDbType.Varchar2).Value = empCode;

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                rejectedScopes.Add(new
                                {
                                    AuditId = dr["AUDIT_ID"].ToString(),
                                    Department = dr["DEP_NAME"] == DBNull.Value ? "" : dr["DEP_NAME"].ToString(),
                                    Period = dr["FINANCIAL_PERIOD"] == DBNull.Value ? "" : dr["FINANCIAL_PERIOD"].ToString(),
                                    Auditor = dr["EMP_NAME"] == DBNull.Value ? "" : dr["EMP_NAME"].ToString(),
                                    Status = dr["STATUS"] == DBNull.Value ? "" : dr["STATUS"].ToString(),
                                    Scope = dr["SCOPE_TEXT"] == DBNull.Value ? "" : dr["SCOPE_TEXT"].ToString(),
                                    RejectionRemark = dr["REJECTION_REMARK"] == DBNull.Value ? "" : dr["REJECTION_REMARK"].ToString(),

                                    StartDate = dr["START_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["START_DATE"]).ToString("dd-MMM-yyyy"),

                                    EndDate = dr["END_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["END_DATE"]).ToString("dd-MMM-yyyy"),

                                    CompletionDate = dr["COMPLETION_DATE"] == DBNull.Value
                                        ? ""
                                        : Convert.ToDateTime(dr["COMPLETION_DATE"]).ToString("dd-MMM-yyyy")
                                });
                            }
                        }
                    }
                }

                return Ok(rejectedScopes);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("getalertauditdrill")]
        public IHttpActionResult GetAlertAuditDrill(int idtype, string fdate, string tdate, int firm_id, string id = "")
        {
            try
            {
                DataTable dt = new DataTable();
                string query = "";

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    if ((idtype == 2 || idtype == 3 || idtype == 4) && string.IsNullOrEmpty(id))
                    {
                        return BadRequest("ID is required for this drill level.");
                    }

                    // ==========================
                    // IDTYPE = 1 → REGION LEVEL
                    // ==========================
                    if (idtype == 1)
                    {
                        query = @"
SELECT 
    b.REG_ID,
    b.REG_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.REG_NAME NOT IN ('THRISSUR REGION', 'MAAFIN KERALA REGION')

GROUP BY b.REG_ID, b.REG_NAME
ORDER BY b.REG_NAME";
                    }

                    // ==========================
                    // IDTYPE = 2 → AREA LEVEL
                    // ==========================
                    else if (idtype == 2)
                    {
                        query = @"
SELECT 
    b.AREA_ID,
    b.AREA_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.REG_ID = :id

GROUP BY b.AREA_ID, b.AREA_NAME
ORDER BY b.AREA_NAME";
                    }

                    // ==========================
                    // IDTYPE = 3 → BRANCH LEVEL
                    // ==========================
                    else if (idtype == 3)
                    {
                        query = @"
SELECT 
    b.BRANCH_ID,
    b.BRANCH_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.AREA_ID = :id

GROUP BY b.BRANCH_ID, b.BRANCH_NAME
ORDER BY b.BRANCH_NAME";
                    }

                    // ==========================
                    // IDTYPE = 4 → DETAIL LEVEL
                    // ==========================
                    else if (idtype == 4)
                    {
                        query = @"
SELECT 
    t.alert_id AS ALERT_ID,
    t.branch_id AS BRANCH_ID,
    NVL(t.pledge_no, 0) AS PLEDGE_NO,
    t.send_dt AS SEND_DATE,
    (TRUNC(TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')) - TRUNC(t.send_dt)) AS DAYS,
    'PENDING FOR REPLY' AS STATUS
FROM masset.risk_master_inventory_base t
JOIN branch_master c ON t.branch_id = c.branch_id
WHERE t.branch_id = :id
  AND c.firm_id = :firm_id
  AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
  AND (
        (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
        OR (
            (t.reply_date IS NULL AND t.reply_received_date IS NULL)
            OR (
                t.reply_date IS NULL 
                AND t.reply_received_date IS NOT NULL
                AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            )
        )
      )
  AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
ORDER BY t.send_dt DESC";
                    }
                    else
                    {
                        return BadRequest("Invalid idtype.");
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        if (idtype == 2 || idtype == 3 || idtype == 4)
                        {
                            cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = id;
                        }

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }


        [HttpGet]
        [Route("getpreviousmonthalertauditdrill")]
        public IHttpActionResult GetPreviousMonthAlertAuditDrill(int idtype, string fdate, string tdate, int firm_id, string id = "")
        {
            try
            {
                DataTable dt = new DataTable();
                string query = "";

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    if ((idtype == 2 || idtype == 3 || idtype == 4) && string.IsNullOrEmpty(id))
                    {
                        return BadRequest("ID is required for this drill level.");
                    }

                    // ==========================
                    // IDTYPE = 1 → REGION LEVEL
                    // ==========================
                    if (idtype == 1)
                    {
                        query = @"
SELECT 
    b.REG_ID,
    b.REG_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.REG_NAME NOT IN ('THRISSUR REGION', 'MAAFIN KERALA REGION')

GROUP BY b.REG_ID, b.REG_NAME
ORDER BY b.REG_NAME";
                    }

                    // ==========================
                    // IDTYPE = 2 → AREA LEVEL
                    // ==========================
                    else if (idtype == 2)
                    {
                        query = @"
SELECT 
    b.AREA_ID,
    b.AREA_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.REG_ID = :id

GROUP BY b.AREA_ID, b.AREA_NAME
ORDER BY b.AREA_NAME";
                    }

                    // ==========================
                    // IDTYPE = 3 → BRANCH LEVEL
                    // ==========================
                    else if (idtype == 3)
                    {
                        query = @"
SELECT 
    b.BRANCH_ID,
    b.BRANCH_NAME,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) < TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL 
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        )
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS OPENING,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS SEND,

    SUM(
        CASE 
            WHEN (
                    (
                        t.reply_date IS NOT NULL
                        AND TRUNC(t.reply_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                    OR (
                        t.reply_date IS NULL 
                        AND t.reply_received_date IS NOT NULL
                        AND TRUNC(t.reply_received_date) >= TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                        AND TRUNC(t.reply_received_date) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                    )
                 )
            THEN 1 ELSE 0
        END
    ) AS ATTENDED,

    SUM(
        CASE 
            WHEN t.send_dt IS NOT NULL
             AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
             AND (
                    (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
                    OR (
                        (t.reply_date IS NULL AND t.reply_received_date IS NULL)
                        OR (
                            t.reply_date IS NULL
                            AND t.reply_received_date IS NOT NULL
                            AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
                        )
                    )
                 )
             AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            THEN 1 ELSE 0
        END
    ) AS PENDING

FROM branch_detail b
JOIN branch_master c ON b.branch_id = c.branch_id
LEFT JOIN masset.risk_master_inventory_base t ON t.branch_id = b.branch_id

WHERE c.firm_id = :firm_id
  AND b.AREA_ID = :id

GROUP BY b.BRANCH_ID, b.BRANCH_NAME
ORDER BY b.BRANCH_NAME";
                    }

                    // ==========================
                    // IDTYPE = 4 → DETAIL LEVEL
                    // ==========================
                    else if (idtype == 4)
                    {
                        query = @"
SELECT 
    t.alert_id AS ALERT_ID,
    t.branch_id AS BRANCH_ID,
    NVL(t.pledge_no, 0) AS PLEDGE_NO,
    t.send_dt AS SEND_DT,
    (TRUNC(TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')) - TRUNC(t.send_dt)) AS DAYS,
    'PENDING FOR REPLY' AS STATUS
FROM masset.risk_master_inventory_base t
JOIN branch_master c ON t.branch_id = c.branch_id
WHERE t.branch_id = :id
  AND c.firm_id = :firm_id
  AND TRUNC(t.send_dt) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
  AND (
        (t.reply_date IS NOT NULL AND TRUNC(t.reply_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY'))
        OR (
            (t.reply_date IS NULL AND t.reply_received_date IS NULL)
            OR (
                t.reply_date IS NULL 
                AND t.reply_received_date IS NOT NULL
                AND TRUNC(t.reply_received_date) > TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
            )
        )
      )
  AND (TRUNC(t.send_dt) + 4) <= TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')
ORDER BY t.send_dt DESC";
                    }
                    else
                    {
                        return BadRequest("Invalid idtype.");
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        if (idtype == 2 || idtype == 3 || idtype == 4)
                        {
                            cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = id;
                        }

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
        [HttpGet]
        [Route("getgoldbranchaudit")]
        public IHttpActionResult GetGoldBranchAudit(string fdate, string tdate, int firm_id)
        {
            try
            {
                DataTable dt = new DataTable();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
SELECT 
    COUNT(*) AS TOTAL,
    SUM(CASE WHEN STATUS = 2 THEN 1 ELSE 0 END) AS COMPLETED,
    SUM(CASE WHEN STATUS = 1 THEN 1 ELSE 0 END) AS PENDING,
    SUM(CASE WHEN STATUS = 3 THEN 1 ELSE 0 END) AS DELAYED
FROM TBL_SEC_AUDITOR_MST
WHERE TRUNC(AUDIT_DT) BETWEEN TO_DATE(UPPER(:fdate), 'DD-MON-YYYY')
                          AND TO_DATE(UPPER(:tdate), 'DD-MON-YYYY')";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("getgoldauditareasummary")]
        public IHttpActionResult GetGoldAuditAreaSummary(string category, string fdate, string tdate, int firm_id)
        {
            try
            {
                DataTable dt = new DataTable();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = "";

                    // ================= FULL =================
                    if (category == "FULL")
                    {
                        query = @"
SELECT
    ROW_NUMBER() OVER (ORDER BY bd.REG_NAME) AS AREA_ID,
    bd.REG_NAME AS AREA_NAME,

    /* Exact client report grouping = Region wise */
    COUNT(DISTINCT a.BRANCH_ID) AS TOTAL_BRANCHES,

    /* Client style */
    COUNT(DISTINCT a.BRANCH_ID) AS DUE_MTM,
    COUNT(DISTINCT a.BRANCH_ID) AS DUE_MTD,

    /* Green rows */
    COUNT(DISTINCT CASE
        WHEN NVL(a.LAGDAYS,0) BETWEEN 0 AND 45
        THEN a.BRANCH_ID
    END) AS COMPLETED,

    /* No separate ongoing in client report */
    0 AS ONGOING,

    /* Yellow + Red rows */
    COUNT(DISTINCT CASE
        WHEN NVL(a.LAGDAYS,0) BETWEEN 46 AND 200
        THEN a.BRANCH_ID
    END) AS PENDING

FROM Auditlagdays a

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = a.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = a.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id
AND NVL(a.LAGDAYS,0) BETWEEN 0 AND 200

GROUP BY bd.REG_NAME
ORDER BY bd.REG_NAME";
                    }

                    // ================= RISK =================
                    else if (category == "RISK")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    /* Total branches from client lag report */
    COUNT(DISTINCT bd.BRANCH_ID) AS TOTAL_BRANCHES,

    /* Month To Date = same as report list till today */
    COUNT(DISTINCT bd.BRANCH_ID) AS DUE_MTD,

    /* Month To Month = same current month report list */
    COUNT(DISTINCT bd.BRANCH_ID) AS DUE_MTM,

    /* Completed = branch exists in pledge table with status=1 */
    COUNT(DISTINCT CASE
        WHEN apd.STATUS = 1
        THEN bd.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    /* Pending = no completed status */
    COUNT(DISTINCT CASE
        WHEN apd.STATUS IS NULL OR apd.STATUS <> 1
        THEN bd.BRANCH_ID
    END) AS PENDING

FROM Auditlagdays a

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = a.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = bd.BRANCH_ID

LEFT JOIN
(
    SELECT BRANCH_ID, MAX(STATUS) AS STATUS
    FROM audit_pledge_deduction
    GROUP BY BRANCH_ID
) apd
    ON apd.BRANCH_ID = a.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id
AND NVL(a.LAGDAYS,0) BETWEEN 0 AND 200

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    // ================= SECURITY =================
                    else if (category == "SECURITY")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    COUNT(DISTINCT d.BRANCH_ID) AS TOTAL_BRANCHES,

    0 AS DUE_MTD,

    COUNT(DISTINCT d.BRANCH_ID) AS DUE_MTM,

    COUNT(DISTINCT CASE
        WHEN d.LAG_DAYS BETWEEN 1 AND 45
        THEN d.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    COUNT(DISTINCT CASE
        WHEN d.LAG_DAYS > 45
        THEN d.BRANCH_ID
    END) AS PENDING

FROM BLUEAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    // ================= DOCUMENT =================
                    else if (category == "DOCUMENT")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    COUNT(DISTINCT d.BRANCH_ID) AS TOTAL_BRANCHES,

    0 AS DUE_MTD,

    COUNT(DISTINCT CASE
        WHEN d.LAG_DAYS > 0
        THEN d.BRANCH_ID
    END) AS DUE_MTM,

    COUNT(DISTINCT CASE
        WHEN d.AUDIT_FINISH_DT IS NOT NULL
         AND d.AUDIT_FINISH_DT <> '-'
        THEN d.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    COUNT(DISTINCT CASE
        WHEN d.AUDIT_FINISH_DT IS NULL
          OR d.AUDIT_FINISH_DT = '-'
        THEN d.BRANCH_ID
    END) AS PENDING

FROM DOCUMENTAUDITLAGDAYS d
JOIN BRANCH_DETAIL bd
ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message });
            }
        }
        [HttpGet]
        [Route("getgoldauditbranchdetails")]
        public IHttpActionResult GetGoldAuditBranchDetails(string category, string areaId, string fdate, string tdate, int firm_id)
        {
            try
            {
                DataTable dt = new DataTable();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = "";
                    string cat = (category ?? "").Trim().ToUpper();

                    // ================= FULL =================
                    if (cat == "FULL")
                    {
                        query = @"
SELECT
    bd.REG_NAME AS AREA_NAME,
    a.BRANCH_ID,
    bd.BRANCH_NAME,
    a.LASTAUDITDATE AS DUE_DATE,

    CASE
        WHEN NVL(a.LAGDAYS,0) BETWEEN 0 AND 45 THEN 'Completed'
        WHEN NVL(a.LAGDAYS,0) BETWEEN 46 AND 200 THEN 'Pending'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM Auditlagdays a

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = a.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = a.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id
AND TRIM(UPPER(bd.REG_NAME)) = TRIM(UPPER(:areaId))
AND NVL(a.LAGDAYS,0) BETWEEN 0 AND 200

ORDER BY bd.BRANCH_NAME";
                    }

                    // ================= RISK =================
                    else if (cat == "RISK")
                    {
                        query = @"
SELECT
    bd.AREA_NAME,
    bd.BRANCH_ID,
    bd.BRANCH_NAME,
    a.LASTAUDITDATE AS DUE_DATE,

    CASE
        WHEN apd.STATUS = 1 THEN 'Completed'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM BRANCH_DETAIL bd

INNER JOIN Auditlagdays a
    ON a.BRANCH_ID = bd.BRANCH_ID

INNER JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = bd.BRANCH_ID

LEFT JOIN
(
    SELECT BRANCH_ID, MAX(STATUS) STATUS
    FROM audit_pledge_deduction
    GROUP BY BRANCH_ID
) apd
ON apd.BRANCH_ID = bd.BRANCH_ID

WHERE bd.AREA_ID = TO_NUMBER(:areaId)
AND bm.FIRM_ID = :firm_id

ORDER BY bd.BRANCH_NAME";
                    }

                    // ================= SECURITY =================
                    else if (cat == "SECURITY")
                    {
                        query = @"
SELECT
    bd.AREA_NAME,
    d.BRANCH_ID,
    bd.BRANCH_NAME,
    d.BASE_DATE AS DUE_DATE,

    CASE
        WHEN d.LAG_DAYS > 45 THEN 'Pending'
        ELSE 'Completed'
    END AS STATUS_TEXT

FROM BLUEAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bd.AREA_ID = TO_NUMBER(:areaId)
AND bm.FIRM_ID = :firm_id

ORDER BY d.LAG_DAYS DESC";
                    }

                    // ================= DOCUMENT =================
                    else if (cat == "DOCUMENT")
                    {
                        query = @"
SELECT
    bd.AREA_NAME,
    d.BRANCH_ID,
    d.BRANCH_NAME,
    d.AUDIT_FINISH_DT AS DUE_DATE,

    CASE
        WHEN d.AUDIT_FINISH_DT IS NOT NULL
         AND d.AUDIT_FINISH_DT <> '-'
        THEN 'Completed'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM DOCUMENTAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bd.AREA_ID = TO_NUMBER(:areaId)
AND bm.FIRM_ID = :firm_id

ORDER BY bd.BRANCH_NAME";
                    }

                    else
                    {
                        return Ok(new
                        {
                            error = "Invalid category",
                            category = category
                        });
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("areaId", OracleDbType.Varchar2).Value = areaId;
                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message });
            }
        }
        // ======================
        // AREA SUMMARY API
        // ======================
        [HttpGet]
        [Route("getpreviousgoldauditareasummary")]
        public IHttpActionResult GetPreviousGoldAuditAreaSummary(string category, string fdate, string tdate, int firm_id)
        {
            try
            {
                DataTable dt = new DataTable();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = "";

                    // ================= FULL =================
                    if (category == "FULL")
                    {
                        query = @"
SELECT
    ROW_NUMBER() OVER (ORDER BY bd.REG_NAME) AS AREA_ID,
    bd.REG_NAME AS AREA_NAME,

    /* Total branches having previous month records */
    COUNT(DISTINCT m.BRANCH_ID) AS TOTAL_BRANCHES,

    /* Previous month screen usually no MTD */
    0 AS DUE_MTD,

    /* Due MTM = all previous month planned branches */
    COUNT(DISTINCT m.BRANCH_ID) AS DUE_MTM,

    /* Completed */
    COUNT(DISTINCT CASE
        WHEN m.STATUS = 2
        THEN m.BRANCH_ID
    END) AS COMPLETED,

    /* Ongoing */
    COUNT(DISTINCT CASE
        WHEN m.STATUS = 1
        THEN m.BRANCH_ID
    END) AS ONGOING,

    /* Pending */
    COUNT(DISTINCT CASE
        WHEN NVL(m.STATUS,0) = 0
        THEN m.BRANCH_ID
    END) AS PENDING

FROM TBL_SEC_AUDITOR_MST m

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = m.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = m.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id
AND TRUNC(m.AUDIT_DT) BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
                         AND TO_DATE(:tdate,'DD-MON-YYYY')

GROUP BY bd.REG_NAME
ORDER BY bd.REG_NAME";
                    }

                    // ================= RISK =================
                    else if (category == "RISK")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    /* Only previous month due branches */
    COUNT(DISTINCT CASE
        WHEN TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY')
             BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
             AND TO_DATE(:tdate,'DD-MON-YYYY')
        THEN bd.BRANCH_ID
    END) AS TOTAL_BRANCHES,

    0 AS DUE_MTD,

    /* Previous month due count */
    COUNT(DISTINCT CASE
        WHEN TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY')
             BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
             AND TO_DATE(:tdate,'DD-MON-YYYY')
        THEN bd.BRANCH_ID
    END) AS DUE_MTM,

    /* Completed in previous month */
    COUNT(DISTINCT CASE
        WHEN apd.STATUS = 1
        AND TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY')
             BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
             AND TO_DATE(:tdate,'DD-MON-YYYY')
        THEN bd.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    /* Pending in previous month */
    COUNT(DISTINCT CASE
        WHEN (apd.STATUS IS NULL OR apd.STATUS <> 1)
        AND TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY')
             BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
             AND TO_DATE(:tdate,'DD-MON-YYYY')
        THEN bd.BRANCH_ID
    END) AS PENDING

FROM BRANCH_DETAIL bd

JOIN Auditlagdays a
    ON a.BRANCH_ID = bd.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = bd.BRANCH_ID

LEFT JOIN
(
    SELECT BRANCH_ID, MAX(STATUS) AS STATUS
    FROM audit_pledge_deduction
    GROUP BY BRANCH_ID
) apd
    ON apd.BRANCH_ID = bd.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    // ================= SECURITY =================
                    else if (category == "SECURITY")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    COUNT(DISTINCT d.BRANCH_ID) AS TOTAL_BRANCHES,

    0 AS DUE_MTD,

    COUNT(DISTINCT d.BRANCH_ID) AS DUE_MTM,

    COUNT(DISTINCT CASE
        WHEN d.LAG_DAYS BETWEEN 1 AND 45
        THEN d.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    COUNT(DISTINCT CASE
        WHEN d.LAG_DAYS > 45
        THEN d.BRANCH_ID
    END) AS PENDING

FROM BLUEAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    // ================= DOCUMENT =================
                    else if (category == "DOCUMENT")
                    {
                        query = @"
SELECT
    bd.AREA_ID,
    bd.AREA_NAME,

    /* Total = Completed + Pending */
    COUNT(DISTINCT CASE
        WHEN d.BRANCH_ID IS NOT NULL
        THEN d.BRANCH_ID
    END) AS TOTAL_BRANCHES,

    0 AS DUE_MTD,

    /* Due branches */
    COUNT(DISTINCT CASE
        WHEN NVL(d.LAG_DAYS,0) > 0
        THEN d.BRANCH_ID
    END) AS DUE_MTM,

    /* Completed in Previous Month */
    COUNT(DISTINCT CASE
        WHEN d.AUDIT_FINISH_DT IS NOT NULL
         AND d.AUDIT_FINISH_DT <> '-'
         AND TO_DATE(d.AUDIT_FINISH_DT,'DD-MON-YYYY')
             BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
             AND TO_DATE(:tdate,'DD-MON-YYYY')
        THEN d.BRANCH_ID
    END) AS COMPLETED,

    0 AS ONGOING,

    /* Pending in Previous Month */
    COUNT(DISTINCT CASE
        WHEN d.AUDIT_FINISH_DT IS NULL
          OR d.AUDIT_FINISH_DT = '-'
          OR TO_DATE(d.AUDIT_FINISH_DT,'DD-MON-YYYY') > TO_DATE(:tdate,'DD-MON-YYYY')
        THEN d.BRANCH_ID
    END) AS PENDING

FROM DOCUMENTAUDITLAGDAYS d
JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = d.BRANCH_ID
JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bm.FIRM_ID = :firm_id

GROUP BY bd.AREA_ID, bd.AREA_NAME
ORDER BY bd.AREA_NAME";
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message });
            }
        }

        // ======================
        // BRANCH DETAILS API
        // ======================
        [HttpGet]
        [Route("getpreviousgoldauditbranchdetails")]
        public IHttpActionResult GetPreviousGoldAuditBranchDetails(string category, string areaId, string fdate, string tdate, int firm_id)
        {
            try
            {
                DataTable dt = new DataTable();

                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = "";
                    string cat = (category ?? "").Trim().ToUpper();

                    // ================= FULL =================
                    if (cat == "FULL")
                    {
                        query = @"
SELECT
    bd.REG_NAME AS AREA_NAME,
    bd.BRANCH_ID,
    bd.BRANCH_NAME,
    m.AUDIT_DT AS DUE_DATE,

    CASE
        WHEN m.STATUS = 2 THEN 'Completed'
        WHEN m.STATUS = 1 THEN 'Ongoing'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM TBL_SEC_AUDITOR_MST m

JOIN BRANCH_DETAIL bd
    ON bd.BRANCH_ID = m.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = m.BRANCH_ID

WHERE TRIM(bd.REG_NAME) = TRIM(:areaId)
AND bm.FIRM_ID = :firm_id
AND TRUNC(m.AUDIT_DT) BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
                         AND TO_DATE(:tdate,'DD-MON-YYYY')

ORDER BY m.AUDIT_DT DESC, bd.BRANCH_NAME";
                    }

                    // ================= RISK =================
                    else if (cat == "RISK")
                    {
                        query = @"
SELECT DISTINCT
    bd.AREA_NAME,
    bd.BRANCH_ID,
    bd.BRANCH_NAME,
    TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY') AS DUE_DATE,

    CASE
        WHEN apd.STATUS = 1 THEN 'Completed'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM BRANCH_DETAIL bd

JOIN Auditlagdays a
    ON a.BRANCH_ID = bd.BRANCH_ID

JOIN BRANCH_MASTER bm
    ON bm.BRANCH_ID = bd.BRANCH_ID

LEFT JOIN
(
    SELECT BRANCH_ID, MAX(STATUS) STATUS
    FROM audit_pledge_deduction
    GROUP BY BRANCH_ID
) apd
ON apd.BRANCH_ID = bd.BRANCH_ID

WHERE bd.AREA_ID = :areaId
AND bm.FIRM_ID = :firm_id
AND TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY')
    BETWEEN TO_DATE(:fdate,'DD-MON-YYYY')
    AND TO_DATE(:tdate,'DD-MON-YYYY')

ORDER BY TO_DATE(a.LASTAUDITDATE,'DD-MON-YYYY') DESC";
                    }

                    // ================= SECURITY =================
                    else if (cat == "SECURITY")
                    {
                        query = @"
SELECT
    bd.AREA_NAME,
    d.BRANCH_ID,
    bd.BRANCH_NAME,
    d.BASE_DATE AS DUE_DATE,

    CASE
        WHEN d.LAG_DAYS > 45 THEN 'Pending'
        ELSE 'Completed'
    END AS STATUS_TEXT

FROM BLUEAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bd.AREA_ID = :areaId
AND bm.FIRM_ID = :firm_id

ORDER BY d.LAG_DAYS DESC";
                    }

                    // ================= DOCUMENT =================
                    else if (cat == "DOCUMENT")
                    {
                        query = @"
SELECT
    bd.AREA_NAME,
    bd.BRANCH_ID,
    bd.BRANCH_NAME,
    d.AUDIT_FINISH_DT AS DUE_DATE,

    CASE
        WHEN d.AUDIT_FINISH_DT IS NOT NULL
         AND d.AUDIT_FINISH_DT <> '-'
        THEN 'Completed'
        ELSE 'Pending'
    END AS STATUS_TEXT

FROM DOCUMENTAUDITLAGDAYS d

JOIN BRANCH_DETAIL bd
ON bd.BRANCH_ID = d.BRANCH_ID

JOIN BRANCH_MASTER bm
ON bm.BRANCH_ID = d.BRANCH_ID

WHERE bd.AREA_ID = :areaId
AND bm.FIRM_ID = :firm_id

ORDER BY bd.BRANCH_NAME";
                    }

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;

                        cmd.Parameters.Add("areaId", OracleDbType.Varchar2).Value = areaId;
                        cmd.Parameters.Add("fdate", OracleDbType.Varchar2).Value = fdate;
                        cmd.Parameters.Add("tdate", OracleDbType.Varchar2).Value = tdate;
                        cmd.Parameters.Add("firm_id", OracleDbType.Int32).Value = firm_id;

                        using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }

                return Ok(dt);
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message });
            }
        }
    }
}
