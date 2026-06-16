using Majik.Core.Game;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 115 — the central legal-target enumerator. Maps a free-text target
/// <c>Description</c> (e.g. "any target", "target creature or player") to a
/// <see cref="TargetCategory"/> and enumerates the complete legal candidate
/// pool for that category against the live game state — creatures, players,
/// planeswalkers, permanents, stack spells, graveyard cards.
///
/// <para>
/// It is the FALLBACK pool used by <see cref="TargetCollection"/> ONLY when a
/// card ships no machine-readable candidates (its <c>TargetRequest</c> resolves
/// to an empty pool). Bespoke <c>CandidateGatherer</c>s (color / controller /
/// power-toughness / mana-value filters) win and are never consulted here.
/// </para>
///
/// <para>
/// For coarse exotic predicates ("creature with power 1 or less") the category
/// pool is intentionally broad (all creatures); the precise per-card rule is
/// enforced by the CR 608.2b resolution recheck in
/// <see cref="Majik.Core.Services.StackResolver"/> via the category-derived
/// <see cref="BuildLegalityPredicate"/>. The UI may highlight a hair more than
/// strictly legal; resolution is never illegal.
/// </para>
/// </summary>
public static partial class TargetCandidateService
{
    /// <summary>
    /// Most-specific-first classification of a free-text target description.
    /// "spell" / "graveyard" are matched before "creature"/"player" so
    /// "target creature spell" → CreatureSpell (not Creature) and
    /// "target card in a graveyard" → GraveyardCard (not a permanent slot).
    /// </summary>
    internal static TargetCategory Classify(string? description)
    {
        var d = (description ?? string.Empty).ToLowerInvariant().Trim();
        if (d.Length == 0 || d.Contains("no target")) return TargetCategory.None;

        if (d.Contains("graveyard")) return TargetCategory.GraveyardCard;
        if (d.Contains("noncreature spell")) return TargetCategory.NoncreatureSpell;
        if (d.Contains("creature spell")) return TargetCategory.CreatureSpell;
        if (d.Contains("spell")) return TargetCategory.Spell;

        if (d.Contains("any target")) return TargetCategory.AnyTarget;

        var hasCreature = d.Contains("creature");
        var hasPlayer = d.Contains("player");
        var hasPw = d.Contains("planeswalker");
        if (hasCreature && hasPlayer) return TargetCategory.CreatureOrPlayer;
        if (hasCreature && hasPw) return TargetCategory.CreatureOrPlaneswalker;
        if (hasPlayer && hasPw) return TargetCategory.PlayerOrPlaneswalker;
        if (hasCreature) return TargetCategory.Creature;
        if (hasPw) return TargetCategory.Planeswalker;
        if (d.Contains("opponent")) return TargetCategory.Opponent;
        if (hasPlayer) return TargetCategory.Player;

        if (d.Contains("nonland permanent")) return TargetCategory.NonlandPermanent;
        if (d.Contains("permanent")) return TargetCategory.Permanent;
        if (d.Contains("artifact")) return TargetCategory.Artifact;
        if (d.Contains("enchantment")) return TargetCategory.Enchantment;
        if (d.Contains("land")) return TargetCategory.Land;
        return TargetCategory.None;
    }
}

/// <summary>
/// CR 115 — the coarse target category a free-text target description maps to.
/// Drives <see cref="TargetCandidateService.GatherCandidates"/> (which pool to
/// enumerate) and <see cref="TargetCandidateService.BuildLegalityPredicate"/>
/// (the CR 608.2b resolution-recheck fallback).
/// </summary>
public enum TargetCategory
{
    None,
    AnyTarget,
    Creature,
    Player,
    Opponent,
    CreatureOrPlayer,
    CreatureOrPlaneswalker,
    PlayerOrPlaneswalker,
    Planeswalker,
    Permanent,
    NonlandPermanent,
    Artifact,
    Enchantment,
    Land,
    Spell,
    NoncreatureSpell,
    CreatureSpell,
    GraveyardCard,
}
