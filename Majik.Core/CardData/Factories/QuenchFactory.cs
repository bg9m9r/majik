using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quench (Ravnica Allegiance, {1}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell unless its controller pays {1}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue.
/// - <b>Counter target spell unless its controller pays {1}</b> — same
///   "auto-pay-if-able" posture as <see cref="ManaLeakFactory"/> /
///   <see cref="MysticalDisputeFactory"/> / <see cref="DazeFactory"/>: at
///   resolution the engine checks whether the target spell's controller
///   has {1} available in their mana pool; if yes, it is spent
///   automatically and the counter no-ops (CR 118.4 — "unless" cost). If
///   no, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card goes to
///   the graveyard (CR 701.5).
///
/// ## Deferred
/// - Real "do you want to pay {1}?" agent prompt — same queue as Daze /
///   Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Quench")]
public static class QuenchFactory
{
    public const string CardName = "Quench";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {1}").</summary>
    public const int UnlessPayGeneric = 1;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (counter unless pay {1}) is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a single
    /// spell; on resolution checks whether the target's controller can pay {1}
    /// — if so they pay it automatically and the spell resolves normally; if
    /// not, the spell is countered (CR 701.5) and its card goes to the
    /// graveyard. CR 608.2b — illegal target at resolution is handled by the
    /// pre-resolve target-legality check; this body assumes the resolved
    /// target is still a live <see cref="ISpell"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Quench — counter target spell unless its controller pays {1}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 118.4 — target's controller may pay {1} to prevent
                        // the counter. v1 auto-pays when able (same posture as
                        // Mana Leak / Daze / Mystical Dispute).
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // Controller couldn't pay — counter the spell (CR 701.5).
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
