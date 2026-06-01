using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// PLAN 01 (Slice F) — translates the free-form <c>TargetFilter</c> /
/// <c>target</c> strings carried by the JSON / DSL targeting effects
/// (<see cref="DealDamageEffectDef"/> / <see cref="DestroyTargetEffectDef"/> /
/// <see cref="UntapTargetEffectDef"/>) into a
/// <see cref="TargetRequest"/> for the shared
/// <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline.
///
/// <para>
/// Each request carries a 1..1 cardinality and a
/// <see cref="TargetRequest.CandidateGatherer"/> that enumerates the live
/// legal candidates against the resolving <see cref="GameContext"/> — exactly
/// what a hand-written factory supplies. The gatherer's predicate IS the
/// resolution-time legality the effect re-checks (CR 608.2b), so the agent is
/// only ever offered legal picks and an illegal pick fizzles cleanly.
/// </para>
/// </summary>
public static class TargetFilters
{
    /// <summary>
    /// Build the <see cref="TargetRequest"/> for the given filter string.
    /// <paramref name="verb"/> is woven into the request description only
    /// (e.g. "destroy", "untap", "deal 1 damage"). Unknown filters fall back
    /// to the broadest legal pool (any target) rather than throwing, so a new
    /// JSON card never hard-fails on an unrecognised filter — it simply
    /// targets widely (the resolution-time guard still enforces the printed
    /// rule for the verbs that gate on type).
    /// </summary>
    public static TargetRequest ToTargetRequest(string? filter, string verb)
    {
        var normalized = (filter ?? "").Trim().ToLowerInvariant();
        var (description, predicate) = Resolve(normalized, verb);
        return new TargetRequest(
            Description: description,
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            CandidateGatherer: ctx => Gather(ctx, predicate));
    }

    private static (string Description, Func<object, bool> Predicate) Resolve(string filter, string verb) =>
        filter switch
        {
            "any" or "any_target" or "creature_or_player" =>
                ($"{verb} to any target", IsAnyTarget),
            "creature" =>
                ($"{verb} target creature", o => o is Creature c && OnBattlefield(c)),
            "permanent" =>
                ($"target permanent to {verb}", o => o is Permanent p && OnBattlefield(p)),
            "legendary_permanent" =>
                ($"target legendary permanent to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasSupertype(CardSupertype.Legendary)),
            "legendary_creature" =>
                ($"target legendary creature to {verb}",
                    o => o is Creature c && OnBattlefield(c) && c.HasSupertype(CardSupertype.Legendary)),
            "nonbasic_land" =>
                ($"target nonbasic land to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && p.HasType(CardType.Land) && !p.HasSupertype(CardSupertype.Basic)),
            "artifact_enchantment_nonbasic_land" =>
                ($"target artifact, enchantment, or nonbasic land to {verb}",
                    IsArtifactEnchantmentOrNonbasicLand),
            "artifact" =>
                ($"target artifact to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasType(CardType.Artifact)),
            "enchantment" =>
                ($"target enchantment to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasType(CardType.Enchantment)),
            // Unknown filter — fall back to any target (broadest legal pool).
            _ => ($"{verb} target ({filter})", IsAnyTarget),
        };

    /// <summary>
    /// CR 115.3 — "any target" = a creature, a player, a planeswalker, or a
    /// battle. Battles are not modelled, so this is creature / player /
    /// planeswalker.
    /// </summary>
    private static bool IsAnyTarget(object o) => o switch
    {
        Player => true,
        Planeswalker pw => OnBattlefield(pw),
        Creature c => OnBattlefield(c),
        _ => false,
    };

    private static bool IsArtifactEnchantmentOrNonbasicLand(object o)
    {
        if (o is not Permanent p || !OnBattlefield(p)) return false;
        if (p.HasType(CardType.Artifact)) return true;
        if (p.HasType(CardType.Enchantment)) return true;
        return p.HasType(CardType.Land) && !p.HasSupertype(CardSupertype.Basic);
    }

    private static bool OnBattlefield(ICard card) => card.Zone == ZoneType.Battlefield;

    /// <summary>
    /// Enumerate the live legal candidates from the resolving context:
    /// every player plus every battlefield permanent across all players,
    /// filtered by <paramref name="predicate"/>.
    /// </summary>
    private static IReadOnlyList<object> Gather(GameContext ctx, Func<object, bool> predicate)
    {
        var result = new List<object>();
        foreach (var player in ctx.AllPlayers)
        {
            if (predicate(player)) result.Add(player);

            var battlefield = player.Zones?.Battlefield;
            if (battlefield == null) continue;
            foreach (var card in battlefield.GetCards())
            {
                if (predicate(card)) result.Add(card);
            }
        }
        return result;
    }
}
