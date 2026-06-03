using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Needle Drop (Born of the Gods, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Needle Drop deals 1 damage to any target that was dealt damage this
///    turn.
///    Draw a card."
///
/// ## Implementation
///
/// The defining mechanic is the targeting <em>restriction</em>: the spell may
/// only target an object that "was dealt damage this turn" (CR 120.3). That
/// per-turn flag is stamped at every damage seam onto
/// <see cref="Player.WasDealtDamageThisTurn"/> /
/// <see cref="Permanent.WasDealtDamageThisTurn"/> and cleared at cleanup
/// (CR 514.2). It is surfaced as the
/// <c>any_target_dealt_damage_this_turn</c> filter on
/// <see cref="TargetFilters"/>, whose candidate gatherer enumerates only the
/// legal (already-damaged) creatures / planeswalkers / players — so the agent
/// is only ever offered objects that satisfy the restriction and the
/// resolution-time re-check (CR 608.2b) fizzles cleanly if the target somehow
/// no longer qualifies.
///
/// Card shape comes from the <see cref="CardDef"/> DSL (instant, {R}); the
/// resolve-time body lives in <see cref="BuildSpellDefinition"/> because the
/// targeting restriction needs a live <see cref="GameContext"/> gatherer (not
/// expressible in the data-only DSL <c>.To(TargetKind)</c> sugar).
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. Deal 1 damage to the chosen (already-damaged) target via
///      <see cref="Fx.DealDamageAny(object,int)"/> — creature / player /
///      planeswalker / battle (CR 306.7 routes a planeswalker to loyalty
///      removal).
///   2. The caster draws a card (CR 120.2) via <see cref="Fx.DrawCards"/>.
/// </summary>
[CardName("Needle Drop")]
public static class NeedleDropFactory
{
    public const string CardName = "Needle Drop";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 1 damage.</summary>
    public const int Damage = 1;

    /// <summary>CR 120.2 — draw a card after the damage.</summary>
    private const int CardsDrawn = 1;

    /// <summary>The targeting filter key — "was dealt damage this turn"
    /// (CR 120.3), resolved by <see cref="TargetFilters"/>.</summary>
    private const string DamagedTargetFilter = "any_target_dealt_damage_this_turn";

    /// <summary>CardDef DSL — card shape only ({R} Instant). The targeting
    /// restriction + resolve body live in <see cref="BuildSpellDefinition"/>
    /// because they need a live game context.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Needle Drop is cast.
    /// Single 1..1 "any target that was dealt damage this turn" request whose
    /// candidate gatherer offers only already-damaged creatures /
    /// planeswalkers / players (CR 120.3). On resolution:
    ///   1. Deals <see cref="Damage"/> (1) damage to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3 / CR 306.7).
    ///   2. The <paramref name="caster"/> draws a card (CR 120.2).
    /// </summary>
    /// <param name="caster">The player who cast Needle Drop; draws the card.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                TargetFilters.ToTargetRequest(
                    DamagedTargetFilter, verb: "deal 1 damage", intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Needle Drop: 1 damage to a damaged target, draw a card", _ =>
                    {
                        // CR 608.2b — re-check the targeting restriction at
                        // resolution: a target that was NOT dealt damage this
                        // turn (e.g. an illegal pick) does nothing. The damaged
                        // gatherer normally guarantees this, but the re-check
                        // keeps the spell honest if the pick is forced.
                        if (target is null || !TargetFilters.WasDealtDamageThisTurn(target))
                        {
                            // CR 608.2c — Needle Drop has a single target; if it
                            // is illegal on resolution the spell doesn't resolve
                            // and the draw does NOT happen (the whole spell is
                            // countered by the game rules). Short-circuit.
                            return System.Threading.Tasks.ValueTask.CompletedTask;
                        }

                        // 1. CR 120.3 / CR 306.7 — deal 1 damage to any target.
                        Fx.DealDamageAny(target, Damage);

                        // 2. CR 120.2 — draw a card.
                        Fx.DrawCards(caster, CardsDrawn);

                        return System.Threading.Tasks.ValueTask.CompletedTask;
                    }),
                };
            });
    }
}
