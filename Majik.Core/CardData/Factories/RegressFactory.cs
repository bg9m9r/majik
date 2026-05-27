using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Regress (Mirrodin, {2}{U}).
///
/// Instant. Oracle text:
///   "Return target permanent to its owner's hand."
///
/// ## Implemented (v1)
/// - Instant {2}{U} card shape, owner / controller wired.
/// - <see cref="BuildDefinition"/> exposes a single 1..1
///   "target permanent" <see cref="TargetRequest"/> with
///   <see cref="BotIntent.Bounce"/> intent. The candidate gatherer
///   enumerates every battlefield permanent across all players
///   (printed text has no controller filter — any permanent type,
///   any controller, is a legal target).
/// - Resolve body: CR 608.2b illegal-target re-check at resolution
///   (target must still be on the battlefield). CR 701.20 — return
///   the permanent to its owner's hand.
/// - Effect is identical to Boomerang ({U}{U}) at a different mana cost.
/// </summary>
[CardName("Regress")]
public static class RegressFactory
{
    public const string CardName = "Regress";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Construct Regress as an Instant card with owner / controller wired.
    /// The resolve SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver
    /// wire-up site (mirrors Vapor Snag / VaporSnagFactory).
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
    /// CR 608.2b: if the chosen target is no longer on the battlefield
    /// at resolution, the effect does nothing.
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
                    // Any battlefield permanent across all players.
                    // Bounce intent in the bot's ranker prefers
                    // tempo-loss against opponents (CMC-as-spend proxy).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — return target permanent to its owner's hand",
                        () => Resolve(raw, zoneService)),
                };
            });

    private static void Resolve(object raw, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be on the battlefield.
        if (raw is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        // CR 701.20 — return to owner's hand.
        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            var controller = target.Controller ?? targetOwner;
            controller.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }
    }
}
