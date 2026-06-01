using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — printed-replacement lifecycle binder for Mox Diamond
/// (Stronghold):
///   "If Mox Diamond would enter, you may discard a land card instead.
///    If you don't, sacrifice Mox Diamond."
///
/// The replacement fires once each time Mox Diamond would enter the
/// battlefield (CR 614.6 — self-replacements on the entering object):
/// <list type="number">
///   <item>Prompt the controller via
///         <see cref="IPlayerAgent.ChooseYesNoAsync"/> (intent
///         <see cref="BotIntent.CostToDecline"/> — the choice's cost is
///         a card from hand, downside-tagged so default-agent posture
///         prefers "no" / sacrifice when no land is available). If the
///         controller has zero land cards in hand the prompt is skipped
///         (the "yes" branch is illegal — see CR 614.6 / CR 117.x for
///         "you may ... instead" replacements: if the alternative cost
///         cannot be paid, the replacement still applies but the
///         "instead" branch fails, so the "sacrifice" tail runs).</item>
///   <item>If yes: pick a specific land card from hand via
///         <see cref="IPlayerAgent.ChooseFromHandAsync"/>
///         (<see cref="BotIntent.DiscardCost"/>), move it Hand → Graveyard,
///         and let the original intent through unchanged so Mox Diamond
///         lands on the battlefield.</item>
///   <item>If no: rewrite the intent's <see cref="ZoneMoveIntent.ToZone"/>
///         to <see cref="ZoneType.Graveyard"/> — Mox Diamond is "sacrificed"
///         (CR 701.16 — it never actually entered the battlefield; the
///         replacement redirects the move into the graveyard).</item>
/// </list>
///
/// ## Lifecycle scope
/// Unlike battlefield-presence replacements (Containment Priest /
/// Hardened Scales / Anger of the Gods), Mox Diamond's printed
/// replacement applies to the SAME card's own ETB attempt — so the
/// lifecycle binder must register the effect BEFORE the card enters
/// the battlefield. The factory registers on construction (the card is
/// either in the library at game-start or in hand by the time the
/// player casts it; either way the replacement is live when the cast
/// resolution attempts the Stack → Battlefield move).
///
/// Self-replacements are gated with <c>OneShot = false</c> + a per-
/// invocation history bit (CR 616.1c — each effect fires at most once
/// per intent), so a single "would enter" event prompts the agent
/// exactly once even if the bus replays. The replacement stays
/// registered for future ETB attempts (reanimation, blink, return-from-
/// exile) — each ETB attempt re-runs the prompt.
///
/// ## Agent selector
/// The lifecycle takes a <see cref="Func{Player, IPlayerAgent}"/> so the
/// caller can override the registered <see cref="AgentRegistry"/> lookup
/// (tests pass a scripted agent directly). Falls back to
/// <see cref="AgentRegistry.Get(Player)"/> when the selector is null
/// (production wiring path — same posture as
/// <see cref="Majik.Core.CardData.Factories.AnnihilatorFactory"/>).
///
/// ## Why register via the bus instead of a built-in ETB intent flag
/// The <see cref="ZoneMoveIntent"/> already carries side-channel hooks
/// for "enters tapped" / "enters with counters" because those are
/// universal sub-cases. "Pay X or sacrifice" is one-off enough that the
/// bus-registered <see cref="LambdaReplacement{TIntent}"/> pattern stays
/// the cleanest fit — same shape every per-card printed replacement
/// (Containment Priest, Rest in Peace, Leyline of the Void) already
/// uses.
/// </summary>
public sealed class MoxDiamondEntersReplacementEffect
{
    private readonly Card _source;
    private readonly ReplacementBus _bus;
    private readonly Func<Player, IPlayerAgent?>? _agentSelector;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _registered;

    public MoxDiamondEntersReplacementEffect(
        Card source,
        ReplacementBus replacementBus,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _agentSelector = agentSelector;

        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) =>
                ReferenceEquals(intent.Card, _source)
                && intent.ToZone == ZoneType.Battlefield
                && intent.FromZone != ZoneType.Battlefield,
            // Sync path (ReplacementBus.Apply / direct-call unit tests):
            // looks the agent up off the registry and bridges the already-
            // completed scripted/heuristic prompt.
            replace: (intent, _) => ResolveReplacement(intent, ctx: null),
            // PLAN 08 — async path (ReplacementBus.ApplyAsync): awaits the
            // controller's agent off the live ResolutionContext so the
            // "discard a land?" / which-land prompt never bridges sync-over-
            // async on a human's think-time.
            replaceAsync: (intent, _, ctx) => ResolveReplacementAsync(intent, ctx),
            oneShot: false,
            tag: this);
    }

    /// <summary>Whether the replacement is currently registered on the bus.</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Register the replacement on the bus. Idempotent — re-attaching is a
    /// no-op. Called by the factory once on card construction.
    /// </summary>
    public void Attach()
    {
        if (_registered) return;
        _bus.Register(_effect);
        _registered = true;
    }

    /// <summary>
    /// Unregister the replacement. Idempotent. Typically only invoked
    /// when tearing down a game (e.g. test fixtures).
    /// </summary>
    public void Detach()
    {
        if (!_registered) return;
        _bus.Unregister(_effect);
        _registered = false;
    }

    private const string DiscardQuestion =
        "Discard a land card to keep Mox Diamond on the battlefield?";

    /// <summary>
    /// Synchronous path (<see cref="ReplacementBus.Apply{TIntent}"/>) — the
    /// no-resolution-context path (shape-only callers, non-cast zone moves).
    /// CR 614.6 prompting is INTENTIONALLY NOT done here: a player choice must
    /// be <c>await</c>ed, never bridged sync-over-async, so the "discard a
    /// land?" prompt lives exclusively on <see cref="ResolveReplacementAsync"/>
    /// (the production cast-resolution path). This sync path applies the
    /// deterministic no-prompt posture: sacrifice Mox Diamond (the conservative
    /// "no" branch — a card without a live agent never opts into the alternative
    /// discard cost). Mirrors the historical no-agent fallback.
    /// </summary>
    private ZoneMoveIntent ResolveReplacement(ZoneMoveIntent intent, ResolutionContext? ctx)
    {
        _ = ctx;
        var chooser = ResolveChooser(intent);
        if (chooser == null) return intent;

        // No prompt on the sync path — "if you don't, sacrifice Mox Diamond."
        return intent with { ToZone = ZoneType.Graveyard };
    }

    /// <summary>
    /// PLAN 08 — async path (<see cref="ReplacementBus.ApplyAsync{TIntent}"/>).
    /// Genuinely <c>await</c>s the controller's agent for the "discard a land?"
    /// yes/no and the which-land pick so a human's choice never blocks a
    /// thread-pool thread on a sync-over-async bridge. CR 614.6 semantics
    /// (gated on the alternative cost being payable) are identical to the
    /// synchronous <see cref="ResolveReplacement"/>.
    /// </summary>
    private async ValueTask<ZoneMoveIntent?> ResolveReplacementAsync(
        ZoneMoveIntent intent, ResolutionContext ctx)
    {
        var chooser = ResolveChooser(intent);
        if (chooser == null) return intent;

        var lands = LandsInHand(chooser);

        bool wantsToDiscard = lands.Count > 0 && await PromptYesNoAsync(chooser, ctx).ConfigureAwait(false);
        if (!wantsToDiscard)
        {
            // "If you don't, sacrifice Mox Diamond." Redirect Stack →
            // Graveyard (CR 614 — Mox Diamond never actually enters).
            return intent with { ToZone = ZoneType.Graveyard };
        }

        var pick = await PromptLandPickAsync(chooser, lands, ctx).ConfigureAwait(false) ?? lands[0];
        DiscardPick(chooser, pick);
        return intent;
    }

    // ------------------------------------------------------------------
    // Shared helpers (sync + async paths).
    // ------------------------------------------------------------------

    /// <summary>CR 614.6 — the chooser is the controller of the would-enter
    /// object (falls back to owner when no controller has been set).</summary>
    private Player? ResolveChooser(ZoneMoveIntent intent) =>
        intent.Controller ?? _source.Controller ?? _source.Owner;

    /// <summary>CR 614.6 — the alternative cost ("discard a land") is only
    /// payable when the chooser has a land card in hand.</summary>
    private static List<ICard> LandsInHand(Player chooser) =>
        chooser.Zones.Hand.GetCards().Where(c => c.HasType(CardType.Land)).ToList();

    /// <summary>Move the chosen land Hand → Graveyard (CR 701.16).</summary>
    private static void DiscardPick(Player chooser, ICard pick)
    {
        chooser.Zones.Hand.RemoveCard(pick);
        chooser.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }

    /// <summary>Async path agent resolution — the test-supplied selector wins
    /// (so scripted tests drive a specific agent), else the live context agent,
    /// else the ambient registry.</summary>
    private IPlayerAgent? AgentFor(Player chooser, ResolutionContext ctx) =>
        _agentSelector != null ? _agentSelector(chooser)
        : ctx.Agent ?? AgentRegistry.Get(chooser);

    private async ValueTask<bool> PromptYesNoAsync(Player chooser, ResolutionContext ctx)
    {
        var agent = AgentFor(chooser, ctx);
        if (agent == null) return false;

        return await agent.ChooseYesNoAsync(DiscardQuestion, BotIntent.DiscardCost, ctx.Ct)
            .ConfigureAwait(false);
    }

    private async ValueTask<ICard?> PromptLandPickAsync(
        Player chooser, IReadOnlyList<ICard> lands, ResolutionContext ctx)
    {
        var agent = AgentFor(chooser, ctx);
        if (agent == null) return lands.Count > 0 ? lands[0] : null;

        return await agent.ChooseFromHandAsync(chooser, lands, BotIntent.DiscardCost, ctx.Ct)
            .ConfigureAwait(false);
    }
}
