using ClocktowerDemo.Domain;
using ClocktowerDemo.Services.Politeness;
using System;
using System.Threading;
using System.Threading.Tasks;

public class HeuristicPolitenessDetector : IPolitenessDetector
{
    public Task<PolitenessResult> ClassifyAsync(string text, CancellationToken ct)
    {
        text ??= string.Empty;
        int score = 0;

        if (text.Contains("please", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could you", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("would you", StringComparison.OrdinalIgnoreCase))
            score += 2;

        if (text.Contains("thank", StringComparison.OrdinalIgnoreCase))
            score += 2;

        if (text.Contains("now!", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("right now", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hurry", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tell me", StringComparison.OrdinalIgnoreCase))
            score -= 2;

        if (text.EndsWith('!')) score -= 1;
        if (text.Length < 4) score -= 1;

        string label = score >= 2 ? "polite" : score <= -1 ? "impolite" : "neutral";
        double conf = Math.Clamp(Math.Abs(score) / 3.0, 0.3, 0.95);
        return Task.FromResult(new PolitenessResult(label, conf));
    }
}