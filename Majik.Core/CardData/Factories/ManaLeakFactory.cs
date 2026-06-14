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
/// Named-card factory for Mana Leak (Stronghold / various reprints, {1}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell unless its controller pays {3}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue.
/// - <b>Counter target spell unless its controller pays {3}</b> — mirrors the
///   "unless pay" pattern from <see cref="DazeFactory"/>, routed through
///   <see cref="Majik.Core.Primitives.PayUnlessCounterRider"/>. At resolution
///   the target spell's CONTROLLER is asked (CR 118.4) whether to pay {3} to
///   keep their spell; on "yes" + affordable it is spent and the counter
///   no-ops, on "no" / can't afford the spell is countered (CR 701.5). The
///   default heuristic bot pays when able; remote / human agents get the real
///   prompt. The legacy synchronous (shape-only) path keeps the deterministic
///   "pay if able" posture.
/// </summary>
[CardName("Mana Leak")]
public static class ManaLeakFactory
{
    public const string CardName = "Mana Leak";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (counter unless pay {3}) is built via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a single
    /// spell; on resolution checks whether the target's controller can pay {3}
    /// — if so they pay it automatically and the spell resolves normally; if
    /// not, the spell is countered (CR 701.5) and its card goes to the
    /// graveyard (CR 608.2b — illegal-target check happens before this point).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    // CR 118.4 — ask the target spell's controller whether to
                    // pay {3} to keep it on the stack; counter on no / can't
                    // afford (CR 701.5). See PayUnlessCounterRider.
                    Majik.Core.Primitives.PayUnlessCounterRider.Build(
                        "Mana Leak — counter target spell unless its controller pays {3}",
                        stack,
                        () => targetResolver(raw) as ISpell,
                        unlessPayN: 3),
                };
            });
    }
}
