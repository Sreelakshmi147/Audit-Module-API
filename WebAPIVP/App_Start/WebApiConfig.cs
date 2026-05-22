using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Cors;   
namespace WebAPIVP
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // ✅ FORCE JSON ONLY (VERY IMPORTANT)
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            // ✅ ENABLE CORS FOR ANGULAR
            var cors = new EnableCorsAttribute(
                //origins: "http://10.5.101.213:8086,http://localhost:8086,https://gen.mactech.net.in/HOAudit/",
                origins: "*",
                headers: "*",
                methods: "*"
            );
            config.EnableCors(cors);

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }


    }
}
