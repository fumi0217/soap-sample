using System.Runtime.Serialization;
using System.ServiceModel;

namespace MtomI18nClient.Contracts;

// ---------------------------------------------------------------------------
// MockDocSuite: an original, mock SOAP contract.
//
// It models a *generic* pattern common to enterprise document/workflow
// repository services: session-based login, an "I18nString"-style
// language-tagged label type, object search, and object upload carrying a
// binary payload (the part MTOM optimizes). It is not derived from,
// affiliated with, or a reproduction of any real company's product,
// namespace, or WSDL -- every name and behavior below is invented for this
// sample.
// ---------------------------------------------------------------------------

public static class MockDocSuiteNs
{
    public const string Value = "http://example.com/mockdocsuite";
}

[ServiceContract(Namespace = MockDocSuiteNs.Value, Name = "MockDocSuite")]
public interface IMockDocSuiteService
{
    [OperationContract(
        Action = MockDocSuiteNs.Value + "/Login",
        ReplyAction = MockDocSuiteNs.Value + "/LoginResponse")]
    LoginResponse Login(LoginRequest request);

    [OperationContract(
        Action = MockDocSuiteNs.Value + "/SearchRepositoryObjects",
        ReplyAction = MockDocSuiteNs.Value + "/SearchRepositoryObjectsResponse")]
    SearchRepositoryObjectsResponse SearchRepositoryObjects(SearchRepositoryObjectsRequest request);

    [OperationContract(
        Action = MockDocSuiteNs.Value + "/UploadRepositoryObject",
        ReplyAction = MockDocSuiteNs.Value + "/UploadRepositoryObjectResponse")]
    UploadRepositoryObjectResponse UploadRepositoryObject(UploadRepositoryObjectRequest request);

    [OperationContract(
        Action = MockDocSuiteNs.Value + "/Logout",
        ReplyAction = MockDocSuiteNs.Value + "/LogoutResponse")]
    LogoutResponse Logout(LogoutRequest request);
}

/// <summary>
/// A single language-tagged label, e.g. {Lang="ja", Value="日本語ラベル"}.
/// A list of these stands in for an "I18nString" (internationalized,
/// multi-language string) type, the pattern this sample is built around.
/// </summary>
[DataContract(Namespace = MockDocSuiteNs.Value)]
public class I18nLabel
{
    [DataMember(Order = 0)]
    public string Lang { get; set; } = string.Empty;

    [DataMember(Order = 1)]
    public string Value { get; set; } = string.Empty;
}

// ----- Login ----------------------------------------------------------

[MessageContract(IsWrapped = true, WrapperName = "LoginRequest", WrapperNamespace = MockDocSuiteNs.Value)]
public class LoginRequest
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string UserId { get; set; } = string.Empty;

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 1)]
    public string CredentialType { get; set; } = "password";

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 2)]
    public string Credential { get; set; } = string.Empty;
}

[MessageContract(IsWrapped = true, WrapperName = "LoginResponse", WrapperNamespace = MockDocSuiteNs.Value)]
public class LoginResponse
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public bool Success { get; set; }

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 2)]
    public string Message { get; set; } = string.Empty;
}

// ----- SearchRepositoryObjects -----------------------------------------

[MessageContract(IsWrapped = true, WrapperName = "SearchRepositoryObjectsRequest", WrapperNamespace = MockDocSuiteNs.Value)]
public class SearchRepositoryObjectsRequest
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string SessionId { get; set; } = string.Empty;

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 1)]
    public string Query { get; set; } = string.Empty;
}

[DataContract(Namespace = MockDocSuiteNs.Value)]
public class RepositoryObjectSummary
{
    [DataMember(Order = 0)]
    public string ObjectId { get; set; } = string.Empty;

    /// <summary>The object's display class/title as an I18nString (list of I18nLabel).</summary>
    [DataMember(Order = 1)]
    public List<I18nLabel> ObjectClass { get; set; } = new();

    [DataMember(Order = 2)]
    public string ModifiedDate { get; set; } = string.Empty;
}

[MessageContract(IsWrapped = true, WrapperName = "SearchRepositoryObjectsResponse", WrapperNamespace = MockDocSuiteNs.Value)]
public class SearchRepositoryObjectsResponse
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public List<RepositoryObjectSummary> Objects { get; set; } = new();
}

// ----- UploadRepositoryObject (the MTOM-relevant operation) ------------

[MessageContract(IsWrapped = true, WrapperName = "UploadRepositoryObjectRequest", WrapperNamespace = MockDocSuiteNs.Value)]
public class UploadRepositoryObjectRequest
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>I18nString: the object's display label in one or more languages.</summary>
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 1)]
    public List<I18nLabel> Label { get; set; } = new();

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 2)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The binary payload MTOM optimizes. With MessageEncoding.Mtom on the
    /// binding, WCF transmits this as a raw MIME part referenced by
    /// &lt;xop:Include&gt; instead of inline base64.
    /// </summary>
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 3)]
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

[MessageContract(IsWrapped = true, WrapperName = "UploadRepositoryObjectResponse", WrapperNamespace = MockDocSuiteNs.Value)]
public class UploadRepositoryObjectResponse
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string ObjectId { get; set; } = string.Empty;

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 1)]
    public int SizeBytes { get; set; }

    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 2)]
    public string Message { get; set; } = string.Empty;
}

// ----- Logout ------------------------------------------------------------

[MessageContract(IsWrapped = true, WrapperName = "LogoutRequest", WrapperNamespace = MockDocSuiteNs.Value)]
public class LogoutRequest
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string SessionId { get; set; } = string.Empty;
}

[MessageContract(IsWrapped = true, WrapperName = "LogoutResponse", WrapperNamespace = MockDocSuiteNs.Value)]
public class LogoutResponse
{
    [MessageBodyMember(Namespace = MockDocSuiteNs.Value, Order = 0)]
    public string Message { get; set; } = string.Empty;
}
