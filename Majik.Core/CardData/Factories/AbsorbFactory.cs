using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Absorb (Apocalypse / Dominaria United, {W}{U}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell. You gain 3 life."
///
/// ## Declarative spell schema (composite counter — proof of the
/// <c>counter_target_spell</c> union verb)
/// <see cref="BuildDefinition"/> declares the printed text as a TWO-verb
/// composition handed to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>:
/// a <see cref="CounterTargetSpellEffectDef"/> (the new union counter verb, which
/// targets a spell on the stack and removes it via the shared
/// <see cref="Majik.Core.Primitives.Fx.Counter"/> primitive — CR 701.5) followed
/// by an untargeted <see cref="GainLifeSelfEffectDef"/> for the +3 life
/// (CR 119.3). Unlike the generic single-clause <c>CounterTargetSpellTemplate</c>
/// (which would bind the counter clause and silently drop the lifegain rider),
/// the composition resolves BOTH clauses in printed order (CR 608.2c).
///
/// CR 608.2b — a target that has left the stack at resolution fizzles the
/// counter; the lifegain still happens (the two clauses are independent, not a
/// linked "if you do"). CR 701.5b — an uncounterable target survives the
/// counter attempt.
/// </summary>
[CardName("Absorb")]
public static class AbsorbFactory
{
    public const string CardName = "Absorb";
    public const string PrintedManaCost = "{W}{U}{U}";

    /// <summary>Gain this much life when Absorb resolves (CR 119.3).</summary>
    public const int LifeGain = 3;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "counter target spell; you gain 3 life" SpellDefinition
    /// declaratively (the <c>counter_target_spell</c> verb + the
    /// <c>gain_life_self</c> rider).
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new CounterTargetSpellEffectDef(),
                new GainLifeSelfEffectDef { Amount = LifeGain },
            });
}
