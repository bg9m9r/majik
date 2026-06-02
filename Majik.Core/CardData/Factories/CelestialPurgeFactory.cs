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
    ///
    /// Declarative conversion (the exile-verb slice): delegates to the shared
    /// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> with a single
    /// <see cref="ExileTargetEffectDef"/> over the <c>black_or_red_permanent</c>
    /// filter. That filter's predicate IS the CR 608.2b legality (battlefield +
    /// black-or-red), re-checked at resolution by the verb — byte-equivalent to
    /// the former hand-rolled colour gate, on the same shared exile primitive.
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
                new ExileTargetEffectDef { TargetFilter = "black_or_red_permanent" },
            });
    }
}
