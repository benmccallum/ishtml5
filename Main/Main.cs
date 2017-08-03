using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace ishtml5
{
    public static class Main
    {
        [FunctionName("Main")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequestMessage req, 
            IQueryable<TestedUrl> testedUrlsInTable,
            ICollector<TestedUrl> testedUrlsOutTable,
            TraceWriter log)
        {
            log.Info("Main was triggered.");

            // Extract query parameter
            string url = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "url", true) == 0)
                .Value;
            
            // Check a url param was passed
            if (url == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a url on the query string or in the request body");
            }

            // Validate it's a website URL
            Uri uri = null;
            var isUrl = (url.StartsWith("http://") || url.StartsWith("https://"))
                && Uri.IsWellFormedUriString(url, UriKind.Absolute)
                && Uri.TryCreate(url, UriKind.Absolute, out uri);
            if (!isUrl)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a VALID url");
            }

            var isHtml5 = await GetResult(uri, testedUrlsInTable, testedUrlsOutTable);

            return req.CreateResponse(HttpStatusCode.OK, isHtml5);
        }

        private static async Task<bool> GetResult(Uri uri,
            IQueryable<TestedUrl> testedUrlsInTable,
            ICollector<TestedUrl> testedUrlsOutTable)
        {
            // Try get from cache if recent enough
            var testedUrl = testedUrlsInTable
                .SingleOrDefault(x => x.PartitionKey == uri.Host && x.RowKey == uri.ToString());

            // TODO: Probably push this into a config
            // TODO: Wait for answer on https://stackoverflow.com/questions/17325445/timestamp-query-in-azure to see if I can include this in initial query and save bandwidth
            var oneWeekAgo = new DateTimeOffset(DateTime.UtcNow.AddDays(-7));
            if (testedUrl.Timestamp >= oneWeekAgo)
            {
                return testedUrl.IsHtml5;
            }

            // Else get a fresh one and store in cache
            
            // Make a web request for that URL document and then "crudely" inspect for doctype declaration
            var client = new HttpClient();
            var response = await client.GetAsync(uri);
            var html = (await response.Content.ReadAsStringAsync()).Trim();
            var isHtml5 = html.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase);

            testedUrl = new TestedUrl(uri, isHtml5);

            testedUrlsOutTable.Add(testedUrl);

            return testedUrl.IsHtml5;
        }

        /// <summary>
        /// Represents a tested URL who's result has been cached.
        /// </summary>
        public class TestedUrl : TableEntity
        {
            public bool IsHtml5 { get; set; }

            public TestedUrl(Uri uri, bool isHtml5)
            {
                this.PartitionKey = uri.Host;
                this.RowKey = uri.ToString();
                this.IsHtml5 = isHtml5;
            }
        }
    }
}