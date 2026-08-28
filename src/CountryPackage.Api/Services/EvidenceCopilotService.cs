using System.Text.RegularExpressions;
using CountryPackage.Api.Auth;
using CountryPackage.Api.Contracts;
using CountryPackage.Api.Domain;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CountryPackage.Api.Services;

public sealed record EvidencePassage(string Source, string Text);

public sealed class EvidenceCopilotService(
    ICurrentUser currentUser,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    TimeProvider clock,
    IModelGateway modelGateway)
{
    private const string WorkflowVersion = "evidence-preparation-v1";

    public async Task<CopilotPrepareResponse> PrepareAsync(CopilotPrepareRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.User ?? throw new ApiException(401, "identity.required", "A valid development identity is required.");
        var country = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
        if (country.Length != 3 || country.Any(x => x is < 'A' or > 'Z'))
            throw new ApiException(400, "copilot.invalid_country", "Country code must be a three-letter uppercase code.");
        if (request.Instructions?.Length > 5000 || request.ReviewComment?.Length > 2000 || request.ExistingDraft?.Length > 100000)
            throw new ApiException(400, "copilot.input_too_large", "Copilot inputs exceed the configured bounds.");
        if (user.Role != UserRole.CountryEditor)
            throw new ApiException(403, "authorization.role", "Only Country Editors can use the Evidence Copilot.");
        if (!user.CountryScopes.Contains(country))
            throw new ApiException(404, "resource.not_found", "No authorized evidence was found.");

        var evidence = LoadEvidence(country);
        if (evidence.Count == 0)
        {
            var emptyManifest = new EvidenceManifestInput([], [], ["No authorized evidence was available for this country."], WorkflowVersion, "no-model-invoked", clock.GetUtcNow());
            return new CopilotPrepareResponse(
                request.ExistingDraft ?? "",
                [],
                ["Insufficient evidence: add governed country sources before preparing this package."],
                emptyManifest);
        }

        var query = string.Join(' ', new[] { request.Instructions, request.ReviewComment, request.ExistingDraft }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var selected = Rank(evidence, query).Take(5).ToList();
        var citations = selected.Select((x, index) => $"[{index + 1}] {x.Source}").ToList();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Instructions) && string.IsNullOrWhiteSpace(request.ReviewComment))
            warnings.Add("No specific instruction was supplied; the draft uses the highest-ranked country evidence.");

        var purpose = string.IsNullOrWhiteSpace(request.Instructions)
            ? "Prepare a concise management package grounded in the governed sources."
            : request.Instructions.Trim();
        var generated = await modelGateway.GenerateAsync(country, purpose, request.ExistingDraft, request.ReviewComment, selected, cancellationToken);
        var manifest = new EvidenceManifestInput(
            selected.Select(x => x.Source).Distinct().ToList(),
            citations,
            warnings,
            WorkflowVersion,
            generated.ModelIdentifier,
            clock.GetUtcNow());
        return new(generated.Content, citations, warnings, manifest);
    }

    public byte[] Export(CopilotExportRequest request)
    {
        var user = currentUser.User ?? throw new ApiException(401, "identity.required", "A valid development identity is required.");
        if (user.Role != UserRole.CountryEditor)
            throw new ApiException(403, "authorization.role", "Only Country Editors can export Copilot drafts.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            throw new ApiException(400, "copilot.invalid_title", "The document title must contain between 1 and 200 characters.");
        if (string.IsNullOrWhiteSpace(request.Draft))
            throw new ApiException(400, "copilot.empty_draft", "The draft cannot be empty.");
        if (request.Draft.Length > 100000)
            throw new ApiException(400, "copilot.draft_too_large", "The draft exceeds the export limit.");

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            body.Append(CreateParagraph(request.Title.Trim(), true));
            foreach (var line in request.Draft.Replace("\r", "").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    body.Append(new Paragraph());
                    continue;
                }

                var isHeading = line.StartsWith('#');
                body.Append(CreateParagraph(line.TrimStart('#', ' '), isHeading));
            }
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private List<EvidencePassage> LoadEvidence(string countryCode)
    {
        var configured = configuration["Storage:SourceDirectory"] ?? "sources";
        var directory = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        if (!Directory.Exists(directory))
            return [];

        var passages = new List<EvidencePassage>();
        foreach (var path in Directory.EnumerateFiles(directory, $"{countryCode}-*.docx", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var document = WordprocessingDocument.Open(path, false);
                var paragraphs = document.MainDocumentPart?.Document.Body?
                    .Descendants<Paragraph>()
                    .Select(x => x.InnerText.Trim())
                    .Where(x => x.Length >= 30) ?? [];
                passages.AddRange(paragraphs.Select(x => new EvidencePassage(Path.GetFileName(path), x)));
            }
            catch (Exception exception) when (exception is IOException or OpenXmlPackageException)
            {
                // Invalid repository files are skipped; validation findings expose insufficient evidence.
            }
        }
        return passages;
    }

    private static IEnumerable<EvidencePassage> Rank(IEnumerable<EvidencePassage> evidence, string query)
    {
        var terms = Regex.Matches(query.ToLowerInvariant(), "[a-z]{4,}").Select(x => x.Value).ToHashSet();
        return evidence
            .Select((passage, index) => new
            {
                Passage = passage,
                Score = terms.Count(term => passage.Text.Contains(term, StringComparison.OrdinalIgnoreCase)),
                Index = index
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Passage);
    }

    private static Paragraph CreateParagraph(string text, bool heading)
    {
        var runProperties = heading ? new RunProperties(new Bold(), new FontSize { Val = "28" }) : null;
        var run = new Run();
        if (runProperties is not null) run.Append(runProperties);
        run.Append(new Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
        return new Paragraph(run);
    }
}
