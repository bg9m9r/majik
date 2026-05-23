using Majik.Bot.Diagnostics;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks targets for a TargetRequest. v1: from the legal candidate set,
/// prefer opponent's highest-power creatures. If no creatures, prefer
/// the opponent themselves; otherwise pick the first candidate.
/// </summary>
public static class TargetPolicy
{
    public static IReadOnlyList<object> Pick(
        GameContext ctx, Player self, TargetRequest request,
        IBotDecisionSink? sink = null)
    {
        if (request.LegalCandidates.Count == 0)
            return Array.Empty<object>();

        // Score every candidate so we can both rank-pick and emit alternatives.
        var scored = request.LegalCandidates
            .Select(c => (candidate: c, score: Score(c, self)))
            .OrderByDescending(s => s.score)
            .ToList();

        var count = Math.Min(request.MaxTargets, scored.Count);
        var chosen = scored.Take(count).Select(s => s.candidate).ToList();

        EmitDecision(ctx, self, request, chosen, scored, sink);
        return chosen;
    }

    private static void EmitDecision(
        GameContext ctx, Player self, TargetRequest request,
        IReadOnlyList<object> chosen,
        IReadOnlyList<(object candidate, double score)> scored,
        IBotDecisionSink? sink)
    {
        if (sink is null || ReferenceEquals(sink, NullBotDecisionSink.Instance)) return;

        var chosenLabel = $"Target:{{{string.Join(",", chosen.Select(LabelCandidate))}}}";
        // Score the chosen set = sum of picked scores (mirrors how Pick picks top-N).
        var chosenSet = new HashSet<object>(chosen, ReferenceEqualityComparer.Instance);
        var chosenScore = scored.Where(s => chosenSet.Contains(s.candidate)).Sum(s => s.score);

        var alts = scored
            .Where(s => !chosenSet.Contains(s.candidate))
            .Take(3)
            .Select(s => new BotDecisionAlternative(LabelCandidate(s.candidate), s.score))
            .ToList();

        var ctxFlags = new Dictionary<string, string>
        {
            ["turn"] = ctx.TurnNumber.ToString(),
            ["phase"] = ctx.CurrentPhase?.ToString() ?? "null",
            ["candidateCount"] = scored.Count.ToString(),
            ["minTargets"] = request.MinTargets.ToString(),
            ["maxTargets"] = request.MaxTargets.ToString(),
            ["intent"] = request.Intent.ToString(),
        };
        if (!string.IsNullOrEmpty(request.Description))
            ctxFlags["request"] = request.Description;
        if (scored.Count == 1) ctxFlags["forced"] = "true";

        try
        {
            sink.Record(new BotDecision(
                DecisionType: "Target",
                Chosen: chosenLabel,
                ChosenScore: chosenScore,
                Alternatives: alts,
                Context: ctxFlags));
        }
        catch { /* observer fault must not abort engine */ }
    }

    private static string LabelCandidate(object c) => c switch
    {
        Creature crt => $"{crt.Name}({crt.Power}/{crt.Toughness})",
        ICard card => card.Name ?? card.GetType().Name,
        Player p => $"Player:{p.Name}",
        _ => c.GetType().Name,
    };

    private static double Score(object candidate, Player self)
    {
        if (candidate is Creature crt)
        {
            var ownedBySelf = ReferenceEquals(crt.Controller ?? crt.Owner, self);
            return ownedBySelf ? -100 : crt.Power * 10 + crt.Toughness;
        }
        if (candidate is Player p && !ReferenceEquals(p, self))
            return 5;
        return 0;
    }
}
