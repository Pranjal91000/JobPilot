using JobPilot.Domain.Enums;

namespace JobPilot.Domain.ValueObjects;

public sealed record Provenance
{
    public VerificationStatus Status { get; init; }
    public FactSource Source { get; init; }
    public Guid? SourceDocumentId { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }

    // Required for EF Core parameterless constructor
    public Provenance()
    {
        Status = VerificationStatus.ExtractedUnconfirmed;
        Source = FactSource.ResumeExtraction;
    }

    public Provenance(VerificationStatus status, FactSource source, Guid? sourceDocumentId, DateTimeOffset? verifiedAt)
    {
        Status = status;
        Source = source;
        SourceDocumentId = sourceDocumentId;
        VerifiedAt = verifiedAt;
    }

    public static Provenance UserEntered(DateTimeOffset now) =>
        new(VerificationStatus.Verified, FactSource.UserEntered, null, now);

    public static Provenance Extracted(Guid docId) =>
        new(VerificationStatus.ExtractedUnconfirmed, FactSource.ResumeExtraction, docId, null);

    public static Provenance AiSuggested() =>
        new(VerificationStatus.AiSuggested, FactSource.AiSuggestion, null, null);

    public Provenance Confirm(DateTimeOffset now) =>
        new(VerificationStatus.Verified, Source, SourceDocumentId, now);
}

