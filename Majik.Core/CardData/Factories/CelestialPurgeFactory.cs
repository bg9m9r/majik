using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Celestial Purge (Magic 2011, {1}{W}).
///
/// Instant. Oracle text:
///   "Exile target black or red permanent."
///
/// ## Implemented (v1)
/// - Instant {1}{W} (White) card shape with owner / controller wired.
/// - <b>Exile target black or red permanent</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/> whose
///   effect exiles the chosen permanent if it is on the battlefield and its
///   colour set (per <see cref="CardColors.GetColors"/>) contains
///   <see cref="ManaColor.Black"/> or <see cref="ManaColor.Red"/>.
///
/// ## Notes
/// - CR 608.2b — if the chosen target is not a permanent on the battlefield at
///   resolution time (illegal target), the effect does nothing.
/// - Colour is checked at resolution time (not at targeting time). If the
///   permanent has lost its black or red colour by then, the effect is a no-op.
/// - "Permanent" is broader than "creature" — lands, artifacts, enchantments,
///   planeswalkers and battles that are black or red are all legal targets.
///   The candidate gatherer collects every card on the battlefield; the
///   resolution guard enforces the colour requirement (CR 608.2b pattern
///   mirrors Goblin Cratermaker Mode B).
/// </summary>
[CardName("Celestial Purge")]
public static class CelestialPurgeFactory
{
    public const string CardName = "Celestial Purge";
    public const string Cost = "{1}{W}";

    /// <summary>CardDef DSL — card shape only. Exile body lives in
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, Cost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "exile target black or red permanent" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that
    /// hand permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target black or red permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Gather all battlefield permanents; colour enforcement
                    // at resolution time mirrors the CR 608.2b pattern.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c =>
                        {
                            var colours = CardColors.GetColors(c);
                            return colours.Contains(ManaColor.Black)
                                || colours.Contains(ManaColor.Red);
                        })
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
                        "Celestial Purge — exile target black or red permanent",
                        () =>
                        {
                            if (resolved is not ICard target) return;
                            // CR 608.2b — illegal target at resolution → no-op.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Colour check at resolution time (CR 608.2b).
                            var colours = CardColors.GetColors(target);
                            var isBlackOrRed = colours.Contains(ManaColor.Black)
                                           || colours.Contains(ManaColor.Red);
                            if (!isBlackOrRed) return;

                            // Exile (CR 701.21).
                            var owner = target.Owner;
                            if (owner != null)
                            {
                                owner.Zones.Battlefield.RemoveCard(target);
                                owner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);
                        }),
                };
            });
}
