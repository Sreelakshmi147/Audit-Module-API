using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Http;
using Oracle.ManagedDataAccess.Client;
using WebAPIVP.Models;
using WebAPIVP.ConnectionString;
using Microsoft.Ajax.Utilities;

namespace WebAPIVP.Controllers
{
    public class CustomerController : ApiController
    {
        private readonly string _connectionString;
        public CustomerController()
        {
            MaafinDbHelper dbHelper = new MaafinDbHelper();
            dbHelper.Connection();
            _connectionString = dbHelper.conStr1;     
        }
         
        //This method select customer from the database

        [HttpGet]
        [Route("api/customer/getall")]
        public IHttpActionResult GetAllCustomers(string search = null)
        {
            List<CustomerModel> customers = new List<CustomerModel>();

            using (OracleConnection connection = new OracleConnection(_connectionString))
            {
                connection.Open();

                string query = @"
            SELECT 
                cust_id,
                customer_name,
                adress,
                phone,
                DOB,
                gender,
                email,
                place,
                IdType,
                IdNumber
            FROM temp_vp_customer_form
            WHERE
                (:search IS NULL OR
                 LOWER(customer_name) LIKE '%' || LOWER(:search) || '%' OR
                 LOWER(adress)        LIKE '%' || LOWER(:search) || '%' OR
                 TO_CHAR(phone)      LIKE '%' || :search || '%' OR
                 LOWER(email)         LIKE '%' || LOWER(:search) || '%' OR
                 LOWER(place)         LIKE '%' || LOWER(:search) || '%' OR
                 LOWER(IdType)        LIKE '%' || LOWER(:search) || '%' OR
                 LOWER(IdNumber)      LIKE '%' || LOWER(:search) || '%')
            ORDER BY cust_id DESC";

                using (OracleCommand cmd = new OracleCommand(query, connection))
                {
                    
                    cmd.Parameters.Add(
                        ":search",
                        string.IsNullOrWhiteSpace(search) ? null : search
                    );

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            customers.Add(new CustomerModel
                            {
                                CustId = Convert.ToInt32(dr["CUST_ID"]),
                                CustomerName = dr["CUSTOMER_NAME"].ToString(),
                                Adress = dr["ADRESS"].ToString(),
                                Phone = Convert.ToInt64(dr["PHONE"]),
                                DOB = Convert.ToDateTime(dr["DOB"]),
                                Gender = dr["GENDER"].ToString(),
                                Email = dr["EMAIL"].ToString(),
                                Place = dr["PLACE"].ToString(),
                                IdType = dr["IDTYPE"].ToString(),
                                IdNumber = dr["IDNUMBER"].ToString()
                            });
                        }
                    }
                }
            }

            return Ok(customers);
        }


        //private IHttpActionResult Ok(List<CustomerModel> customers)
        //{ 
        //    throw new NotImplementedException();
        //}


        [HttpPost]
        [Route("api/customer/add")]
        public IHttpActionResult AddCustomer(CustomerModel customer)
        {
            
            if (CalculateAge(customer.DOB) < 18)
            {
                return Ok(new
                {
                    IsSuccess = false,
                    Message = "Age must be 18 years or above"
                });
            }

            try
            {
                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                INSERT INTO temp_vp_customer_form
                (cust_id, customer_name, adress, phone, DOB, gender, email, place, IdType, IdNumber)
                VALUES
                (TEMP_VP_CUSTOMER_FORM_seq.NEXTVAL,
                 :customer_name,
                 :adress,
                 :phone,
                 :dob,
                 :gender,
                 :email,
                 :place,
                 :idtype,
                 :idnumber)";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.Parameters.Add(":customer_name", customer.CustomerName);
                        cmd.Parameters.Add(":adress", customer.Adress);
                        cmd.Parameters.Add(":phone", customer.Phone);
                        cmd.Parameters.Add(":dob", customer.DOB);
                        cmd.Parameters.Add(":gender", customer.Gender);
                        cmd.Parameters.Add(":email", customer.Email);
                        cmd.Parameters.Add(":place", customer.Place);
                        cmd.Parameters.Add(":idtype", customer.IdType);
                        cmd.Parameters.Add(":idnumber", customer.IdNumber);

                        cmd.ExecuteNonQuery();
                    }
                }

                return Ok(new
                {
                    IsSuccess = true,
                    Message = "Customer Saved Successfully"
                });
            }
            catch (OracleException ex)
            {
                if (ex.Number == 1)
                {
                    return Ok(new
                    {
                        IsSuccess = false,
                        Message = "Phone number or Email already exists"
                    });
                }

                return InternalServerError(ex);
            }
        }

        private int CalculateAge(DateTime dob)
        {
            int age = DateTime.Today.Year - dob.Year;
            if (dob.Date > DateTime.Today.AddYears(-age))
                age--;
            return age;
        }



        [HttpDelete]
        [Route("api/customer/delete/{id}")]
        public IHttpActionResult DeleteCustomer(int id)
        {

            try
            {
                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = "DELETE FROM temp_vp_customer_form WHERE cust_id = :id";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.Parameters.Add(":id", id);

                        int rows = cmd.ExecuteNonQuery();

                        if (rows == 0)
                        {
                            return Ok(new
                            {
                                IsSuccess = false,
                                Message = "Customer not found"
                            });
                        }
                    }
                }

                return Ok(new
                {
                    IsSuccess = true,
                    Message = "Customer deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPut]
        [Route("api/customer/update")]
        public IHttpActionResult UpdateCustomer(CustomerModel customer)
        {
            if (CalculateAge(customer.DOB) < 18)
            {
                return Ok(new
                {
                    IsSuccess = false,
                    Message = "Age must be 18 years or above"
                });
            }

            try
            {
                using (OracleConnection con = new OracleConnection(_connectionString))
                {
                    con.Open();

                    string query = @"
                UPDATE temp_vp_customer_form
                SET
                    customer_name = :customer_name,
                    adress        = :adress,
                    phone         = :phone,
                    DOB           = :dob,
                    gender        = :gender,
                    email         = :email,
                    place         = :place,
                    IdType        = :idtype,
                    IdNumber      = :idnumber
                WHERE cust_id = :cust_id";

                    using (OracleCommand cmd = new OracleCommand(query, con))
                    {
                        cmd.Parameters.Add(":customer_name", customer.CustomerName);
                        cmd.Parameters.Add(":adress", customer.Adress);
                        cmd.Parameters.Add(":phone", customer.Phone);
                        cmd.Parameters.Add(":dob", customer.DOB);
                        cmd.Parameters.Add(":gender", customer.Gender);
                        cmd.Parameters.Add(":email", customer.Email);
                        cmd.Parameters.Add(":place", customer.Place);
                        cmd.Parameters.Add(":idtype", customer.IdType);
                        cmd.Parameters.Add(":idnumber", customer.IdNumber);
                        cmd.Parameters.Add(":cust_id", customer.CustId);

                        int rows = cmd.ExecuteNonQuery();

                        if (rows == 0)
                        {
                            return Ok(new
                            {
                                IsSuccess = false,
                                Message = "Customer not found"
                            });
                        }
                    }
                }

                return Ok(new
                {
                    IsSuccess = true,
                    Message = "Customer updated successfully"
                });
            }
            catch (OracleException ex)
            {
               
                if (ex.Number == 1)
                {
                    return Ok(new
                    {
                        IsSuccess = false,
                        Message = "Phone number or Email already exists"
                    });
                }

                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("api/customer/getbyid/{id}")]
        public IHttpActionResult GetCustomerById(int id)
        {
            CustomerModel customer = null;

            using (OracleConnection con = new OracleConnection(_connectionString))
            {
                con.Open();

                string query = @"
            SELECT 
                cust_id,
                customer_name,
                adress,
                phone,
                DOB,
                gender,
                email,
                place,
                IdType,
                IdNumber
            FROM temp_vp_customer_form
            WHERE cust_id = :id";

                using (OracleCommand cmd = new OracleCommand(query, con))
                {
                    cmd.Parameters.Add(":id", id);

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            customer = new CustomerModel
                            {
                                CustId = Convert.ToInt32(dr["CUST_ID"]),
                                CustomerName = dr["CUSTOMER_NAME"].ToString(),
                                Adress = dr["ADRESS"].ToString(),
                                Phone = Convert.ToInt64(dr["PHONE"]),
                                DOB = Convert.ToDateTime(dr["DOB"]),
                                Gender = dr["GENDER"].ToString(),
                                Email = dr["EMAIL"].ToString(),
                                Place = dr["PLACE"].ToString(),
                                IdType = dr["IDTYPE"].ToString(),
                                IdNumber = dr["IDNUMBER"].ToString()
                            };
                        }
                    }
                }
            }

            if (customer == null)
            {
                return NotFound(); 
            }

            return Ok(customer);
        }




    }
}
