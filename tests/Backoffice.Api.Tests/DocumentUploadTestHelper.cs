using Backoffice.Application.Policy;
using Backoffice.Domain.Documents;

namespace Backoffice.Api.Tests;

/// <summary>
/// Builds the multipart/form-data request `POST /v1/cases/{caseId}/documents` now requires
/// (see DocumentsEndpoints.cs) — shared across every test that registers a document as setup,
/// since they'd otherwise all duplicate the same multipart-construction boilerplate.
/// </summary>
public static class DocumentUploadTestHelper
{
    public static HttpRequestMessage BuildRequest(
        Guid caseId, long expectedVersion, DocumentType documentType, MediaType mediaType, string fileName, byte[]? fileBytes = null)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(documentType.ToWireString()), "documentType" },
            { new StringContent(mediaType.ToWireString()), "mediaType" },
        };
        content.Add(new ByteArrayContent(fileBytes ?? "test document content"u8.ToArray()), "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{caseId}/documents") { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", expectedVersion.ToString());
        return request;
    }
}
