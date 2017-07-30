using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Xml;

namespace ishtml5
{
    public static class Main
    {
        [FunctionName("Main")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            string url = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "url", true) == 0)
                .Value;

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();

            // Set name to query string or body data
            url = url ?? data?.url;

            // Check a url param was passed
            if (url == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a url on the query string or in the request body");
            }

            // Validate it's a URL
            var isUrl = true;
            if (!isUrl)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a VALID url");
            }
            
            // Make a web request for that URL document and then parse it to see if it has a HTML5 doctype
            var client = new HttpClient();
            var response = await client.GetAsync(url);
            var html = (await response.Content.ReadAsStringAsync()).Trim();
            var hasDoctype = html.StartsWith("<!doctype", System.StringComparison.OrdinalIgnoreCase);
            if (!hasDoctype)
            {
                return req.CreateResponse(HttpStatusCode.OK, false);
            }

            var htmlSnippet = html.Substring(0, html.IndexOf(">")) + "<lol></lol>";
            var xml = new XmlDocument();
            xml.LoadXml(htmlSnippet);
            
            var isHtml5 = xml.DocumentType.Name.Equals("html", System.StringComparison.OrdinalIgnoreCase);

            return req.CreateResponse(HttpStatusCode.OK, isHtml5);
        }
    }
}