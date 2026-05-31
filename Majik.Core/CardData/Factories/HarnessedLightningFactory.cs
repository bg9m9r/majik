using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Harnessed Lightning (Aether Revolt, {1}{R}).
///
/// Instant. Oracle text:
///   "You get {E}{E}{E} (three energy counters). Choose target creature.
///    You may pay X {E}. Harnessed Lightning deals X damage to that
///    creature."
///
/// Modern Boros/Izzet Energy's flex burn slot — pays its own ramp at
/// {1}{R} (banks three energy whether or not it kills) and scales as
/// the energy pool grows. Pairs with Whirler Virtuoso (energy battery /
/// flying flood) and Aetherworks Marvel (graveyard feed).
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{R}.
/// - <b>Single 1..1 "target creature"</b> <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Burn"/> — the bot's burn scorer
///   sees damage-to-creature as removal-shaped). Mirrors the binder
///   pattern shared with Lightning Strike's creature-only variant
///   and the burn-creature templates.
/// - <b>Resolve body</b> (CR 608.2):
///     1. Controller gets <c>{E}{E}{E}</c> via
///        <see cref="Player.GainEnergy"/>(3) (CR 106.13b — energy is a
///        single integer ledger; the three-pip oracle wording collapses
///        to a single call). The energy gain is unconditional — fires
///        even when the target is no longer legal at resolution
///        (CR 608.2b — the "deal X damage" step fizzles but the
///        printed text before "Harnessed Lightning deals" is a
///        separate sentence that still resolves).
///     2. Resolve the chosen creature; if it left the battlefield
///        between cast and resolve (CR 608.2b), the damage step
///        fizzles — the energy gain already committed.
///     3. <b>Optional "pay X energy → X damage"</b>: query the
///        controller's <see cref="IPlayerAgent.ChooseXAsync"/> at
///        resolution to pick X (clamped to <c>0..controller.EnergyCounters</c>
///        AFTER the +3 gain). When no agent is registered, default to
///        <c>min(energy, target's remaining toughness)</c> — a
///        conservative "kill the target when possible, never overspend"
///        heuristic mirrored from <see cref="BurstLightningFactory"/>'s
///        kicker fallback.
///     4. Spend X energy via <see cref="Player.PayEnergy"/> and deal X
///        damage to the target through <see cref="Fx.DealDamage"/>
///        (target is a Creature; no any-target routing needed —
///        creature damage marks via the standard
///        <see cref="OracleSpellBinder"/> path).
///     5. When X == 0 (the agent chose to skip the X-pay rider), no
///        damage and no energy spent — the printed "you may" makes the
///        whole rider optional (CR 117.5).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Resolution-time agent X prompt vs cast-time X</b>:
///   <see cref="IPlayerAgent.ChooseXAsync"/> is intended for cast-time
///   X declarations (CR 107.3c). Harnessed Lightning's printed "you may
///   pay X" is a resolve-time decision (CR 608.2 — the spell's effects
///   sequence pays X DURING resolution, not when announcing the cast).
///   v1 reuses <c>ChooseXAsync</c> at resolution as a pragmatic
///   simplification — same posture as Voltage Surge's optional
///   sacrifice-an-artifact additional cost being chosen pre-cast even
///   though the printed wording is "as you cast". A dedicated
///   resolve-time "may pay X" agent surface (paired with a
///   <c>ChooseYesNoAsync</c> + bounded X prompt) is the canonical
///   follow-up; the bot's burn-EV scorer already understands "spend
///   energy → damage" trades.
/// - <b>Energy-budget-aware bot fallback</b>: the no-agent fallback
///   pays min(energy, remaining toughness). A smarter bot might keep
///   energy reserved for Aetherworks Marvel's 6-energy activation —
///   that decision belongs in the EV layer, not the resolve closure.
/// </summary>
[CardName("Harnessed Lightning")]
public static class HarnessedLightningFactory
{
    public const string CardName = "Harnessed Lightning";
    public const string PrintedManaCost = "{1}{R}";
    public const int EnergyGain = 3;

    /// <summary>CardDef DSL — card shape only. The "gain energy + may
    /// pay X energy for X damage" body is built at cast time via
    /// <see cref="BuildSpellDefinition"/> (the runtime needs the
    /// caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Harnessed
    /// Lightning is cast. Declares a single 1..1 "target creature"
    /// <see cref="TargetRequest"/>; on resolution grants the controller
    /// three energy, then optionally pays X energy for X damage to the
    /// chosen creature (X picked by the controller's agent via
    /// <see cref="IPlayerAgent.ChooseXAsync"/>; no-agent fallback pays
    /// min(energy, target toughness)).
    /// </summary>
    /// <param name="controller">Spell controller — receives the energy
    /// gain and pays X energy on resolution.</param>
    /// <param name="source">The Harnessed Lightning card instance —
    /// passed to <see cref="IPlayerAgent.ChooseXAsync"/> as the source
    /// reference so the agent's EV layer can identify the prompt.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        ICard source,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                var targetRaw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: gain {{E}}{{E}}{{E}}, optional pay X {{E}} → X damage",
                        async ctx =>
                        {
                            // CR 106.13b — the {E}{E}{E} gain is
                            // unconditional and fires before the
                            // "deal X damage" rider, so the controller
                            // can spend the freshly-banked energy on
                            // the same spell's X.
                            controller.GainEnergy(EnergyGain);

                            // CR 608.2b — recheck target legality at
                            // resolve. If the chosen creature is gone,
                            // the damage step fizzles; the energy gain
                            // above already committed.
                            if (targetRaw is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Agent picks X. ChooseXAsync is the
                            // cast-time variable-cost surface (CR 107.3c);
                            // v1 reuses it at resolve as the pragmatic
                            // "may pay X" prompt — see factory xmldoc
                            // "Deferred (v1 gaps)".
                            var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                            int x;
                            if (agent != null)
                            {
                                x = await agent.ChooseXAsync(ctx.Game!, source: source)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                // No-agent fallback: spend up to the
                                // target's remaining toughness — kill
                                // the target when affordable, never
                                // overspend on excess damage.
                                var lethal = Math.Max(
                                    0, target.GetToughness() - target.Damage);
                                x = Math.Min(controller.EnergyCounters, lethal);
                            }

                            // Clamp to [0, available energy] — the
                            // printed "you may pay X" is bounded by
                            // CR 117.5 (can't pay a cost you can't
                            // afford); agent-supplied X gets the same
                            // clamp.
                            if (x < 0) x = 0;
                            if (x > controller.EnergyCounters)
                            {
                                x = controller.EnergyCounters;
                            }
                            if (x == 0) return; // "may pay X" → X = 0 skips

                            if (!controller.PayEnergy(x)) return;
                            Fx.DealDamage(target, x);
                        }),
                };
            });
    }
}
