using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystical Dispute (Throne of Eldraine, {2}{U}).
///
/// Instant. Oracle text:
///   "This spell costs {2} less to cast if it targets a blue spell.
///    Counter target spell unless its controller pays {3}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{U}, blue.
/// - <b>Cost reduction (CR 117.7)</b>: a printed
///   <see cref="CostReductionAbility"/> using the whole-reducer shape. At
///   cost-calc time <see cref="CostReduction.GetEffectiveCost"/> consults
///   the reducer; the closure reads
///   <see cref="Card.PendingCastTargets"/> (stamped by
///   <see cref="SpellCastFlow"/> immediately after target collection — see
///   that file for the Pending* idiom shared with Delve count / cast-X).
///   If any picked target is an <see cref="ISpell"/> whose card is blue
///   (<see cref="CardColors.GetColors(ICard)"/>), the reducer returns 2.
///   Otherwise zero. Floor-at-zero is enforced by <see cref="CostReduction"/>.
/// - <b>Counter unless pay {3}</b>: <see cref="BuildSpellDefinition"/> declares
///   one 1..1 "target spell" <see cref="TargetRequest"/>. At resolution the
///   target's controller is auto-prompted via <see cref="Player.PayMana"/>
///   for {3}; if they can pay, the counter no-ops (CR 118.4). Otherwise the
///   spell is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to graveyard
///   (CR 701.5).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for "pay {3}?"</b> — same auto-pay posture as Daze /
///   Mana Leak / Cursecatcher. Real "would you like to pay?" choice is
///   queued behind a future agent-prompt surface.
/// - <b>Bot affordability sees worst-case cost</b> — HeuristicBotAgent's
///   "can I cast this?" picker calls
///   <see cref="CostReduction.GetEffectiveCost"/> BEFORE choosing targets,
///   so it sees the printed {2}{U} and may skip casts that would actually
///   resolve as {U}. The reduction still applies for real (cast-time
///   payment uses the same call AFTER targets are stamped), so the bot
///   simply plays slightly more conservatively than optimal. Polish for
///   later — same posture as a hypothetical "Spell Pierce sees the
///   target's CMC."
/// </summary>
[CardName("Mystical Dispute")]
public static class MysticalDisputeFactory
{
    public const string CardName = "Mystical Dispute";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Cost reduction granted when Mystical Dispute targets a blue spell
    /// (CR 117.7). Exposed as a constant for tests / mirror docs.
    /// </summary>
    public const int BlueTargetGenericReduction = 2;

    /// <summary>
    /// Pay-or-counter rider (CR 118.4 — "unless its controller pays {3}").
    /// </summary>
    public const int UnlessPayGeneric = 3;

    /// <summary>
    /// Construct Mystical Dispute as an Instant card with owner / controller
    /// wired + the target-conditional cost-reduction ability attached. The
    /// resolve-time SpellDefinition (counter-unless-pay-{3}) is built on
    /// demand via <see cref="BuildSpellDefinition"/> — mirrors the shape of
    /// <see cref="ManaLeakFactory"/> / <see cref="NegateFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "This spell costs {2} less to cast if it targets a blue
        // spell." Whole-reducer shape: the closure inspects the card's
        // pending cast targets (stamped by SpellCastFlow right after target
        // collection — see Card.PendingCastTargets). Returns 2 when any
        // chosen target is a blue spell, else 0. Floor-at-zero is enforced
        // by CostReduction.GetEffectiveCost so {2}{U} → {U} (not below).
        card.AddAbility(new CostReductionAbility(
            totalReducer: _ => TargetsBlueSpell(card) ? BlueTargetGenericReduction : 0,
            description: "Costs {2} less to cast if it targets a blue spell"));

        return card;
    }

    /// <summary>
    /// Predicate consulted at cost-calc time: does the card's pending cast
    /// target set include at least one blue spell? Tolerates the no-target
    /// case (null / empty) — bot affordability and shape-only tests both
    /// call <see cref="CostReduction.GetEffectiveCost"/> before targets
    /// are picked.
    /// </summary>
    private static bool TargetsBlueSpell(ICard card)
    {
        var pending = (card as Card)?.PendingCastTargets;
        if (pending == null) return false;

        foreach (var bucket in pending)
        {
            foreach (var raw in bucket)
            {
                if (raw is not ISpell spell) continue;
                if (CardColors.GetColors(spell.Card).Contains(ManaColor.Blue))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Build the "counter target spell unless its controller pays {3}"
    /// SpellDefinition. Mirrors <see cref="ManaLeakFactory.BuildDefinition"/>
    /// with N=3. CR 608.2b — illegal target at resolution is handled by the
    /// pre-resolve target-legality check; this body assumes the resolved
    /// target is still a live <see cref="ISpell"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster to a live engine object (typically pass-through in tests;
    /// production callers route via a TargetResolver service).</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
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
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Mystical Dispute — counter target spell unless its controller pays {3}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 118.4 — target's controller may pay {3} to
                            // prevent the counter. v1 auto-pays when able
                            // (same posture as Mana Leak / Cursecatcher /
                            // Daze).
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
