using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Swords to Plowshares (Alpha {W}).
///
/// Instant. Oracle text:
///   "Exile target creature. Its controller gains life equal to its power."
///
/// ## Implemented (v1)
/// - Instant {W} (White) card shape with owner / controller wired.
/// - <b>Exile target creature + lifegain by power</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/> whose
///   effect exiles the chosen creature and gives that creature's controller
///   life equal to its power, sampled BEFORE the zone move (CR 112.7a — last
///   known information once it leaves the battlefield). Power is read via
///   <see cref="Creature.Power"/>, which routes through
///   <see cref="Majik.Core.Effects.ContinuousEffectsService.Compute(Permanent)"/>
///   when the creature has an <c>ActiveEffects</c> service attached (Tarmogoyf
///   CDA / anthems / pump all feed through). Negative power is floored to
///   zero per CR 119.3 (a player can't gain a negative amount of life).
///
/// ## Notes
/// - CR 608.2b — if the chosen target is not a creature on the battlefield at
///   resolution time (illegal target), the effect does nothing.
/// - Mirrors the Solitude ETB-exile pattern but ships on an instant spell with
///   a 1..1 target request (Solitude is "up to one").
/// </summary>
[CardName("Swords to Plowshares")]
public static class SwordsToPlowsharesFactory
{
    public const string CardName = "Swords to Plowshares";
    public const string Cost = "{W}";

    /// <summary>
    /// Construct Swords to Plowshares as an Instant card with owner /
    /// controller wired. The resolve SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up
    /// site (mirrors Spell Snare / Force of Will).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "exile target creature + controller gains life equal to its
    /// power" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// (e.g. a stack-object handle) to the live engine object. Pass
    /// <c>o =&gt; o</c> for tests that hand creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather all creatures on the
                    // battlefield. Removal intent in the bot's ranker pushes
                    // opponent's biggest threat to the top.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Swords to Plowshares — exile target creature; its controller gains life equal to its power",
                        () =>
                        {
                            if (resolved is not Creature target) return;
                            // CR 608.2b — illegal target at resolution → no-op.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 112.7a — sample power BEFORE the zone move,
                            // since lifegain references the creature as last
                            // seen on the battlefield. Floor negative power
                            // to zero (CR 119.3).
                            var snapshotPower = target.Power;
                            var lifeAmount = snapshotPower < 0 ? 0 : snapshotPower;
                            var targetController = target.Controller ?? target.Owner;

                            // Exile (CR 701.21).
                            var fromOwner = target.Owner;
                            if (fromOwner != null)
                            {
                                fromOwner.Zones.Battlefield.RemoveCard(target);
                                fromOwner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);

                            // Lifegain to the exiled creature's controller
                            // (CR 119.3).
                            if (targetController != null && lifeAmount > 0)
                            {
                                targetController.GainLife(lifeAmount);
                            }
                        }),
                };
            });
}
