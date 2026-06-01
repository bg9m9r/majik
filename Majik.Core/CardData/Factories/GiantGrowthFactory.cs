using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Giant Growth (Limited Edition Alpha, {G}).
///
/// Instant. Oracle text:
///   "Target creature gets +3/+3 until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {G}.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1..1 "target creature" <see cref="TargetRequest"/>. On resolve:
///   register a <see cref="PumpUntilEndOfTurnEffect"/>(+3, +3) on the
///   target creature's <see cref="Creature.ActiveEffects"/> (CR 514.2 —
///   "until end of turn"). CR 608.2b: if the target is no longer a
///   Creature on the battlefield at resolution, the effect no-ops.
///
/// Mirrors <see cref="MutagenicGrowthFactory"/> minus the Phyrexian
/// alt-cost / keyword rider (Giant Growth is the un-Phyrexian sibling
/// at +3/+3 for {G}).
/// </summary>
[CardName("Giant Growth")]
public static class GiantGrowthFactory
{
    public const string CardName = "Giant Growth";
    public const string PrintedManaCost = "{G}";

    /// <summary>Layer 7c +P/+T magnitude (CR 613.1g).</summary>
    public const int PumpAmount = 3;

    /// <summary>
    /// CardDef DSL — the full spell in one fluent declaration. "Target
    /// creature gets +3/+3 until end of turn." compiles to a 1..1 "target
    /// creature" <see cref="TargetRequest"/> + a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(+3,+3) resolve step via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/>.
    /// </summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .Resolve(c => c.PumpUntilEndOfTurn(PumpAmount, PumpAmount).To(TargetKind.Creature));

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets +3/+3 until end of turn"
    /// <see cref="SpellDefinition"/>. Delegates entirely to the fluent
    /// <c>.Resolve(...)</c> body via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/> — the ~30-line
    /// bespoke SpellDefinition collapses to one call.
    ///
    /// On resolve: the materializer validates the target is still a
    /// <see cref="Creature"/> on the Battlefield (CR 608.2b — illegal
    /// target → no-op), then registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(+3, +3) on its
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires in
    /// cleanup). When ActiveEffects is null (shape-only tests) the
    /// registration is a no-op — identical guards to the prior bespoke body.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinition(Define(), resolver: o => o);
}
