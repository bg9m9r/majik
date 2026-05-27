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
/// Named-card factory for Cancel ({1}{U}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell."
///
/// The vanilla hard counter — <see cref="NegateFactory"/> shape with no type
/// filter and no "unless pays" rider. On resolution the target spell is
/// countered via <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard
/// zone-move (CR 701.5).
/// </summary>
[CardName("Cancel")]
public static class CancelFactory
{
    public const string CardName = "Cancel";
    public const string PrintedManaCost = "{1}{U}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Any spell on the
    /// stack is a legal target (no type filter).
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
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Cancel — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
