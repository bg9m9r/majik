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
/// Named-card factory for Counterspell (Alpha / many reprints, {U}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell."
///
/// The canonical hard counter — {U}{U} for an unconditional counter of
/// any spell on the stack (CR 701.5). Mirrors <see cref="NegateFactory"/>
/// without the noncreature filter.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}{U}, blue.
/// - <b>Counter target spell</b> — at resolution, the target is removed
///   from the stack via <see cref="OracleSpellBinder.RemoveFromStack"/>
///   and its card moves to the graveyard (CR 701.5).
///
/// The <see cref="SpellTemplates.Templates.Counter"/> family of templates
/// already binds Counterspell's oracle text generically; this named factory
/// exists so the embedded-seed exporter flags the row
/// <c>IsImplemented = true</c> (the seed reflects over <c>[CardName]</c>
/// factories, not spell-template coverage).
/// </summary>
[CardName("Counterspell")]
public static class CounterspellFactory
{
    public const string CardName = "Counterspell";
    public const string PrintedManaCost = "{U}{U}";

    /// <summary>CardDef DSL — card shape only. The counter-target-spell
    /// SpellDefinition is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. At resolution the
    /// target spell is removed from the stack (CR 701.5) and its card moves
    /// to the graveyard. No type gate — any spell is a legal target.
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
