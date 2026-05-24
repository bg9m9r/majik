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
/// Named-card factory for Spell Snare (Coldsnap, {U}).
///
/// Instant. Oracle text:
///   "Counter target spell with mana value 2."
///
/// ## Implemented (v1)
/// - Instant {U} (Blue) card shape with owner / controller wired.
/// - <b>Counter target spell with mv-2</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> whose effect samples the target
///   spell's mana value at resolution time and counters it iff that value
///   equals 2 (CR 202.3, CR 701.5, CR 608.2b). A target whose mana value is
///   not 2 at resolution time is treated as an illegal target — the effect
///   does nothing for it (CR 608.2b).
///
/// ## Notes on mana-value comparison (CR 202.3)
/// Mana value is read from the target card's printed
/// <see cref="Card.ManaCostValue"/> (<see cref="Majik.Core.ValueObjects.ManaCost.TotalValue"/>).
/// For spells cast with {X} this includes the chosen X via the engine's
/// existing X-collapse (see Chalice of the Void) — Spell Snare reuses the
/// same shape since both want printed mv + chosen X.
/// </summary>
[CardName("Spell Snare")]
public static class SpellSnareFactory
{
    public const string CardName = "Spell Snare";

    /// <summary>CardDef DSL — card shape only. Counter-with-mv-2 body is
    /// built via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, "{U}");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell with mana value 2" SpellDefinition.
    /// CR 608.2b: if the chosen target's mana value is not 2 at resolution
    /// time (illegal target), the effect does nothing for that target —
    /// the target spell remains on the stack.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell with mana value 2", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Spell Snare — counter target spell with mana value 2", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;
                        // CR 202.3 — sample mana value at resolution time.
                        // Mirrors Chalice of the Void's MV read (printed +
                        // PendingCastX where applicable).
                        var castCard = spell.Card;
                        var printed = castCard is Card concrete
                            ? concrete.ManaCostValue.TotalValue
                            : Majik.Core.ValueObjects.ManaCost.Parse(castCard.ManaCost).TotalValue;
                        var x = (castCard as Card)?.PendingCastX ?? 0;
                        var manaValue = printed + x;

                        // CR 608.2b — illegal target at resolution → effect
                        // does nothing for that target.
                        if (manaValue != 2) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
}
