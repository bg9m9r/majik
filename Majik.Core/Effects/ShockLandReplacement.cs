using Majik.Core.Cards;
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

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        var controller = intent.Controller ?? _land.Owner;
        if (controller is null)
        {
            // No controller known — shape-only path, enter tapped (the
            // pre-agent posture for the same intent).
            return intent with { EntersTapped = true };
        }

        // CR 119.4 deferral — refuse the auto-suicide. At low life the
        // production replacement never prompts and always enters tapped.
        if (controller.LifeTotal <= 2)
        {
            return intent with { EntersTapped = true };
        }

        var agent = AgentRegistry.Get(controller);
        if (agent is null)
        {
            // No agent registered — preserve the legacy MVP posture
            // (auto-pay-2-life when controller has > 2 life). Existing
            // integration tests / shape-only paths depend on this branch.
            controller.LoseLife(2);
            return intent with { EntersTapped = false };
        }

        bool wantsToPay;
        try
        {
            // Sync-over-async wart shared with every v1 effect closure
            // that prompts an agent (Scry / Surveil / LibraryPick / etc.).
            // TODO (v2): make effects async so we can await here.
            wantsToPay = agent.ChooseYesNoAsync(
                ctx: null,
                question: $"Pay 2 life for {_land.Name} to enter untapped?",
                sourceCardName: _land.Name,
                ct: default).GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent fault → fall back to tapped, no payment.
            return intent with { EntersTapped = true };
        }

        if (!wantsToPay)
        {
            return intent with { EntersTapped = true };
        }

        // CR 118.8 — pay 2 life via Player.LoseLife so SBA / combat
        // listeners observe the change. Enter untapped.
        controller.LoseLife(2);
        return intent with { EntersTapped = false };
    }
}
