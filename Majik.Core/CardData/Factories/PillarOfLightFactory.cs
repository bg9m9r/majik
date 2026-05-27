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
/// Named-card factory for Pillar of Light (Magic 2015, {2}{W}).
///
/// Instant. Oracle text:
///   "Exile target creature with toughness 4 or greater."
///
/// ## Implemented (v1)
/// - Instant {2}{W} (White) card shape with owner / controller wired.
/// - <b>Exile target creature with toughness 4 or greater</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/> whose
///   effect exiles the chosen creature if it is on the battlefield and its
///   <see cref="Creature.Toughness"/> is ≥ 4 at resolution time.
///
/// ## Notes
/// - CR 608.2b — if the chosen target is not on the battlefield at
///   resolution time (illegal target), the effect does nothing.
/// - Toughness is checked at resolution time (not at targeting time). If the
///   creature's toughness has dropped below 4 by then, the effect is a no-op
///   (CR 608.2b pattern mirrors Goblin Cratermaker / Celestial Purge).
/// - Only creatures are legal targets; the candidate gatherer filters by
///   <see cref="Creature"/> type and toughness ≥ 4 at targeting time.
/// </summary>
[CardName("Pillar of Light")]
public static class PillarOfLightFactory
{
    public const string CardName = "Pillar of Light";
    public const string Cost = "{2}{W}";

    /// <summary>CardDef DSL — card shape only. Exile body lives in
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, Cost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "exile target creature with toughness 4 or greater" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that
    /// hand creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with toughness 4 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Gather battlefield creatures with toughness ≥ 4 at targeting time.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Toughness >= 4)
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
                        "Pillar of Light — exile target creature with toughness 4 or greater",
                        () =>
                        {
                            if (resolved is not Creature target) return;
                            // CR 608.2b — illegal target at resolution → no-op.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Toughness check at resolution time (CR 608.2b).
                            if (target.Toughness < 4) return;

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
