using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 / Ravnica shock-land replacement. Watches the land's ETB
/// <see cref="ZoneMoveIntent"/> and either:
///   - prompts the controller's registered <see cref="IPlayerAgent"/> via
///     <see cref="IPlayerAgent.ChooseYesNoAsync(Majik.Core.Game.GameContext?,string,string?,System.Threading.CancellationToken)"/>
///     and applies the answer (pay 2 life + enter untapped on YES, enter
///     tapped on NO), or
///   - when no agent is registered, falls back to the pre-prompt MVP
///     posture ("pay 2 life if controller has &gt; 2 life, else tapped"),
///     which preserves no-agent integration tests / shape-only callers.
/// <para>
/// CR 119.4 deferral: at <c>LifeTotal &lt;= 2</c> the prompt is skipped and
/// the land enters tapped (the prompt would let the player auto-suicide;
/// the binder-chain refuses that until a real life-payment cost engine
/// lands). The test-only <c>ShockLandCycleFactory</c> exercises the
/// LifeTotal == 2 carve-out via its own predicate; both paths share the
/// agent prompt shape so a future unification swap remains mechanical.
/// </para>
/// </summary>
public sealed class ShockLandReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _land;

    public ShockLandReplacement(ICard land)
    {
        _land = land ?? throw new ArgumentNullException(nameof(land));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _land)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    /// <summary>
    /// Synchronous path (<see cref="ReplacementBus.Apply{TIntent}"/>) — the
    /// no-resolution-context path (shape-only callers, non-cast zone moves).
    /// CR 614 prompting is INTENTIONALLY NOT done here: a player choice must
    /// be <c>await</c>ed, never bridged sync-over-async, so the prompt lives
    /// exclusively on <see cref="ReplaceAsync"/> (the production entry path —
    /// play-from-hand, fetch, ramp, any "put onto the battlefield").
    /// <para>
    /// This sync path is reached only when a caller moves the land WITHOUT a
    /// <see cref="ResolutionContext"/> (so it cannot prompt). Per CR 614.1c
    /// ("you may pay 2 life. <b>If you don't, it enters tapped.</b>") the
    /// safe, rules-correct default for "no payment made" is to enter TAPPED —
    /// NOT to silently auto-pay 2 life untapped. Auto-paying here was the
    /// live-play bug: any sync land-into-play path (Growth Spiral / Uro /
    /// Cultivator Colossus, etc.) entered the shock land untapped and debited
    /// 2 life with no prompt. Production land-into-play paths now carry a
    /// <see cref="ResolutionContext"/> and route through <see cref="ReplaceAsync"/>
    /// so the controller is genuinely prompted; this sync fallback simply
    /// declines (enters tapped, pays nothing).
    /// </para>
    /// </summary>
    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        // No context to prompt on — decline the optional payment (CR 614.1c:
        // "if you don't [pay], it enters tapped"). Never auto-pay sync.
        return intent with { EntersTapped = true };
    }

    /// <summary>
    /// PLAN 08 — async path (<see cref="ReplacementBus.ApplyAsync{TIntent}"/>).
    /// Genuinely <c>await</c>s the controller's agent (threaded on
    /// <paramref name="ctx"/>, falling back to <see cref="AgentRegistry"/>)
    /// so a human's "pay 2 life?" think-time never blocks a thread-pool thread
    /// on a sync-over-async bridge. CR 118.8 / CR 119.4 semantics are identical
    /// to the synchronous <see cref="Replace"/>.
    /// </summary>
    public async ValueTask<ZoneMoveIntent?> ReplaceAsync(
        ZoneMoveIntent intent, IReadOnlyList<object> history, ResolutionContext ctx)
    {
        var controller = intent.Controller ?? _land.Owner;
        if (controller is null)
        {
            return intent with { EntersTapped = true };
        }

        // CR 119.4 deferral — refuse the auto-suicide. At low life the
        // production replacement never prompts and always enters tapped.
        if (controller.LifeTotal <= 2)
        {
            return intent with { EntersTapped = true };
        }

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent is null)
        {
            // No agent to prompt — decline the optional payment so the land
            // enters tapped (CR 614.1c "if you don't [pay], it enters tapped").
            // The old "auto-pay when life > 2" posture silently spent the
            // player's life with no prompt — that WAS the bug. A no-agent
            // path is a shape-only / non-interactive caller, so the safe
            // default is the no-payment branch.
            return intent with { EntersTapped = true };
        }

        bool wantsToPay;
        try
        {
            wantsToPay = await agent.ChooseYesNoAsync(
                ctx: ctx.Game,
                question: PromptText,
                sourceCardName: _land.Name,
                ct: ctx.Ct).ConfigureAwait(false);
        }
        catch
        {
            // Defensive: any agent fault → fall back to tapped, no payment.
            return intent with { EntersTapped = true };
        }

        return ApplyDecision(intent, controller, wantsToPay);
    }

    private string PromptText => $"Pay 2 life for {_land.Name} to enter untapped?";

    /// <summary>
    /// CR 118.8 — apply the controller's pay/decline decision. On YES debit 2
    /// life via <see cref="Player.LoseLife"/> (so SBA / combat listeners
    /// observe it) and enter untapped; on NO enter tapped, no payment.
    /// </summary>
    private static ZoneMoveIntent ApplyDecision(
        ZoneMoveIntent intent, Player controller, bool wantsToPay)
    {
        if (!wantsToPay)
        {
            return intent with { EntersTapped = true };
        }

        controller.LoseLife(2);

        // CR 118.8 — the 2 life is paid as a cost for the land to enter
        // untapped. Publish a LifePaidEvent (life-payment provenance, distinct
        // from a LifeChangedEvent decrease) so a "whenever a player pays life …"
        // payoff fires. The bus is looked up best-effort via EventBusRegistry
        // (this replacement has no IEventBus in scope) — no publish if none is
        // registered (direct-construction unit tests), mirroring the
        // Fx.DiscardCard / Fx.Sacrifice best-effort posture.
        EventBusRegistry.Get(controller)?.Publish(
            new LifePaidEvent(controller, 2, wasCost: true));

        return intent with { EntersTapped = false };
    }
}
