using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unsummon ({U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand."
///
/// The plain bounce — <see cref="VaporSnagFactory"/> without the "its
/// controller loses 1 life" rider. CR 608.2b: if the chosen target is no
/// longer a creature on the battlefield at resolution, the effect does
/// nothing.
/// </summary>
[CardName("Unsummon")]
public static class UnsummonFactory
{
    public const string CardName = "Unsummon";
    public const string PrintedManaCost = "{U}";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target creature to its owner's hand" SpellDefinition.
    /// </summary>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used.</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
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

        // CR 701.10 — return to owner's hand.
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
