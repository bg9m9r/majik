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
/// Named-card factory for Dovin's Veto (War of the Spark, {W}{U}).
///
/// Instant. Oracle text:
///   "This spell can't be countered. Counter target noncreature spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{U} (white + blue).
/// - <b>Can't be countered</b> — a <see cref="KeywordAbility"/>
///   ("Uncounterable") marker is attached to the card shape. At cast time
///   <see cref="Majik.Core.Game.SpellCastFlow"/> reads this marker and
///   stamps <see cref="ISpell.CannotBeCountered"/> on the resulting
///   <see cref="Majik.Core.Spells.Spell"/>. Counter effects (Negate,
///   Force of Negation, …) that call
///   <see cref="OracleSpellBinder.RemoveFromStack"/> will find the flag
///   and veto the counter-attempt, leaving Dovin's Veto on the stack to
///   resolve normally (CR 701.5b).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   noncreature spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// - Noncreature gate: at resolution, if the target spell has type Creature
///   (<see cref="CardType.Creature"/>) the effect does nothing (CR 608.2b).
///   Same posture as <see cref="NegateFactory"/> — the filter is applied
///   defensively at resolve time rather than at choose-time.
/// </summary>
[CardName("Dovin's Veto")]
public static class DovinsVetoFactory
{
    public const string CardName = "Dovin's Veto";
    public const string PrintedManaCost = "{W}{U}";

    /// <summary>CardDef DSL — card shape + "Uncounterable" marker.
    /// Resolve behaviour is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Uncounterable");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target noncreature spell" SpellDefinition.
    /// CR 608.2b: if the chosen target is a creature spell at resolution
    /// time, the effect does nothing (illegal target — the spell remains on
    /// the stack).
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
                new TargetRequest("target noncreature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Dovin's Veto — counter target noncreature spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target has become a creature spell
                        // by resolution time, the effect does nothing for it.
                        if (spell.Card.HasType(CardType.Creature)) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
