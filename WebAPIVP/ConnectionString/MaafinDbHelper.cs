using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace WebAPIVP.ConnectionString
{
    //public class MaafinDbHelper : IDisposable
    //{
    //    private bool disposed = false;
    //    //private readonly HttpClient client;
    //    private string cachedConnectionString;
    //    private DateTime cacheExpiryUtc = DateTime.MinValue;
    //    private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);
    //    private HttpClient client;
    //    public string conStr1;

    //    public MaafinDbHelper()
    //    {
    //        client = new HttpClient();
    //        // Use the appropriate base address for your connection-string provider
    //        client.BaseAddress = new Uri("http://localhost:5000/");           
    //    }
    //    // async getter (preferred)
    //    public async Task<string> GetConnectionStringAsync()
    //    {
    //        // Return cached value if not expired
    //        if (!string.IsNullOrWhiteSpace(cachedConnectionString) && DateTime.UtcNow < cacheExpiryUtc)
    //            return cachedConnectionString;

    //        await refreshLock.WaitAsync();
    //        try
    //        {
    //            // double-check after acquiring lock
    //            if (!string.IsNullOrWhiteSpace(cachedConnectionString) && DateTime.UtcNow < cacheExpiryUtc)
    //                return cachedConnectionString;

    //            var response = await client.GetAsync("ConnectionString").ConfigureAwait(false);
    //            response.EnsureSuccessStatusCode();
    //            var message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    //            cachedConnectionString = message?.Trim();
    //            // cache for 10 minutes by default - tune as needed
    //            cacheExpiryUtc = DateTime.UtcNow.AddMinutes(10);
    //            return cachedConnectionString;
    //        }
    //        finally
    //        {
    //            refreshLock.Release();
    //        }
    //    }

    //    // sync wrapper if you need synchronous calls (calls async and blocks)
    //    public string GetConnectionString()
    //    {
    //        return GetConnectionStringAsync().GetAwaiter().GetResult();
    //    }

    //    public void Dispose()
    //    {
    //        Dispose(true);
    //        GC.SuppressFinalize(this);
    //    }

    //    protected virtual void Dispose(bool disposing)
    //    {
    //        if (!disposed)
    //        {
    //            if (disposing)
    //            {
    //                client.Dispose();
    //                refreshLock.Dispose();
    //            }     
    //            disposed = true;
    //        }
    //    }
    //}  


    public class MaafinDbHelper : IDisposable
    {
        private bool disposed = false;
        private HttpClient client;
        public string conStr1;

        public MaafinDbHelper()
        {
            client = new HttpClient();
            client.BaseAddress = new Uri("http://localhost:5000/");
            //client.BaseAddress = new Uri("https://serv.mactech.net.in/Maafin_API/");
        }

        public string Connection()
        {
            var response = client.GetAsync("ConnectionString").Result;
            var message = response.Content.ReadAsStringAsync().Result;
            conStr1 = message.ToString();
            return conStr1;
        }

        // Implement IDisposable interface
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    //Dispose managed resources here
                    client.Dispose();
                }
                // Dispose unmanaged resources here

                disposed = true;
            }
        }
    }
}