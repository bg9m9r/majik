using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disdainful Stroke (Khans of Tarkir and reprints, {1}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell with mana value 4 or greater."
///
/// ## Implemented (v1)
/// - Instant {1}{U} (Blue) card shape with owner / controller wired.
/// - <b>Counter target spell with mv 4+</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> whose effect samples the target
///   spell's mana value at resolution time and counters it iff that value is
///   4 or greater (CR 202.3, CR 701.5, CR 608.2b). A target whose mana value
///   is less than 4 at resolution time is treated as an illegal target — the
///   effect does nothing for it (CR 608.2b) and the spell stays on the stack.
///
/// ## Notes on mana-value comparison (CR 202.3)
/// Mana value is read from the target card's printed
/// <see cref="Card.ManaCostValue"/> (<see cref="Majik.Core.ValueObjects.ManaCost.TotalValue"/>)
/// plus any chosen X (<see cref="Card.PendingCastX"/>). This mirrors
/// <see cref="SpellSnareFactory"/> exactly — both want printed mv + chosen X
/// sampled at resolution. The only difference is the comparison: Spell Snare
/// requires mv == 2, Disdainful Stroke requires mv &gt;= 4.
/// </summary>
[CardName("Disdainful Stroke")]
public static class DisdainfulStrokeFactory
{
    public const string CardName = "Disdainful Stroke";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>CardDef DSL — card shape only. Counter-with-mv-4+ body is
    /// built via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell with mana value 4 or greater"
    /// SpellDefinition. CR 608.2b: if the chosen target's mana value is less
    /// than 4 at resolution time (illegal target), the effect does nothing for
    /// that target — the target spell remains on the stack.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell with mana value 4 or greater", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Disdainful Stroke — counter target spell with mana value 4 or greater", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 202.3 — sample mana value at resolution time
                        // (printed mv + chosen X). Mirrors Spell Snare.
                        var castCard = spell.Card;
                        var printed = castCard is Card concrete
                            ? concrete.ManaCostValue.TotalValue
                            : Majik.Core.ValueObjects.ManaCost.Parse(castCard.ManaCost).TotalValue;
                        var x = (castCard as Card)?.PendingCastX ?? 0;
                        var manaValue = printed + x;

                        // CR 608.2b — illegal target at resolution (mv < 4) →
                        // effect does nothing for that target.
                        if (manaValue < 4) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
