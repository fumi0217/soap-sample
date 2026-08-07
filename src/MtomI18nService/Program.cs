using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using MimeKit;

// ---------------------------------------------------------------------------
// MtomI18nService (a.k.a. "MockDocSuite")
//
// A tiny, self-contained, fully mocked SOAP 1.1 endpoint that speaks
// MTOM/XOP on the wire, modeling a generic pattern used by many enterprise
// document/workflow repository systems: session-based login, an
// "I18nString"-style language-tagged label type, object search, and object
// upload carrying binary content (the part MTOM optimizes).
//
// This is an ORIGINAL MOCK SERVICE invented for this sample. It is not
// derived from, does not reproduce, and is not affiliated with any real
// company's product, WSDL, or namespace -- all names/data below are made up
// and every operation is backed by in-memory, fake logic only.
//
// It is deliberately NOT built on WCF/CoreWCF: CoreWCF's current release
// has no server-side MTOM message encoder (see
// https://github.com/CoreWCF/CoreWCF/issues/10), so a CoreWCF host cannot
// produce/consume real MTOM on Linux/.NET today. Instead this parses and
// emits the multipart/related XOP framing directly, which is portable and
// fully interoperable with a genuine WCF/System.ServiceModel MTOM client.
//
// Operations (all POST to /MockDocSuite.svc, dispatched by the SOAP body's
// root element name -- exactly like a real single-endpoint SOAP service):
//   Login(UserId, CredentialType, Credential) -> SessionId
//   SearchRepositoryObjects(SessionId, Query) -> RepositoryObjectSummary[]
//   UploadRepositoryObject(SessionId, Label[I18nString], FileName, Content)
//   Logout(SessionId)
// ---------------------------------------------------------------------------

const string SoapEnvNs = "http://schemas.xmlsoap.org/soap/envelope/";
const string AppNs = "http://example.com/mockdocsuite";
const string XopNs = "http://www.w3.org/2004/08/xop/include";

XNamespace soapNs = SoapEnvNs;
XNamespace appNs = AppNs;
XNamespace xopNs = XopNs;

// Mock in-memory session store: sessionId -> userId. No real authentication;
// any non-empty UserId/Credential is accepted, matching "mock internals".
var sessions = new ConcurrentDictionary<string, string>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5205");
var app = builder.Build();

app.MapGet("/", () => "MockDocSuite (mock service) is running. POST MTOM SOAP requests to /MockDocSuite.svc");

app.MapPost("/MockDocSuite.svc", async (HttpRequest request, HttpResponse response, ILogger<Program> logger) =>
{
    var contentType = request.ContentType ?? string.Empty;
    if (!contentType.Contains("multipart/related", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Rejected non-MTOM request with Content-Type: {ContentType}", contentType);
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsync("Expected an MTOM request (multipart/related; type=\"application/xop+xml\").");
        return;
    }

    // MimeKit parses a MIME *entity*, i.e. headers + body. ASP.NET Core has
    // already stripped the Content-Type header off into request.ContentType,
    // so we re-attach it in front of the raw body before handing it to MimeKit.
    using var raw = new MemoryStream();
    var header = Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n\r\n");
    await raw.WriteAsync(header);
    await request.Body.CopyToAsync(raw);
    raw.Position = 0;

    var entity = await MimeEntity.LoadAsync(raw);
    if (entity is not MultipartRelated related || related.Root is not MimePart rootPart)
    {
        logger.LogWarning("Malformed MTOM payload: root XOP part not found.");
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsync("Malformed MTOM payload: could not locate the root XOP/SOAP part.");
        return;
    }

    string envelopeXml;
    await using (var rootStream = new MemoryStream())
    {
        await rootPart.Content.DecodeToAsync(rootStream);
        envelopeXml = Encoding.UTF8.GetString(rootStream.ToArray());
    }

    var envelope = XDocument.Parse(envelopeXml);
    var body = envelope.Root?.Element(soapNs + "Body")
        ?? throw new InvalidOperationException("SOAP Body not found.");
    var operationEl = body.Elements().FirstOrDefault()
        ?? throw new InvalidOperationException("SOAP Body has no operation element.");

    logger.LogInformation("Dispatching operation: {Operation}", operationEl.Name.LocalName);

    string responseXml = operationEl.Name.LocalName switch
    {
        "LoginRequest" => HandleLogin(operationEl),
        "SearchRepositoryObjectsRequest" => HandleSearch(operationEl),
        "UploadRepositoryObjectRequest" => HandleUpload(operationEl, related, logger),
        "LogoutRequest" => HandleLogout(operationEl),
        var other => throw new InvalidOperationException($"Unknown operation: {other}"),
    };

    var isFault = responseXml.Contains("<soap:Fault>", StringComparison.Ordinal);
    var (mtomBytes, mtomContentType) = BuildMtomResponse(responseXml);
    response.StatusCode = isFault ? StatusCodes.Status500InternalServerError : StatusCodes.Status200OK;
    response.ContentType = mtomContentType;
    await response.Body.WriteAsync(mtomBytes);
});

app.Run();

// ---------------------------------------------------------------------------
// Handlers -- all logic below is mocked (in-memory, fake data).
// ---------------------------------------------------------------------------

string HandleLogin(XElement req)
{
    var userId = (string?)req.Element(appNs + "UserId") ?? "";
    var credential = (string?)req.Element(appNs + "Credential") ?? "";

    bool success = !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(credential);
    string sessionId = success ? Guid.NewGuid().ToString("N") : "";
    if (success) sessions[sessionId] = userId;

    var message = success
        ? $"Mock login OK for '{Esc(userId)}'."
        : "UserId and Credential are required.";

    return
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <soap:Envelope xmlns:soap="{SoapEnvNs}">
           <soap:Body>
             <LoginResponse xmlns="{AppNs}">
               <Success>{(success ? "true" : "false")}</Success>
               <SessionId>{sessionId}</SessionId>
               <Message>{message}</Message>
             </LoginResponse>
           </soap:Body>
         </soap:Envelope>
         """;
}

string HandleSearch(XElement req)
{
    var sessionId = (string?)req.Element(appNs + "SessionId") ?? "";
    var query = (string?)req.Element(appNs + "Query") ?? "";

    if (!sessions.ContainsKey(sessionId))
    {
        return SoapFault("Client", "Invalid or expired SessionId. Call Login first.");
    }

    // Canned mock results -- multilingual labels regardless of query.
    var objects = new[]
    {
        (Id: "obj-1001", Modified: "2026-08-01", Labels: new (string Lang, string Value)[]
        {
            ("ja", $"「{Esc(query)}」検索結果サンプル 1"),
            ("en", $"Sample search result 1 for '{Esc(query)}'"),
        }),
        (Id: "obj-1002", Modified: "2026-07-15", Labels: new (string Lang, string Value)[]
        {
            ("ja", "四半期レポート（サンプル）"),
            ("en", "Quarterly report (sample)"),
        }),
    };

    var objectsXml = new StringBuilder();
    foreach (var o in objects)
    {
        objectsXml.Append("      <RepositoryObjectSummary>\n");
        objectsXml.Append($"        <ObjectId>{o.Id}</ObjectId>\n");
        objectsXml.Append("        <ObjectClass>\n");
        foreach (var (lang, value) in o.Labels)
        {
            objectsXml.Append($"          <I18nLabel><Lang>{lang}</Lang><Value>{Esc(value)}</Value></I18nLabel>\n");
        }
        objectsXml.Append("        </ObjectClass>\n");
        objectsXml.Append($"        <ModifiedDate>{o.Modified}</ModifiedDate>\n");
        objectsXml.Append("      </RepositoryObjectSummary>\n");
    }

    return
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <soap:Envelope xmlns:soap="{SoapEnvNs}">
           <soap:Body>
             <SearchRepositoryObjectsResponse xmlns="{AppNs}">
               <Objects>
         {objectsXml}      </Objects>
             </SearchRepositoryObjectsResponse>
           </soap:Body>
         </soap:Envelope>
         """;
}

string HandleUpload(XElement req, MultipartRelated related, ILogger logger)
{
    var sessionId = (string?)req.Element(appNs + "SessionId") ?? "";
    if (!sessions.ContainsKey(sessionId))
    {
        return SoapFault("Client", "Invalid or expired SessionId. Call Login first.");
    }

    var fileName = (string?)req.Element(appNs + "FileName") ?? "";

    var labelEl = req.Element(appNs + "Label");
    var labels = labelEl?.Elements(appNs + "I18nLabel")
        .Select(l => ((string?)l.Element(appNs + "Lang") ?? "", (string?)l.Element(appNs + "Value") ?? ""))
        .ToList() ?? new List<(string, string)>();

    var contentEl = req.Element(appNs + "Content");
    var include = contentEl?.Element(xopNs + "Include");
    var contentBytes = Array.Empty<byte>();

    if (include != null)
    {
        var href = (string?)include.Attribute("href") ?? "";
        var cid = Uri.UnescapeDataString(href.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)
            ? href["cid:".Length..]
            : href);

        var binaryPart = related.OfType<MimePart>()
            .FirstOrDefault(p => (p.ContentId ?? "").Trim('<', '>') == cid);

        if (binaryPart != null)
        {
            using var binaryStream = new MemoryStream();
            binaryPart.Content.DecodeTo(binaryStream);
            contentBytes = binaryStream.ToArray();
        }
        else
        {
            logger.LogWarning("xop:Include referenced Content-ID {Cid} but no matching MIME part was found.", cid);
        }
    }
    else if (contentEl != null && !string.IsNullOrEmpty(contentEl.Value))
    {
        contentBytes = Convert.FromBase64String(contentEl.Value); // fallback for non-MTOM callers
    }

    var objectId = $"obj-{Guid.NewGuid():N}"[..12];
    var labelSummary = string.Join(", ", labels.Select(l => $"{l.Item1}='{l.Item2}'"));

    logger.LogInformation(
        "UploadRepositoryObject: session={Session} file={File} bytes={Size} labels=[{Labels}]",
        sessionId, fileName, contentBytes.Length, labelSummary);

    return
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <soap:Envelope xmlns:soap="{SoapEnvNs}">
           <soap:Body>
             <UploadRepositoryObjectResponse xmlns="{AppNs}">
               <ObjectId>{objectId}</ObjectId>
               <SizeBytes>{contentBytes.Length}</SizeBytes>
               <Message>{Esc($"Stored (mock) {contentBytes.Length} byte(s) as '{fileName}' with {labels.Count} label(s).")}</Message>
             </UploadRepositoryObjectResponse>
           </soap:Body>
         </soap:Envelope>
         """;
}

string HandleLogout(XElement req)
{
    var sessionId = (string?)req.Element(appNs + "SessionId") ?? "";
    sessions.TryRemove(sessionId, out _);

    return
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <soap:Envelope xmlns:soap="{SoapEnvNs}">
           <soap:Body>
             <LogoutResponse xmlns="{AppNs}">
               <Message>Session {sessionId} closed (mock).</Message>
             </LogoutResponse>
           </soap:Body>
         </soap:Envelope>
         """;
}

string SoapFault(string faultCode, string faultString) =>
    $"""
     <?xml version="1.0" encoding="UTF-8"?>
     <soap:Envelope xmlns:soap="{SoapEnvNs}">
       <soap:Body>
         <soap:Fault>
           <faultcode>soap:{faultCode}</faultcode>
           <faultstring>{Esc(faultString)}</faultstring>
         </soap:Fault>
       </soap:Body>
     </soap:Envelope>
     """;

static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;

// Hand-build a single-part MTOM (multipart/related) response. There is no
// binary to optimize in most responses, so it is usually a single XOP part
// -- but the Content-Type still has to be multipart/related, because the
// WCF client's MtomMessageEncoder expects (and requires) that framing on
// replies too, once the binding is configured for MTOM.
static (byte[] Bytes, string ContentType) BuildMtomResponse(string soapXml)
{
    var boundary = $"MIME_boundary_{Guid.NewGuid():N}";
    var contentId = $"root.message@{Guid.NewGuid():N}";

    var body = new StringBuilder()
        .Append("--").Append(boundary).Append("\r\n")
        .Append("Content-Type: application/xop+xml; charset=UTF-8; type=\"text/xml\"\r\n")
        .Append("Content-Transfer-Encoding: 8bit\r\n")
        .Append("Content-ID: <").Append(contentId).Append(">\r\n\r\n")
        .Append(soapXml)
        .Append("\r\n--").Append(boundary).Append("--\r\n")
        .ToString();

    var bytes = Encoding.UTF8.GetBytes(body);
    var responseContentType =
        $"multipart/related; type=\"application/xop+xml\"; start=\"<{contentId}>\"; start-info=\"text/xml\"; boundary=\"{boundary}\"";

    return (bytes, responseContentType);
}
