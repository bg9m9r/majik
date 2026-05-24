using Majik.Core.Abilities;
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
///   "unless pay" pattern from <see cref="DazeFactory"/>. At resolution the
///   engine checks whether the target spell's controller has {3} available;
///   if yes, it is spent automatically and the counter no-ops (v1 auto-pay
///   posture — same as Daze / Mana Tithe). If no, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card goes to the
///   graveyard (CR 701.5).
///
/// ## Deferred
/// - Real "do you want to pay {3}?" agent prompt — same queue as Daze /
///   Stubborn Denial / Spell Pierce. v1 is deterministic: "pay if able."
/// </summary>
public static class ManaLeakFactory
{
    public const string CardName = "Mana Leak";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Construct the Mana Leak card shape (Instant, {1}{U}).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

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

        var unlessCost = ManaCost.Zero.AddGenericCost(3);

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
                    new Effect("Mana Leak — counter target spell unless its controller pays {3}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 118.4 — if the target's controller has {3} in their
                        // mana pool, they may pay; v1 auto-pays when able.
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            // Controller paid {3} — spell is NOT countered.
                            return;
                        }

                        // Controller couldn't pay — counter the spell.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
