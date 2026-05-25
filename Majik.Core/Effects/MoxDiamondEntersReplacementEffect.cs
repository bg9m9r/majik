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
            replace: (intent, _) => ResolveReplacement(intent),
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

    private ZoneMoveIntent ResolveReplacement(ZoneMoveIntent intent)
    {
        // Live controller — the player making the choice is whoever is
        // currently controlling Mox Diamond at ETB time (CR 614.6 — choice
        // belongs to the controller of the would-enter object; falls back
        // to owner when no controller has been set).
        var chooser = intent.Controller ?? _source.Controller ?? _source.Owner;
        if (chooser == null)
        {
            // Defensive: no chooser available → leave the move alone (Mox
            // enters normally). Should never happen in real play.
            return intent;
        }

        // CR 614.6 — "instead" replacement gated on whether the alternative
        // cost can be paid. If the chooser has no land cards in hand,
        // they cannot discard a land, so the "yes" branch is illegal —
        // run the sacrifice tail directly.
        var lands = chooser.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();

        bool wantsToDiscard = lands.Count > 0 && PromptYesNo(chooser);

        if (!wantsToDiscard)
        {
            // "If you don't, sacrifice Mox Diamond." Redirect the would-
            // enter move into the graveyard so Mox Diamond never actually
            // hits the battlefield (CR 614 — replacement effects fire
            // before the affected event, so the original Stack →
            // Battlefield move is rewritten Stack → Graveyard).
            return intent with { ToZone = ZoneType.Graveyard };
        }

        // "You may discard a land card instead." Pick which Land to
        // discard (deterministic first when no agent is registered) and
        // move it Hand → Graveyard. Mox Diamond's intent passes through
        // unchanged so it lands on the battlefield.
        var pick = PromptLandPick(chooser, lands) ?? lands[0];
        chooser.Zones.Hand.RemoveCard(pick);
        chooser.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);

        return intent;
    }

    private bool PromptYesNo(Player chooser)
    {
        var agent = _agentSelector != null
            ? _agentSelector(chooser)
            : AgentRegistry.Get(chooser);
        if (agent == null) return false; // no agent → default to "sacrifice"

        return agent.ChooseYesNoAsync(
            "Discard a land card to keep Mox Diamond on the battlefield?",
            BotIntent.DiscardCost)
            .GetAwaiter().GetResult();
    }

    private ICard? PromptLandPick(Player chooser, IReadOnlyList<ICard> lands)
    {
        var agent = _agentSelector != null
            ? _agentSelector(chooser)
            : AgentRegistry.Get(chooser);
        if (agent == null) return lands.Count > 0 ? lands[0] : null;

        return agent.ChooseFromHandAsync(chooser, lands, BotIntent.DiscardCost)
            .GetAwaiter().GetResult();
    }
}
