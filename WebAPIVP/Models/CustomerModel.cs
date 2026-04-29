using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebAPIVP.Models
{
    public class CustomerModel
    {
            public int CustId { get; set; }
            public string CustomerName { get; set; }
            public string Adress { get; set; }
            public long Phone { get; set; }
            public DateTime DOB { get; set; }
            public string Gender { get; set; }
            public string Email { get; set; }
            public string Place { get; set; }
            public string IdType { get; set; }
            public string IdNumber { get; set; }
  


    }
}