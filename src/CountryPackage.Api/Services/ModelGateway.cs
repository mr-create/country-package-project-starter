using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CountryPackage.Api.Services;

public sealed record ModelDraft(string Content, string ModelIdentifier);

public interface IModelGateway
{
    Task<ModelDraft> GenerateAsync(
        string countryCode,
        string purpose,
        string? existingDraft,
        string? reviewComment,
        IReadOnlyList<EvidencePassage> evidence,
        CancellationToken cancellationToken);
}

public sealed class ConfigurableModelGateway(HttpClient httpClient, IConfiguration configuration) : IModelGateway
{
    public async Task<ModelDraft> GenerateAsync(
        string countryCode,
        string purpose,
        string? existingDraft,
        string? reviewComment,
        IReadOnlyList<EvidencePassage> evidence,
        CancellationToken cancellationToken)
    {
        var endpoint = configuration["Copilot:ModelEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return new(DeterministicDraft(countryCode, purpose, existingDraft, reviewComment, evidence), "deterministic-local-poc");

        var model = configuration["Copilot:Model"] ?? "configured-model";
        var apiKey = configuration["Copilot:ApiKey"];
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (string.Equals(configuration["Copilot:ApiKeyHeader"], "api-key", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
            else
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        request.Content = JsonContent.Create(new
        {
            model,
            temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = "Prepare a concise Country Package draft using only the supplied fictional evidence. Cite claims with the supplied [n] markers, state evidence gaps, and never make an approval recommendation." },
                new { role = "user", content = BuildPrompt(countryCode, purpose, existingDraft, reviewComment, evidence) }
            }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(503, "copilot.model_unavailable", "The configured model provider is unavailable.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new ApiException(503, "copilot.invalid_model_response", "The configured model provider returned no usable content.");
        return new(content, model);
    }

    private static string DeterministicDraft(
        string countryCode,
        string purpose,
        string? existingDraft,
        string? reviewComment,
        IReadOnlyList<EvidencePassage> evidence)
    {
        var heading = string.IsNullOrWhiteSpace(reviewComment)
            ? $"{countryCode} Country Package — Evidence Draft"
            : $"{countryCode} Country Package — Revised Evidence Draft";
        var evidenceSection = string.Join("\n\n", evidence.Select((item, index) => $"- {item.Text} [{index + 1}]"));
        var revisionSection = string.IsNullOrWhiteSpace(reviewComment)
            ? ""
            : $"\n\n## Response to returned comment\n\nReviewer comment: {reviewComment.Trim()}\n\nThe evidence points above should be reviewed and edited to address this comment explicitly.";
        var existingSection = string.IsNullOrWhiteSpace(existingDraft)
            ? ""
            : $"\n\n## Existing Editor draft\n\n{existingDraft.Trim()}";
        return $"# {heading}\n\n## Purpose\n\n{purpose}\n\n## Evidence summary\n\n{evidenceSection}{revisionSection}{existingSection}\n\n## Editor review required\n\nConfirm every claim, citation, date, and management implication before accepting this document.";
    }

    private static string BuildPrompt(
        string countryCode,
        string purpose,
        string? existingDraft,
        string? reviewComment,
        IReadOnlyList<EvidencePassage> evidence) =>
        $"Country: {countryCode}\nPurpose: {purpose}\nReturned comment: {reviewComment ?? "None"}\nExisting draft: {existingDraft ?? "None"}\n\nAuthorized evidence:\n" +
        string.Join("\n", evidence.Select((item, index) => $"[{index + 1}] {item.Source}: {item.Text}"));
}
