using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Metalwork Colossus (Kaladesh, {11}).
///
/// Artifact Creature — Construct 10/10. Oracle text (verified against
/// Scryfall):
///   "This spell costs {X} less to cast, where X is the total mana value
///    of noncreature artifacts you control.
///    Sacrifice two artifacts: Return this card from your graveyard to
///    your hand."
///
/// The base shape (name, Creature + Artifact types, Construct subtype,
/// {11}, 10/10) is materialised from the embedded JSON definition
/// (<c>metalwork-colossus.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two abilities are
/// layered on top here.
///
/// ## Implemented (v1)
///
/// - <b>10/10 Artifact Creature — Construct</b> at {11}. The JSON lists
///   <c>["Creature", "Artifact"]</c> so <see cref="CardDefinitionFactory.Build"/>
///   materialises a <see cref="Creature"/> shell with the
///   <see cref="CardType.Artifact"/> type additively stamped (CR 301.1 /
///   302.1) — same posture as Alpha Myr / Memnite.
///
/// - <b>Self cost-reduction static (CR 117.7 / CR 601.2f)</b>: "This spell
///   costs {X} less to cast, where X is the total mana value of noncreature
///   artifacts you control." Wired via the whole-reduction
///   (<see cref="CostReductionAbility.TotalReducer"/>) shape — the same
///   constructor Domain (Tribal Flames / Scion of Draco) uses — because the
///   reduction is a live tally ("total mana value of …"), not a flat
///   per-instance amount. The reducer is printed ON the card itself, so
///   <see cref="CostReduction.GetEffectiveCost"/> consults it at cast time
///   and scans the caster's battlefield. CR 117.7c — only generic mana is
///   reduced (the printed cost is all-generic {11}, so the whole cost can be
///   driven down) and the cost floors at zero.
///   <list type="bullet">
///     <item><b>"noncreature artifacts"</b> — predicate is
///       <c>HasType(Artifact) &amp;&amp; !HasType(Creature)</c>. Metalwork
///       Colossus itself is an Artifact Creature, so it never counts toward
///       its own discount even if a copy were on the battlefield, and (being
///       on the stack at cost-calc time) it isn't on the battlefield anyway.</item>
///     <item><b>"total mana value"</b> — sums
///       <see cref="Card.ManaCostValue"/>.TotalValue across the matching
///       permanents (CR 202.3 — a permanent's mana value is derived from its
///       mana cost). An artifact land (mana value 0) contributes 0.</item>
///     <item><b>"you control"</b> — the reducer is scoped to the caster's
///       battlefield by <see cref="CostReduction.GetEffectiveCost"/> (the
///       printed reducer lives on the spell being cast, and the helper
///       passes the caster as the battlefield scope).</item>
///   </list>
///
/// - <b>Sacrifice-two-artifacts graveyard recursion (CR 602 / CR 701.16)</b>:
///   "Sacrifice two artifacts: Return this card from your graveyard to your
///   hand." An <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="SacrificeTwoArtifactsCost"/> and whose effect returns this
///   card from the graveyard to its owner's hand via
///   <see cref="Fx.ReturnFromGraveyardToHand"/>. The effect guards on the
///   card being in the graveyard before acting (CR 608.2 — the ability does
///   nothing if its source has left the graveyard), the same no-op-shaped
///   posture Slogurk's counter-removal bounce uses. Routes through the
///   supplied <see cref="ZoneService"/> when wired so the graveyard → hand
///   move publishes <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice target prompting</b>: <see cref="SacrificeTwoArtifactsCost"/>
///   picks the first two eligible artifacts deterministically when the agent
///   hasn't set its targets — same gap documented on that cost shape (shared
///   with Sai, Master Thopterist).
/// - <b>Graveyard-zone activation gating</b>: the engine doesn't yet model
///   "activate only from the graveyard" as a first-class
///   <see cref="ActivatedAbility"/> zone restriction; the effect-body
///   graveyard guard makes an off-graveyard invocation a clean no-op (same
///   posture as Samwise Gamgee's graveyard-return / Slogurk's counter
///   bounce).
/// </summary>
[CardName("Metalwork Colossus")]
public static class MetalworkColossusFactory
{
    public const string CardName = "Metalwork Colossus";
    public const string Slug = "metalwork-colossus";

    /// <summary>
    /// Single-arg dispatcher path. Attaches both abilities to the card shape
    /// without <see cref="ZoneService"/> wiring (the graveyard-return move
    /// uses the direct-zone fallback). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Metalwork Colossus. When <paramref name="zoneService"/> is
    /// supplied, the sacrifice-two-artifacts graveyard return routes through
    /// <see cref="ZoneService.MoveCard"/> so the graveyard → hand move
    /// publishes <see cref="Majik.Core.Events.CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Construct subtype, {11}, 10/10). The JSON carries no
        // abilities — both riders are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {X} less to cast, where X is the
        // total mana value of noncreature artifacts you control."
        //
        // Whole-reduction (TotalReducer) shape: the reduction is a live
        // tally, not a flat per-instance amount, so the function returns the
        // full generic-mana reduction for the caster. Floor-at-zero is
        // enforced by CostReduction.GetEffectiveCost (CR 117.7c).
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: NoncreatureArtifactManaValue,
            description:
                "This spell costs {X} less to cast, where X is the total mana " +
                "value of noncreature artifacts you control."));

        // ----------------------------------------------------------------
        // CR 602 / CR 701.16 — "Sacrifice two artifacts: Return this card
        // from your graveyard to your hand." Mana cost is none; the only
        // cost is sacrificing two artifacts.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to owner's hand",
            () =>
            {
                // CR 608.2 — the ability does nothing if its source has left
                // the graveyard by resolution (no first-class graveyard-zone
                // activation gate yet; mirrors Slogurk / Samwise posture).
                if (card.Zone != ZoneType.Graveyard) return;
                Fx.ReturnFromGraveyardToHand(card, zoneService);
            });

        var returnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeTwoArtifactsCost(excludeSource: card) },
            effects: new IEffect[] { returnEffect });

        card.AddAbility(returnAbility);

        return card;
    }

    /// <summary>
    /// CR 117.7 — total mana value of noncreature artifacts the caster
    /// controls. Pure helper exposed for tests; mirrors the tally consulted
    /// by the printed <see cref="CostReductionAbility.TotalReducer"/>.
    ///
    /// A permanent counts when it has <see cref="CardType.Artifact"/> and is
    /// NOT a <see cref="CardType.Creature"/> ("noncreature artifacts"). Mana
    /// value is read from <see cref="Card.ManaCostValue"/> (CR 202.3); an
    /// artifact land (mana value 0) contributes 0.
    /// </summary>
    public static int NoncreatureArtifactManaValue(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var total = 0;
        foreach (var c in caster.Zones.Battlefield.GetCards())
        {
            if (!c.HasType(CardType.Artifact)) continue;
            if (c.HasType(CardType.Creature)) continue;
            if (c is Card card) total += card.ManaCostValue.TotalValue;
        }
        return total;
    }
}
