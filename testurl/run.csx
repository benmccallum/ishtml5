#r "Microsoft.WindowsAzure.Storage"

using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, 
    TraceWriter log,
    IQueryable<TestedUrl> inputTable,
    ICollector<TestedUrl> outputTable
)
{
    log.Info("TestUrl function was triggered.");

    // Extract query parameter
    string url = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "url", true) == 0)
        .Value;

    // Check a url param was passed
    if (url == null)
    {
        log.Info("Validation failed: url param was null.");
        return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a url on the query string or in the request body");
    }

    // Validate it's a website URL
    Uri uri = null;
    var isUrl = (url.StartsWith("http://") || url.StartsWith("https://"))
        && Uri.IsWellFormedUriString(url, UriKind.Absolute)
        && Uri.TryCreate(url, UriKind.Absolute, out uri);
    if (!isUrl)
    {
        log.Info("Validation failed: url param was not a real url.");
        return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a VALID url");
    }

    var isHtml5 = await GetResult(uri, log, inputTable, outputTable);

    return req.CreateResponse(HttpStatusCode.OK, isHtml5);
}

private static async Task<bool> GetResult(Uri uri,
    TraceWriter log,
    IQueryable<TestedUrl> inputTable,
    ICollector<TestedUrl> outputTable)
{
    // Try get from cache if recent enough
    var q = new TestedUrl(uri, false);
    log.Info("Searching for: " + q.PartitionKey + " " + q.RowKey);
    var testedUrl = inputTable.Where(x => x.PartitionKey == q.PartitionKey && x.RowKey == q.RowKey).SingleOrDefault();
    if (testedUrl == null) 
    {
        log.Info("Cache miss: no match for url.");
        return await Test(uri, log, inputTable, outputTable);
    }

    // TODO: Probably push this into a config
    // TODO: Wait for answer on https://stackoverflow.com/questions/17325445/timestamp-query-in-azure to see if I can include this in initial query and save bandwidth
    var oneWeekAgo = new DateTimeOffset(DateTime.UtcNow.AddDays(-7));
    if (testedUrl.Timestamp >= oneWeekAgo)
    {
        log.Info("Returning from cache.");
        return testedUrl.IsHtml5;
    }

    // Else get a fresh one and store in cache
    return await Test(uri, log, inputTable, outputTable);
}

private static async Task<bool> Test(Uri uri,
    TraceWriter log,
    IQueryable<TestedUrl> inputTable,
    ICollector<TestedUrl> outputTable)
{
    log.Info("Testing: " + uri);

    // Make a web request for that URL document and then "crudely" inspect for doctype declaration
    var client = new HttpClient();
    var response = await client.GetAsync(uri);
    var html = (await response.Content.ReadAsStringAsync()).Trim();
    var isHtml5 = html.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase);

    var testedUrl = new TestedUrl(uri, isHtml5);

    log.Info("Caching: " + uri);
    outputTable.Add(testedUrl);    

    return testedUrl.IsHtml5;
}

/// <summary>
/// Represents a tested URL who's result has been cached.
/// </summary>
public class TestedUrl : TableEntity
{
    public bool IsHtml5 { get; set; }

    public TestedUrl()
    {
            
    }

    public TestedUrl(Uri uri, bool isHtml5)
    {
        this.PartitionKey = uri.Host;
        this.RowKey = Uri.EscapeDataString(uri.ToString());
        this.IsHtml5 = isHtml5;
    }
}