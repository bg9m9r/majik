using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Copy;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flare of Duplication (Modern Horizons 3, {1}{R}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "You may sacrifice a nontoken red creature rather than pay this spell's
///    mana cost.
///    Copy target instant or sorcery spell. You may choose new targets for
///    the copy."
///
/// The red sibling of <see cref="FlareOfDenialFactory"/> (alt cost) crossed
/// with the Twincast / <see cref="ReverberateFactory"/> copy body.
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{R}{R}, red identity (MV 3). Base shape is
///   materialised from the embedded JSON definition
///   (<c>flare-of-duplication.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - NamedCardFactory dispatch via <c>[CardName]</c> source-generator.
/// - <b>Alternative cost (<see cref="SacrificeNontokenRedCreatureAlternativeCost"/>)</b>:
///   the caster may sacrifice a nontoken red creature they control on the
///   battlefield instead of paying {1}{R}{R}. No printed timing clause —
///   CR 118.9 applies, same posture as Flare of Denial.
/// - <b>Resolve — "copy target instant or sorcery spell"</b> (CR 707.10 /
///   706.10a): a distinct copy of the targeted instant/sorcery is put on the
///   stack above it, controlled by Flare of Duplication's controller, and
///   resolves first then ceases to exist (CR 707.10c). Shared with
///   Twincast / Reverberate via
///   <see cref="CopySpellFactory.CopyTargetInstantOrSorcery"/>.
/// - <b>"You may choose new targets for the copy"</b> (CR 707.10a): honoured —
///   the copy effect re-prompts the copier for new targets via
///   <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpellAsync"/>.
///
/// In production the copy body binds automatically through the oracle-text
/// spell-template path (<c>CopyTargetSpellTemplate</c>); this factory exposes
/// <see cref="BuildSpellDefinition"/> so the alt-cost / unit paths can build
/// the same resolution effect directly.
///
/// CR citations:
///   CR 118.9  — alternative cost
///   CR 701.18 — sacrifice
///   CR 707.10 / 706.10a — copy a spell + new targets for the copy
/// </summary>
[CardName("Flare of Duplication")]
public static class FlareOfDuplicationFactory
{
    public const string CardName = "Flare of Duplication";
    public const string Slug = "flare-of-duplication";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>
    /// Construct Flare of Duplication owned and controlled by
    /// <paramref name="owner"/> from the embedded JSON definition. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "copy target instant or sorcery spell. You may choose new
    /// targets for the copy" SpellDefinition (CR 707.10 / 707.10a). Delegates
    /// to the shared <see cref="CopySpellFactory.CopyTargetInstantOrSorcery"/>
    /// builder — identical body to Twincast / Reverberate.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to push the copy. Null in
    /// pure-shape tests; the effect becomes a no-op.</param>
    /// <param name="caster">The controller of the copy (CR 707.10).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(caster);

        return CopySpellFactory.CopyTargetInstantOrSorcery(targetResolver, stack, caster);
    }
}

/// <summary>
/// Bot probe: surfaces <see cref="SacrificeNontokenRedCreatureAlternativeCost"/>
/// candidates for Flare of Duplication during the heuristic bot's spell-cast
/// enumeration.
///
/// For each nontoken red creature the caster controls on the battlefield,
/// yields one <see cref="SacrificeNontokenRedCreatureAlternativeCost"/>
/// instance. No timing restriction is emitted — Flare of Duplication's alt
/// cost has no printed timing gate (same posture as
/// <see cref="FlareOfDenialAltCostProbe"/>).
/// </summary>
public sealed class FlareOfDuplicationAltCostProbe : IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Name != FlareOfDuplicationFactory.CardName) yield break;
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        foreach (var battlefield in caster.Zones.Battlefield.GetCards())
        {
            if (battlefield is not Permanent perm) continue;
            if (!perm.HasType(CardType.Creature)) continue;
            if (perm.IsToken) continue;
            if (!CardColors.GetColors(perm).Contains(ManaColor.Red)) continue;
            yield return new SacrificeNontokenRedCreatureAlternativeCost(perm);
        }
    }
}
