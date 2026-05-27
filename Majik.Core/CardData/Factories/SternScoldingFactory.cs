using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stern Scolding ({U}).
///
/// Instant. Oracle text:
///   "Counter target creature spell with power 2 or less or toughness 2 or less."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - Resolve-time <see cref="SpellDefinition"/> declares one 1..1 "target
///   creature spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// - Creature gate: if the chosen target is NOT a creature spell at resolution
///   (<see cref="CardType.Creature"/>) the effect does nothing (CR 608.2b).
/// - P/T gate: if the creature's power &gt; 2 AND toughness &gt; 2 (i.e. neither
///   condition is met) the effect does nothing (CR 608.2b). Uses base
///   Power/Toughness via <see cref="Creature.Power"/> / <see cref="Creature.Toughness"/>.
/// </summary>
[CardName("Stern Scolding")]
public static class SternScoldingFactory
{
    public const string CardName = "Stern Scolding";
    public const string PrintedManaCost = "{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target creature spell with power or toughness 2 or less"
    /// SpellDefinition. CR 608.2b: if the chosen target is not a creature spell
    /// whose power ≤ 2 OR toughness ≤ 2 at resolution time, the effect does
    /// nothing (illegal target restriction — the spell remains on the stack).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature spell with power 2 or less or toughness 2 or less", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Stern Scolding — counter target small creature spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — only creature spells are legal targets.
                        if (!spell.Card.HasType(CardType.Creature)) return;

                        // P/T condition: the creature must have power ≤ 2 OR toughness ≤ 2.
                        if (spell.Card is not Creature creature) return;
                        if (creature.Power > 2 && creature.Toughness > 2) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
