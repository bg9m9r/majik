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
/// Named-card factory for Dispel ({U}).
///
/// Instant. Oracle text:
///   "Counter target instant spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - Resolve-time <see cref="SpellDefinition"/> declares one 1..1 "target
///   instant spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// - Instant gate: same posture as <see cref="NegateFactory"/> but restricted
///   to instants — if the chosen target is NOT an instant spell at resolution
///   (<see cref="CardType.Instant"/>) the effect does nothing (CR 608.2b),
///   applied defensively at resolve time.
/// </summary>
[CardName("Dispel")]
public static class DispelFactory
{
    public const string CardName = "Dispel";
    public const string PrintedManaCost = "{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target instant spell" SpellDefinition. CR 608.2b:
    /// if the chosen target is not an instant spell at resolution time, the
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
                new TargetRequest("target instant spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Dispel — counter target instant spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — only instant spells are legal targets.
                        if (!spell.Card.HasType(CardType.Instant)) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
