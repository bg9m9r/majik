using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vapor Snag (New Phyrexia, {U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand.
///    Its controller loses 1 life."
///
/// ## Implemented (v1)
/// - Instant {U}, owner/controller wired.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1. Reads target from ChosenSpellParams (1..1 "target creature").
///   2. Validates the target is still a Creature on the Battlefield at
///      resolution (CR 608.2b — illegal target → both effects do nothing;
///      the life-loss rider keys off "its controller", which is only
///      defined when the target is still on the battlefield).
///   3. Captures the controller before moving the card, then routes the
///      bounce through <see cref="ZoneService.MoveCard"/> when a ZoneService
///      is supplied, or falls back to raw zone manipulation.
///   4. Charges the captured controller 1 life via
///      <see cref="Player.LoseLife"/>.
///
/// CR 608.2b applies to both clauses: if the target has left the battlefield
/// by resolution, neither the bounce NOR the life loss happens (the oracle
/// text's "its controller" refers to the creature's controller at resolution,
/// which is indeterminate if it is no longer on the battlefield).
/// </summary>
[CardName("Vapor Snag")]
public static class VaporSnagFactory
{
    public const string CardName = "Vapor Snag";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Construct Vapor Snag as an Instant card with owner/controller wired.
    /// The resolve SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up
    /// site (mirrors Force of Negation / Spell Snare).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target creature to its owner's hand; its controller
    /// loses 1 life" SpellDefinition.
    ///
    /// CR 608.2b: if the chosen target is no longer a creature on the
    /// battlefield at resolution, both effects do nothing.
    /// </summary>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (shape tests /
    /// dispatcher path).</param>
    public static SpellDefinition BuildDefinition(
        ZoneService? zoneService = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Vapor Snag — return target creature to its owner's hand; its controller loses 1 life",
                        () => Resolve(raw, zoneService)),
                };
            });

    private static void Resolve(object raw, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        // Capture controller before the zone move, since SetController may
        // update after returning to hand.
        var controller = target.Controller ?? targetOwner;

        // CR 701.10 — return to owner's hand.
        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            var fromController = controller;
            fromController.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }

        // CR 119.3 — "Its controller loses 1 life." The controller is the
        // player who controlled the creature immediately before it left the
        // battlefield (captured above, before the zone move).
        controller.LoseLife(1);
    }
}
