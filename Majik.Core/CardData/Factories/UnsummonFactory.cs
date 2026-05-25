using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unsummon (Alpha, {U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand."
///
/// ## Implemented (v1)
/// - Instant {U}, owner/controller wired.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1. Reads target from ChosenSpellParams (1..1 "target creature").
///   2. Validates the target is still a Creature on the Battlefield at
///      resolution (CR 608.2b — illegal target → effect does nothing).
///   3. Routes the bounce through <see cref="ZoneService.MoveCard"/>
///      when a ZoneService is supplied, or falls back to raw zone
///      manipulation (shape tests / dispatcher path).
///
/// Mirrors <see cref="VaporSnagFactory"/> without the life-loss rider.
/// </summary>
[CardName("Unsummon")]
public static class UnsummonFactory
{
    public const string CardName = "Unsummon";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Construct Unsummon as an Instant card with owner/controller wired.
    /// The resolve SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up
    /// site (mirrors Vapor Snag).
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
    /// Build the "return target creature to its owner's hand"
    /// SpellDefinition.
    ///
    /// CR 608.2b: if the chosen target is no longer a creature on the
    /// battlefield at resolution, the effect does nothing.
    /// </summary>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-
    /// aware zone moves. When null, raw zone manipulation is used (shape
    /// tests / dispatcher path).</param>
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
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Agent-prompt MVP: live gather all creatures; Bounce
                    // intent in the bot's ranker picks opponent's most-
                    // expensive creature (CMC-as-spend proxy).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Unsummon — return target creature to its owner's hand",
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

        var controller = target.Controller ?? targetOwner;

        // CR 701.20 — return to owner's hand.
        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }
    }
}
