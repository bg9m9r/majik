using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Counterspell (Alpha / many reprints, {U}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell."
///
/// The archetypal hard counter — no filters, no riders, no escape clause.
/// Mirrors <see cref="NegateFactory"/>'s shape with the noncreature
/// restriction removed (any spell is a legal target). At resolution the
/// target spell is removed from the stack via
/// <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to
/// its owner's graveyard (CR 701.5, CR 608.2b).
/// </summary>
[CardName("Counterspell")]
public static class CounterspellFactory
{
    public const string CardName = "Counterspell";
    public const string PrintedManaCost = "{U}{U}";

    /// <summary>CardDef DSL — card shape only. The counter SpellDefinition
    /// is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Declares a single
    /// 1..1 "target spell" request; on resolution removes the target from
    /// the stack and sends its card to the graveyard (CR 701.5).
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
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
                    new Effect("Counterspell — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
