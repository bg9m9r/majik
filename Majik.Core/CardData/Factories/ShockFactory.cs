using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shock (Mirage / many reprints, {R}).
///
/// Instant. Oracle text:
///   "Shock deals 2 damage to any target."
///
/// Vanilla "any target" burn — the simplest Bolt-shaped spell at
/// {R} → 2 damage (CR 115.3 — "any target" = creature, player,
/// planeswalker, or battle). Routed through
/// <see cref="Fx.DealDamageAny"/> so all three legal target classes
/// resolve correctly (CR 306.7 — damage to a planeswalker becomes
/// loyalty removal).
///
/// Mirrors the resolve shape used by <see cref="LavaDartFactory"/> and
/// <see cref="BurstLightningFactory"/>'s base branch.
/// </summary>
[CardName("Shock")]
public static class ShockFactory
{
    public const string CardName = "Shock";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    /// <summary>
    /// CardDef DSL — the entire spell, shape <em>and</em> resolve body, in one
    /// fluent declaration. "Shock deals 2 damage to any target." compiles to a
    /// single 1..1 "any target" <see cref="TargetRequest"/> + a
    /// <see cref="Fx.DealDamageAny"/> resolve step via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/> — no bespoke
    /// <see cref="SpellDefinition"/> / EffectFactory needed.
    /// </summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .Resolve(c => c.DealDamage(Damage).To(TargetKind.AnyTarget));

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Shock is cast.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (2) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>. Delegates entirely to the fluent
    /// <c>.Resolve(...)</c> body via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/> — the ~20-line
    /// bespoke SpellDefinition collapses to one call.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver) =>
        CardDefRuntime.BuildSpellDefinition(Define(), resolver);
}
