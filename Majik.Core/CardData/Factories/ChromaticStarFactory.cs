using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chromatic Star (Lorwyn / Time Spiral Remastered,
/// {1}).
///
/// Artifact. Oracle text:
///   "{T}, Sacrifice Chromatic Star: Add one mana of any color.
///    When Chromatic Star is put into a graveyard from the battlefield,
///    draw a card."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{T}, Sacrifice Chromatic Star: Add one mana of any color</b> —
///   five <see cref="ManaAbility"/> instances (one per WUBRG), same shape
///   as <see cref="LotusPetalFactory"/> / <see cref="MoxOpalFactory"/>.
///   Each ability uses the (source, controller, manaGenerated,
///   canActivateCheck, additionalCostPayer) constructor:
///     - <c>canActivateCheck</c> = <c>!IsTapped AND Zone == Battlefield</c>
///       (gates the once-only activation).
///     - <c>additionalCostPayer</c> performs the sacrifice (CR 701.16)
///       inline — battlefield → owner's graveyard — same posture as Lotus
///       Petal's stub sacrifice. The bus-driven LTB trigger fires from
///       <see cref="ZoneManager"/>'s <see cref="CardMovedEvent"/>.
/// - <b>LTB draw trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> (CR 700.4 / 603.6 — Battlefield →
///   Graveyard self-move; <c>OnDies</c> is permanent-agnostic despite the
///   creature-flavoured name). <c>activeZones = {Battlefield, Graveyard}</c>
///   so the trigger still matches whether the engine evaluates the zone
///   gate just-before-the-move (source still on battlefield, CR 603.10c
///   last-known-information) or just-after (source already in graveyard).
///   Mirrors <see cref="WurmcoilEngineFactory"/>'s dies-trigger wiring.
///   Resolves to <see cref="Fx.DrawCards"/>(controller, 1) — controller
///   resolved at trigger creation time (no later control-change vector
///   for an already-sacrificed artifact, so the owner-controller pair is
///   stable).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: mirrors Lotus Petal / Mishra's
///   Bauble — the engine's generic <see cref="AdditionalCost"/> sacrifice
///   path is a no-op stub today, so the activation closure performs the
///   zone move directly. When the broader sacrifice-cost plumbing lands,
///   the inline move-to-graveyard can drop; the LTB trigger will still
///   fire via the centralised <see cref="CardMovedEvent"/> publication.
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color"
///   is bound as five separate <see cref="ManaAbility"/> instances — the
///   bot's source-picker selects the right colour at payment time. Same
///   posture as Lotus Petal / Mox Opal / Delighted Halfling / City of
///   Brass.
/// </summary>
[CardName("Chromatic Star")]
public static class ChromaticStarFactory
{
    public const string CardName = "Chromatic Star";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Chromatic Star with no live trigger-manager wiring. The
    /// LTB trigger is attached to the card's <see cref="Card.Abilities"/>
    /// collection so structural shape tests can observe it; for end-to-end
    /// firing pass a live <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Chromatic Star with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, the LTB draw trigger
    /// is registered so the bus surfaces it automatically (mirrors The
    /// One Ring / Aether Hub's two-arg pattern).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var star = new Artifact(CardName, PrintedManaCost);
        star.SetOwner(owner);
        star.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice Chromatic Star: Add one mana of any color.
        // Five ManaAbility instances (one per WUBRG) — same shape as Lotus
        // Petal / Mox Opal / Delighted Halfling. Each is gated on:
        //   (1) Chromatic Star is untapped, AND
        //   (2) Chromatic Star is still on the battlefield.
        // The additionalCostPayer performs the sacrifice (CR 701.16)
        // inline; the LTB trigger fires off the ZoneManager-published
        // CardMovedEvent.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            star.AddAbility(new ManaAbility(
                source: star,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !star.IsTapped
                                        && star.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => SacrificeSelf(star, owner)));
        }

        // ----------------------------------------------------------------
        // When Chromatic Star is put into a graveyard from the
        // battlefield, draw a card. CR 700.4 / 603.6 — battlefield →
        // graveyard self-move. Triggers.OnDies despite the name is shape-
        // generic over CardMovedEvent (FromZone=Battlefield → ToZone=
        // Graveyard for the source card). activeZones={Battlefield,
        // Graveyard} so the gate matches whether the engine evaluates
        // pre- or post-move (mirrors Wurmcoil Engine's dies-trigger).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Chromatic Star: draw a card on LTB battlefield->graveyard",
            () => Fx.DrawCards(owner, 1));

        var ltbTrigger = new TriggeredAbility(
            source: star,
            controller: owner,
            condition: Triggers.OnDies(star),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        star.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return star;
    }

    /// <summary>
    /// CR 701.16 — sacrifice: the controller moves Chromatic Star from
    /// the battlefield to its owner's graveyard. Idempotent — defensive
    /// against double-execution if a sibling colour-ability also tried to
    /// pay the cost in the same step (the canActivateCheck gate makes
    /// this unreachable in practice).
    /// </summary>
    private static void SacrificeSelf(Artifact star, Player owner)
    {
        if (star.Zone != ZoneType.Battlefield) return;

        var controller = star.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(star);
        owner.Zones.Graveyard.AddCard(star);
        star.SetZone(ZoneType.Graveyard);
    }
}
