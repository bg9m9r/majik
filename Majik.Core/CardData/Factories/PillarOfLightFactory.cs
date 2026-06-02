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
    ///
    /// Declarative conversion (the exile-verb slice): delegates to the shared
    /// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> with a single
    /// <see cref="ExileTargetEffectDef"/> over the <c>creature_toughness_ge_4</c>
    /// filter. That filter's predicate IS the CR 608.2b legality (battlefield
    /// creature + toughness &gt;= 4), re-checked at resolution by the verb —
    /// byte-equivalent to the former hand-rolled toughness gate on the same
    /// shared exile primitive.
    /// </summary>
    /// <param name="targetResolver">Retained for signature compatibility; the
    /// declarative spell path receives live targets from the cast flow's
    /// <see cref="ChosenSpellParams"/>, so the resolver is a no-op identity
    /// here (pass <c>o =&gt; o</c>).</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);
        return CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ExileTargetEffectDef { TargetFilter = "creature_toughness_ge_4" },
            });
    }
}
