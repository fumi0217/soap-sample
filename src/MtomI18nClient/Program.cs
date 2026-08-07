using System.ServiceModel;
using System.ServiceModel.Channels;
using MtomI18nClient.Contracts;

// ---------------------------------------------------------------------------
// MtomI18nClient
//
// Walks through a full mock "document repository" SOAP flow against
// MockDocSuiteService, using MTOM (Message Transmission Optimization
// Mechanism) as the message encoding:
//
//   Login -> SearchRepositoryObjects -> UploadRepositoryObject -> Logout
//
// UploadRepositoryObject is the MTOM-relevant call: it carries an I18nString
// (multi-language label) plus a binary attachment. With
// MessageEncoding.Mtom on the binding, WCF ships that attachment as a raw
// MIME part referenced via <xop:Include> rather than inflating it ~33% as
// inline base64.
//
// This is a mock scenario invented for this sample -- it targets only the
// local MockDocSuiteService, not any real third-party system.
//
// Usage:
//   dotnet run -- [endpointUrl]
// ---------------------------------------------------------------------------

var endpointUrl = args.Length > 0 ? args[0] : "http://localhost:5205/MockDocSuite.svc";
var attachmentPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sample.bin");

var binding = new BasicHttpBinding(BasicHttpSecurityMode.None)
{
    MessageEncoding = WSMessageEncoding.Mtom,
    MaxReceivedMessageSize = 10 * 1024 * 1024,
    MaxBufferSize = 10 * 1024 * 1024,
};

var factory = new ChannelFactory<IMockDocSuiteService>(binding, new EndpointAddress(endpointUrl));
var channel = factory.CreateChannel();

try
{
    Console.WriteLine($"Endpoint     : {endpointUrl}");
    Console.WriteLine($"Message enc. : {binding.MessageEncoding} (MTOM)");
    Console.WriteLine();

    // 1. Login
    Console.WriteLine("--- Login ---");
    var loginResponse = channel.Login(new LoginRequest
    {
        UserId = "demo-user",
        CredentialType = "password",
        Credential = "demo-password", // mock server accepts anything non-empty
    });
    Console.WriteLine($"Success   : {loginResponse.Success}");
    Console.WriteLine($"SessionId : {loginResponse.SessionId}");
    Console.WriteLine($"Message   : {loginResponse.Message}");
    Console.WriteLine();

    if (!loginResponse.Success)
    {
        Console.Error.WriteLine("Login failed, aborting.");
        Environment.ExitCode = 1;
        return;
    }

    var sessionId = loginResponse.SessionId;

    // 2. Search
    Console.WriteLine("--- SearchRepositoryObjects ---");
    var searchResponse = channel.SearchRepositoryObjects(new SearchRepositoryObjectsRequest
    {
        SessionId = sessionId,
        Query = "quarterly-report",
    });
    foreach (var obj in searchResponse.Objects)
    {
        var labels = string.Join(", ", obj.ObjectClass.Select(l => $"{l.Lang}={l.Value}"));
        Console.WriteLine($"  [{obj.ObjectId}] {labels} (modified {obj.ModifiedDate})");
    }
    Console.WriteLine();

    // 3. Upload (this is the call that actually carries binary + i18n data over MTOM)
    Console.WriteLine("--- UploadRepositoryObject (MTOM) ---");
    var attachmentBytes = await File.ReadAllBytesAsync(attachmentPath);
    var uploadRequest = new UploadRepositoryObjectRequest
    {
        SessionId = sessionId,
        Label = new List<I18nLabel>
        {
            new() { Lang = "ja", Value = "ご注文ありがとうございます" },
            new() { Lang = "fr", Value = "Merci beaucoup" },
            new() { Lang = "de", Value = "Danke schön" },
            new() { Lang = "ko", Value = "감사합니다" },
            new() { Lang = "zh", Value = "谢谢惠顾" },
        },
        FileName = Path.GetFileName(attachmentPath),
        Content = attachmentBytes,
    };

    Console.WriteLine($"Label (i18n) : {string.Join(" / ", uploadRequest.Label.Select(l => $"[{l.Lang}] {l.Value}"))}");
    Console.WriteLine($"Attachment   : {uploadRequest.FileName} ({attachmentBytes.Length} bytes)");

    var uploadResponse = channel.UploadRepositoryObject(uploadRequest);
    Console.WriteLine($"ObjectId  : {uploadResponse.ObjectId}");
    Console.WriteLine($"SizeBytes : {uploadResponse.SizeBytes}");
    Console.WriteLine($"Message   : {uploadResponse.Message}");
    Console.WriteLine();

    // 4. Logout
    Console.WriteLine("--- Logout ---");
    var logoutResponse = channel.Logout(new LogoutRequest { SessionId = sessionId });
    Console.WriteLine($"Message   : {logoutResponse.Message}");

    ((IClientChannel)channel).Close();
    factory.Close();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Request failed: {ex}");
    ((IClientChannel)channel).Abort();
    factory.Abort();
    Environment.ExitCode = 1;
}
