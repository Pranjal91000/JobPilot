using System.Text.Json;
using FluentAssertions;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobPilot.Application.Tests;

public class AnswerDraftingTests
{
    private sealed class FakeAi(string json) : IAiProvider
    {
        public string Name => "fake";
        public AiCapabilities Capabilities => new(true, 1000, true);
        public Task<AiStructuredResponse> CompleteStructuredAsync(AiStructuredRequest request, CancellationToken ct) =>
            Task.FromResult(new AiStructuredResponse(true, JsonDocument.Parse(json), true, json, 0, 0, TimeSpan.Zero, "fake", null));
    }

    private static readonly DraftContext Context = new(
        "Acme", "Engineer", "Build things.",
        new Dictionary<string, string> { ["Personal.FirstName"] = "Ada" },
        []);

    [Fact]
    public async Task Choice_answers_must_be_one_of_the_options()
    {
        var relocate = Guid.NewGuid();
        var source = Guid.NewGuid();
        var json = $$"""
            { "answers": [
              { "fieldRef": "{{relocate}}", "value": "yes", "confidence": 0.9, "reason": "" },
              { "fieldRef": "{{source}}", "value": "A friend told me", "confidence": 0.9, "reason": "" }
            ] }
            """;
        var svc = new AnswerDraftingService(new FakeAi(json), NullLogger<AnswerDraftingService>.Instance);

        var drafts = await svc.DraftAsync(new[]
        {
            new DraftQuestion(relocate, "Willing to relocate?", FieldType.Radio, true, new[] { "Yes", "No" }),
            new DraftQuestion(source, "How did you hear about us?", FieldType.SingleSelect, true, new[] { "LinkedIn", "Referral" }),
        }, Context, CancellationToken.None);

        drafts.Should().ContainSingle();
        drafts[0].FieldId.Should().Be(relocate);
        drafts[0].Value.Should().Be("Yes"); // normalised to the option's exact text
    }

    [Fact]
    public async Task Null_and_unknown_answers_are_dropped()
    {
        var q = Guid.NewGuid();
        var json = $$"""
            { "answers": [
              { "fieldRef": "{{q}}", "value": null, "confidence": 0.1, "reason": "no facts" },
              { "fieldRef": "{{Guid.NewGuid()}}", "value": "stray", "confidence": 0.9, "reason": "" }
            ] }
            """;
        var svc = new AnswerDraftingService(new FakeAi(json), NullLogger<AnswerDraftingService>.Instance);

        var drafts = await svc.DraftAsync(new[] { new DraftQuestion(q, "Why us?", FieldType.Text, true, []) }, Context, CancellationToken.None);

        drafts.Should().BeEmpty();
    }
}
