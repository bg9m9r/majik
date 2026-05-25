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
/// Named-card factory for Spell Pierce (Zendikar / many reprints, {U}).
///
/// Instant. Oracle text:
///   "Counter target noncreature spell unless its controller pays {2}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - <b>Counter target noncreature spell unless its controller pays {2}</b>
///   — mirrors the "unless pay" pattern from <see cref="ManaLeakFactory"/>
///   / <see cref="MysticalDisputeFactory"/>. At resolution the engine:
///   1. Verifies the resolved target is still an <see cref="ISpell"/>
///      whose card is NOT a creature. A creature-typed spell at
///      resolution-time → CR 608.2b — illegal target, full fizzle (the
///      sole-target rule). Spells that became creature-typed via copies
///      / type-changing effects between cast and resolution lose
///      legality.
///   2. Asks the target's controller to pay {2}. v1 auto-pays when able
///      (matches the Mana Leak / Daze / Mystical Dispute posture).
///   3. On non-payment, counters the spell via
///      <see cref="OracleSpellBinder.RemoveFromStack"/> and moves it to
///      its owner's graveyard (CR 701.5).
///
/// ## Deferred (v1 gaps)
/// - <b>"Would you like to pay {2}?" agent prompt</b> — same queue as
///   Daze / Mana Leak / Mystical Dispute / Stubborn Denial. v1 is
///   deterministic: pay if able.
/// - <b>Noncreature gate at target-selection</b> — the
///   <see cref="TargetRequest"/> description carries the "noncreature"
///   constraint as free-text; the engine's target-validity pass uses
///   description-keyed filters only loosely (same posture as Negate /
///   Stubborn Denial). The resolution-time guard above catches the
///   common races (creature-typed at resolve). A future revision can
///   wire the noncreature filter into <c>LegalCandidates</c> directly.
/// </summary>
[CardName("Spell Pierce")]
public static class SpellPierceFactory
{
    public const string CardName = "Spell Pierce";
    public const string PrintedManaCost = "{U}";

    /// <summary>CR 118.4 — pay-or-counter rider.</summary>
    public const int UnlessPayGeneric = 2;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (counter unless pay {2}) is built via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a
    /// single noncreature spell; on resolution checks whether the
    /// target's controller can pay {2} — if so they pay it
    /// automatically and the spell resolves normally; if not, the
    /// spell is countered (CR 701.5) and its card goes to the
    /// graveyard.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object.</param>
    /// <param name="stack">Active stack; required to remove the
    /// countered spell. Null in pure-shape tests; the effect becomes a
    /// no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

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
                        "Spell Pierce — counter target noncreature spell unless its controller pays {2}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 608.2b — resolution-time legality check.
                            // The spell must still be a noncreature spell
                            // (printed types or any type-changing effects
                            // currently in play). If the target became a
                            // creature between cast and resolution (rare
                            // — Trickery-style copies into a token
                            // creature; copies of cast spells stay
                            // typed-as-card), it's illegal at resolve
                            // and Spell Pierce does nothing (sole-target
                            // fizzle).
                            if (spell.Card.HasType(CardType.Creature)) return;

                            // CR 118.4 — "unless its controller pays
                            // {2}". v1 auto-pays when able (same posture
                            // as Mana Leak / Daze / Mystical Dispute).
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // CR 701.5 — counter the spell: remove from
                            // stack, move card to its owner's graveyard.
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
