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
/// Named-card factory for Logic Knot (Future Sight, {X}{U}{U}).
///
/// Instant. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Counter target spell unless its controller pays {X}."
///
/// ## Implemented (v1)
/// - Instant shape, printed mana cost {X}{U}{U}, blue.
/// - <b>Delve marker</b> (CR 702.66) via <see cref="KeywordAbility"/>;
///   payment substitution is owned by
///   <see cref="Majik.Core.Costs.DelveCost"/> +
///   <see cref="SpellCastFlow"/>. Bot discovery via
///   <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/>.
/// - <b>Variable X</b>: <see cref="SpellDefinition.HasVariableX"/> is true
///   so <see cref="SpellCastFlow"/> prompts for an X choice and stamps it
///   onto the card via <see cref="Card.SetPendingCastX"/>. The chosen X
///   also flows into <see cref="ChosenSpellParams.X"/>.
/// - <b>Counter unless pay {X}</b>: mirrors the Mana Leak / Mystical Dispute
///   shape, with the pay-cost generic side equal to the chosen X. At
///   resolution the target spell's controller is auto-prompted for {X};
///   if they can pay, the counter no-ops (CR 118.4). Otherwise the spell
///   is countered via <see cref="OracleSpellBinder.RemoveFromStack"/> and
///   moved to graveyard (CR 701.5).
///
/// ## Deferred (v1 gaps)
/// - Agent prompt for "pay {X}?" — v1 auto-pays when able, same posture
///   as Mana Leak / Mystical Dispute / Cursecatcher / Daze.
/// - Bot affordability sees worst-case (no Delve substitution applied
///   pre-target). Same limitation as the rest of the delve cycle; the
///   real cast path applies the reduction after targets are stamped.
/// </summary>
[CardName("Logic Knot")]
public static class LogicKnotFactory
{
    public const string CardName = "Logic Knot";

    /// <summary>
    /// Printed mana cost. The {X} is collected at cast time and contributes
    /// to MV via <see cref="Card.PendingCastX"/> / <see cref="ChosenSpellParams.X"/>.
    /// </summary>
    public const string PrintedManaCost = "{X}{U}{U}";

    /// <summary>CardDef DSL — Instant shape + Delve marker (CR 702.66).
    /// Resolve behaviour (counter unless pay {X}) is built via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Delve");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell unless its controller pays {X}"
    /// SpellDefinition. X is read from <see cref="ChosenSpellParams.X"/>
    /// (preferred) and falls back to the card's
    /// <see cref="Card.PendingCastX"/> for paths that hand-build the
    /// params without the X field set.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>(), BotIntent.Counter),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                // CR 107.3 — X in costs equals the value the caster
                // chose. Read from ChosenSpellParams.X; fall back to
                // the card's PendingCastX stamp for hand-built params.
                var xChoice = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect("Logic Knot — counter target spell unless its controller pays {X}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        var x = xChoice;
                        if (x == 0 && spell.Card is Card concrete && concrete.PendingCastX.HasValue)
                        {
                            x = concrete.PendingCastX.Value;
                        }

                        var unlessCost = ManaCost.Zero.AddGenericCost(x);

                        // CR 118.4 — target's controller may pay {X};
                        // v1 auto-pays when able (parallels Mana Leak).
                        // X == 0 means the rider is "pay {0}" which any
                        // controller can satisfy trivially — PayMana
                        // succeeds with an empty cost, so the spell is
                        // not countered. That matches the printed text.
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // CR 701.5 — counter the spell: remove from
                        // stack, move card to graveyard.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
