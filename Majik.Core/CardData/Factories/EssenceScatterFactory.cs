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
/// Named-card factory for Essence Scatter ({1}{U}).
///
/// Instant. Oracle text:
///   "Counter target creature spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue.
/// - Resolve-time <see cref="SpellDefinition"/> declares one 1..1 "target
///   creature spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// - Creature gate: this is the inverse of <see cref="NegateFactory"/> — if
///   the chosen target is NOT a creature spell at resolution
///   (<see cref="CardType.Creature"/>) the effect does nothing (CR 608.2b),
///   applied defensively at resolve time.
/// </summary>
[CardName("Essence Scatter")]
public static class EssenceScatterFactory
{
    public const string CardName = "Essence Scatter";
    public const string PrintedManaCost = "{1}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target creature spell" SpellDefinition. CR 608.2b:
    /// if the chosen target is not a creature spell at resolution time, the
    /// effect does nothing (illegal target — the spell remains on the stack).
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
                new TargetRequest("target creature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Essence Scatter — counter target creature spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — only creature spells are legal targets.
                        if (!spell.Card.HasType(CardType.Creature)) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
