using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boomerang (Alpha, {U}{U}).
///
/// Instant. Oracle text:
///   "Return target permanent to its owner's hand."
///
/// ## Implemented (v1)
/// - Instant {U}{U}, owner/controller wired.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1. Reads target from ChosenSpellParams (1..1 "target permanent").
///   2. Validates the target is still a Card on the Battlefield at
///      resolution (CR 608.2b — illegal target → effect does nothing).
///   3. Routes the bounce through <see cref="ZoneService.MoveCard"/>
///      when a ZoneService is supplied, or falls back to raw zone
///      manipulation (shape tests / dispatcher path).
///
/// Boomerang's "target permanent" is intentionally broader than
/// <see cref="UnsummonFactory"/>: artifacts, enchantments, lands,
/// creatures, and planeswalkers are all legal. The CandidateGatherer
/// enumerates every battlefield card; the bot's Bounce ranker picks
/// the highest-CMC opponent permanent.
/// </summary>
[CardName("Boomerang")]
public static class BoomerangFactory
{
    public const string CardName = "Boomerang";
    public const string PrintedManaCost = "{U}{U}";

    /// <summary>
    /// Construct Boomerang as an Instant card with owner/controller wired.
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
    /// Build the "return target permanent to its owner's hand"
    /// SpellDefinition.
    ///
    /// CR 608.2b: if the chosen target is no longer a permanent on the
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
                    "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Live gather all permanents (any type) across all
                    // players' battlefields. CR 110.1 — a permanent is any
                    // card on the battlefield.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Card>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Boomerang — return target permanent to its owner's hand",
                        () => Resolve(raw, zoneService)),
                };
            });

    private static void Resolve(object raw, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be a permanent (Card on Battlefield).
        if (raw is not Card target) return;
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
        // CR 111.7 / SBA 704.5d — if the target was a token, it briefly
        // exists in its owner's Hand and is then removed from that zone
        // by TokensCeaseToExistCheck on the next SBA pass.
    }
}
