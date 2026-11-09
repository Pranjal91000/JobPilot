using JobPilot.Domain.Enums;

namespace JobPilot.Application.Features.Mapping;

public sealed class ConfidenceCalculator : IConfidenceCalculator
{
    public (decimal FinalScore, ConfidenceBreakdown Breakdown) Calculate(
        MappingMethod method,
        decimal labelMatch,
        decimal optionMatch,
        decimal grounding,
        decimal llmSelfScore,
        decimal historyAccuracy)
    {
        decimal methodCap = method switch
        {
            MappingMethod.AdapterRule => 0.99m,
            MappingMethod.Dictionary => 0.97m,
            MappingMethod.SavedAnswer => 0.97m,
            MappingMethod.Llm => 0.92m,
            _ => 0.70m
        };

        if (grounding <= 0)
        {
            var zeroBreakdown = new ConfidenceBreakdown(methodCap, labelMatch, optionMatch, 0m, llmSelfScore, historyAccuracy, 0m);
            return (0m, zeroBreakdown);
        }

        decimal llmFactor = method == MappingMethod.Llm
            ? (0.5m + 0.5m * Math.Clamp(llmSelfScore, 0m, 1m))
            : 1.0m;

        decimal raw = labelMatch * optionMatch * grounding * llmFactor * historyAccuracy;
        decimal finalScore = Math.Round(Math.Min(methodCap, raw), 2);

        var breakdown = new ConfidenceBreakdown(
            MethodCap: methodCap,
            LabelMatch: labelMatch,
            OptionMatch: optionMatch,
            Grounding: grounding,
            LlmSelfScore: llmSelfScore,
            HistoryAccuracy: historyAccuracy,
            FinalScore: finalScore);

        return (finalScore, breakdown);
    }
}

