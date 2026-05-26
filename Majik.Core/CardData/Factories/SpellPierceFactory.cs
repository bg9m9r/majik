using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spell Pierce (Zendikar / various reprints, {U}).
///
/// Instant. Oracle text:
///   "Counter target noncreature spell unless its controller pays {2}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - <b>Counter target noncreature spell unless its controller pays {2}</b>
///   — mirrors the "unless pay" pattern from <see cref="ManaLeakFactory"/>
///   (N=3) and <see cref="DazeFactory"/> (N=1) with the added "noncreature"
///   filter from <see cref="NegateFactory"/>. At resolution:
///   1. CR 608.2b — if the target spell has become a creature spell (mode /
///      type swap), the effect does nothing.
///   2. CR 118.4 — if the target's controller has {2} available in their
///      mana pool, the engine auto-pays (v1 auto-pay posture — same queue
///      as Daze / Mana Leak / Cursecatcher) and the counter no-ops.
///   3. Otherwise the spell is countered via
///      <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves
///      to the graveyard (CR 701.5). Uncounterable spells survive (CR 701.5b).
///
/// Coverage note: the data-driven
/// <see cref="SpellTemplates.Templates.Counter.CounterUnlessPayTemplate"/>
/// already binds this oracle text to the shared
/// <see cref="SpellTemplates.Templates.Counter.CounterSpellFactory.CounterTargetSpellUnlessPay"/>
/// shape, so casting Spell Pierce off a real seed row resolves correctly via
/// the binder path. This named factory exists to surface the printed shape
/// to the <see cref="NamedCardFactory"/> dispatcher (used by bot / tests /
/// shape-only call sites) and to keep <c>IsImplemented</c> flipped at seed
/// export.
///
/// ## Deferred
/// - Real "do you want to pay {2}?" agent prompt — same queue as Daze /
///   Mana Leak / Stubborn Denial. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Spell Pierce")]
public static class SpellPierceFactory
{
    public const string CardName = "Spell Pierce";
    public const string PrintedManaCost = "{U}";

    /// <summary>The {N} the target's controller must pay to avoid the counter.</summary>
    public const int UnlessPayN = 2;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (counter target noncreature spell unless pay {2}) is built via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single
    /// 1..1 "target noncreature spell" request; on resolution checks the
    /// noncreature filter (CR 608.2b) and the unless-pay rider, countering
    /// only when the target's controller cannot / does not pay {2}.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests — the effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayN);

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
                    new Effect(
                        $"{CardName} — counter target noncreature spell unless its controller pays {{{UnlessPayN}}}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 608.2b — if the target has become a
                            // creature spell by resolution time, the effect
                            // does nothing for it (mirrors NegateFactory).
                            if (spell.Card.HasType(CardType.Creature)) return;

                            // CR 118.4 — if the target's controller has
                            // {2} in their mana pool, they auto-pay (v1
                            // auto-pay posture). Spell Pierce no-ops.
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // Otherwise: counter. CR 701.5 / CR 701.5b —
                            // uncounterable spells survive the attempt.
                            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
