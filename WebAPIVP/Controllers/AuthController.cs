using Oracle.ManagedDataAccess.Client;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using System.Web.Http.Cors;
using WebAPIVP.ConnectionString;
using WebAPIVP.Models;

namespace WebAPIVP.Controllers
{
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    [RoutePrefix("api")]
    
    public class AuthController : ApiController
    {
        private readonly string _connectionString;

        public AuthController()
        {
            MaafinDbHelper db = new MaafinDbHelper();
            db.Connection();
            _connectionString = db.conStr1;
        }

        public string HashedPasswod { get; private set; }

        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login(LoginModel model)
        {
            // 1️⃣ Basic validation
            if (model == null ||
                string.IsNullOrWhiteSpace(model.Username) ||
                string.IsNullOrWhiteSpace(model.Password))
            {
                return Ok(new
                {
                    IsSuccess = false,
                    Message = "Employee code and password are required"
                });
            }

            // 🔒 CHECK EMPLOYEE CODE IS NUMERIC
            if (!int.TryParse(model.Username, out int empCode))
            {
                return Ok(new
                {
                    IsSuccess = false,
                    Message = "Employee code must contain numbers only"
                });
            }

            // 2️⃣ Normalize input (SAFE for all frontends)
            model.Username = model.Username.Trim();
            model.Password = model.Password.Trim();

            string empName = null;
            int postId = 0;
            int departmentId = 0;
            int branchId = 0;
            int firmId = 0;
            int statusId = 0;

            try
            {
                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"SELECT 
    t.passwod,
    e.emp_name,
    e.post_id,
    e.department_id,
    e.branch_id,
    e.firm_id,
    e.status_id
FROM maaf_int_logininternal t
JOIN employee_master e 
     ON e.emp_code = t.emp_code
WHERE t.emp_code = :emp_code
  AND e.branch_id=0
  AND e.firm_id=4
  AND e.status_id=1";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.BindByName = true;
                        cmd.Parameters.Add(":emp_code", model.Username);

                        using (OracleDataReader dr = cmd.ExecuteReader())
                        {
                            if (!dr.Read())
                            {
                                return Ok(new
                                {
                                    IsSuccess = false,
                                    Message = "Invalid employee code or password"
                                });
                            }

                            // 3️⃣ NULL-safe + Trim (PREVENTS API CRASH)
                            HashedPasswod = dr["PASSWOD"]?.ToString()?.Trim();
                            empName = dr["EMP_NAME"]?.ToString()?.Trim();

                            postId = dr["POST_ID"] != DBNull.Value
                                     ? Convert.ToInt32(dr["POST_ID"])
                                     : 0;

                            departmentId = dr["DEPARTMENT_ID"] != DBNull.Value
                                           ? Convert.ToInt32(dr["DEPARTMENT_ID"])
                                           : 0;

                            branchId = dr["BRANCH_ID"] != DBNull.Value
                                       ? Convert.ToInt32(dr["BRANCH_ID"])
                                       : 0;

                            firmId = dr["FIRM_ID"] != DBNull.Value
                                     ? Convert.ToInt32(dr["FIRM_ID"])
                                     : 0;

                            statusId = dr["STATUS_ID"] != DBNull.Value
                                       ? Convert.ToInt32(dr["STATUS_ID"])
                                       : 0;

                        }
                    }
                }

                // 4️⃣ Safety check
                if (string.IsNullOrEmpty(HashedPasswod))
                {
                    return Ok(new
                    {
                        IsSuccess = false,
                        Message = "Invalid employee code or password"
                    });
                }

                // 5️⃣ Split hash and salt
                string[] parts = HashedPasswod.Split(':');
                if (parts.Length != 2)
                {
                    return Ok(new
                    {
                        IsSuccess = false,
                        Message = "Invalid password format"
                    });
                }

                string storedHash = parts[0].Trim();
                string storedSalt = parts[1].Trim();

                // 6️⃣ Hash entered password (DO NOT CHANGE LOGIC)
                string enteredHash = HashPasswordWithSHA256(
                    model.Password,
                    storedSalt
                );

                // 7️⃣ Compare hashes
                if (enteredHash == storedHash)
                {
                    string role = "User"; // default

                    // 🔥 AUDIT HEAD
                    if (departmentId == 4 &&
                        postId == 750 &&
                        firmId == 4 &&
                        statusId == 1 &&
                        branchId == 0)
                    {
                        role = "AuditHead";
                    }
                    // 🔹 AUDITOR
                    else if (departmentId == 4 &&
                             firmId == 4 &&
                             statusId == 1 &&
                             branchId == 0)
                    {
                        role = "Auditor";
                    }
                    // 🔹 USER
                    else if (firmId == 4 &&
                             statusId == 1 &&
                             branchId == 0)
                    {
                        role = "User";
                    }

                    return Ok(new
                    {
                        IsSuccess = true,
                        Message = "Login successful",
                        EmployeeName = empName,
                        EmployeeCode = model.Username,
                        Role = role
                    });
                }


                return Ok(new
                {
                    IsSuccess = false,
                    Message = "Invalid employee code or password"
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        // 🔐 HASH METHOD — CORRECT & UNCHANGED
        private string HashPasswordWithSHA256(string password, string salt)
        {
            string saltedPassword = password + salt;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(saltedPassword);
                byte[] hashBytes = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}
