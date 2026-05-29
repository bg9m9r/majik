using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Simian Spirit Guide (Planar Chaos, {2}{R}).
///
/// Creature — Ape Spirit 2/2. Oracle text:
///   "Exile this card from your hand: Add {R}."
///
/// ## Implemented (v1)
/// - Card identity (Creature — Ape Spirit 2/2 {2}{R}) is loaded from
///   <c>Majik.Core/CardData/Cards/simian-spirit-guide.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
///   through <see cref="CardDefinitionFactory"/> — same data-driven
///   identity route as <see cref="DelightedHalflingFactory"/>.
/// - "Exile this card from your hand: Add {R}" is a mana ability
///   (CR 605.1a — it could add mana and has no target; it doesn't use
///   the stack). It is attached in C# rather than via the JSON ability
///   schema because the JSON <c>"kind": "mana"</c> shape only models a
///   battlefield {T} ability — it has no representation for an
///   exile-this-from-hand activation cost or a hand-zone activation
///   gate. Same reason Lotus Petal / Lotus Bloom hand-build their
///   sacrifice-rider mana abilities in C#.
/// - The ability uses the no-tap <see cref="ManaAbility"/> overload
///   (<c>tapsAsCost: false</c>): the card is in the hand, never on the
///   battlefield, so there is nothing to tap. CR 602.5 — "Exile this
///   card from your hand" is an activation-zone restriction; the ability
///   can only be activated while the card is in its controller's hand,
///   enforced by <c>canActivateCheck</c> testing
///   <c>Zone == ZoneType.Hand</c>.
/// - The exile is the activation cost (CR 601.2h / 118 — paid as part of
///   activating), performed inline by <c>additionalCostPayer</c>:
///   move the card from its controller's hand to its owner's exile zone.
///   This mirrors how Lotus Petal pays its sacrifice cost inline.
///
/// ## Deferred (v1 gaps)
/// - <b>Reusable ExileSelfFromHand cost primitive</b>: the exile is done
///   with a local closure (same posture as Lotus Petal's inline
///   sacrifice). A first-class cost object + JSON cost-def can land when
///   a second exile-from-hand-for-mana card (e.g. Elvish Spirit Guide,
///   Lotus Cobra-adjacent pitch effects) arrives and justifies the
///   shared abstraction.
/// </summary>
[CardName("Simian Spirit Guide")]
public static class SimianSpiritGuideFactory
{
    public const string CardName = "Simian Spirit Guide";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("simian-spirit-guide");

    /// <summary>
    /// Construct Simian Spirit Guide owned and controlled by
    /// <paramref name="owner"/>. Identity comes from the embedded JSON;
    /// the exile-from-hand mana ability is attached here (see class
    /// remarks for why it can't be JSON-driven).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var guide = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Exile this card from your hand: Add {R}.
        //
        // CR 605.1a — a mana ability (adds mana, no target, doesn't use
        // the stack). No-tap overload: the card lives in the hand, so
        // there is no permanent to tap. canActivateCheck gates the
        // activation to the controller's hand (CR 602.5 activation-zone
        // restriction). additionalCostPayer pays the exile cost inline
        // (CR 601.2h) — move hand -> owner's exile.
        // ----------------------------------------------------------------
        guide.AddAbility(new ManaAbility(
            source: guide,
            controller: owner,
            manaGenerated: ManaCost.Parse("R"),
            canActivateCheck: () => guide.Zone == ZoneType.Hand,
            additionalCostPayer: _ => ExileFromHand(guide),
            tapsAsCost: false));

        return guide;
    }

    /// <summary>
    /// Pay the "Exile this card from your hand" cost (CR 601.2h): the
    /// controller moves the card from their hand to its owner's exile
    /// zone. Idempotent — defensively no-ops if the card has already left
    /// the hand (shouldn't happen given the canActivateCheck gate).
    /// Mirrors <see cref="LotusPetalFactory"/>'s inline-cost closure.
    /// </summary>
    private static void ExileFromHand(Creature guide)
    {
        if (guide.Zone != ZoneType.Hand) return;

        var controller = guide.Controller;
        var owner = guide.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Hand.RemoveCard(guide);
        owner.Zones.Exile.AddCard(guide);
        guide.SetZone(ZoneType.Exile);
    }
}
