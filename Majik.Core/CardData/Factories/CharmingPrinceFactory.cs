using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Charming Prince (Throne of Eldraine / Wilds of
/// Eldraine, {1}{W}).
///
/// Creature — Human Noble 2/2. Oracle text:
///   "When this creature enters, choose one —
///    • Scry 2.
///    • You gain 3 life.
///    • Exile another target creature you own. Return it to the
///      battlefield under your control at the beginning of the next
///      end step."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Noble, mana cost {1}{W}. Color identity white
///   (derived from {W} pip per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>ETB modal triggered ability</b> (CR 700.2d — "Choose one —",
///   CR 603.1 / CR 603.6a): three modes, one chosen at resolve time via
///   <see cref="IPlayerAgent.ChooseModeAsync"/> (same sync-over-async
///   posture as <see cref="TirelessProvisionerFactory"/>). A per-card mode
///   selector is captured at factory time (mirrors
///   <see cref="PlagueEngineerFactory"/>'s <c>typeChooser</c>) so tests can
///   supply a deterministic mode without registering a full agent.
///
/// ## Modes
/// - <b>Mode 0 — Scry 2</b> (CR 701.20): runs the standard
///   <see cref="ScryAction"/> pipeline for N=2 (same as
///   <see cref="PreordainFactory"/>). Agent's
///   <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> is consulted when
///   an agent is registered; otherwise falls back to all-bottom.
/// - <b>Mode 1 — You gain 3 life</b> (CR 119.3): identical to
///   <see cref="HealerOfTheGladeFactory"/>'s ETB — controller calls
///   <c>controller.GainLife(3)</c>.
/// - <b>Mode 2 — Blink: exile another target creature you own, return at
///   next end step</b> (CR 603.7 / CR 701.21):
///   - "creature you own" = <c>target.Owner == caster</c> checked at
///     resolve time (CR 608.2b). "Another" = distinct object from the
///     Prince itself (CR 115.5b).
///   - Exile: owner-routed zone moves (Battlefield → Exile).
///   - Delayed return: <see cref="DelayedTriggeredAbility"/> registered on
///     the supplied <see cref="TriggerManager"/> fires on the first
///     <see cref="StepStartedEvent"/> with <c>StepType == End</c> after
///     the ETB resolved. Return is "under your control" (the caster's
///     control — not necessarily the owner, but for the "you own"
///     predicate caster and owner are the same at this resolution).
///   - Same delayed-end-step pattern as
///     <see cref="PheliaExuberantShepherdFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only, no agent, no
///   TriggerManager. Defaults to mode 1 (gain 3 life) for the shape-only
///   path (no-target, safest default). Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, int, TriggerManager?)"/> — sets the
///   mode index at factory time via a captured closure. Mode 2
///   requires a non-null <paramref name="triggers"/> for the delayed-return
///   rider; without one the blink fires but no return is registered.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode prompt</b>: the mode is captured at factory
///   time for test convenience. When the engine's "mode choice on stack
///   entry" infrastructure ships (CR 700.2d prompt surface), the captured
///   <c>mode</c> closure becomes the wiring point for the agent call.
/// - <b>Token blink</b>: CR 111.8 — if a token is the blink target it
///   ceases to exist on exile; the return guard on <c>Zone == Exile</c>
///   cleanly no-ops (same posture as <see cref="PheliaExuberantShepherdFactory"/>).
/// </summary>
[CardName("Charming Prince")]
public static class CharmingPrinceFactory
{
    public const string CardName = "Charming Prince";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Mode index for "Scry 2."</summary>
    public const int ModeScry = 0;
    /// <summary>Mode index for "You gain 3 life."</summary>
    public const int ModeGainLife = 1;
    /// <summary>Mode index for "Exile target creature you own; return next end step."</summary>
    public const int ModeBlink = 2;
    private const int LifeGainAmount = 3;
    private const int ScryAmount = 2;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Scry 2.",
        "You gain 3 life.",
        "Exile another target creature you own. Return it to the battlefield under your control at the beginning of the next end step.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Draw,         // Scry 2 — card quality.
        BotIntent.Heal,         // Gain 3 life.
        BotIntent.Protection,   // Blink a creature — ETB value / protection.
    };

    /// <summary>
    /// Construct Charming Prince with no live wiring. The ETB trigger is
    /// attached for shape inspection. Defaults to mode 1 (gain 3 life) since
    /// mode 2 (blink) requires a target to be useful. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, mode: ModeGainLife, triggers: null);

    /// <summary>
    /// Construct Charming Prince with an explicit mode and optional
    /// <see cref="TriggerManager"/>. The mode is captured into the ETB
    /// effect closure so tests can exercise each arm without a full agent.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered with the bus and (for mode 2) the delayed end-step return
    /// is registered after exile resolves.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=scry, 1=gain life, 2=blink).
    /// Overridden by a registered <see cref="IPlayerAgent"/> if one is
    /// present in <see cref="AgentRegistry"/>.</param>
    /// <param name="triggers">TriggerManager — required for bus-driven ETB
    /// and for mode 2's delayed end-step return. May be null.</param>
    public static Creature Create(Player owner, int mode = ModeGainLife, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Noble });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, choose one —
        //    • Scry 2.
        //    • You gain 3 life.
        //    • Exile another target creature you own. Return it to the
        //      battlefield under your control at the beginning of the next
        //      end step."
        // Modal body — mode is resolved via AgentRegistry (when an agent is
        // registered) or the supplied mode parameter.
        // Mode 2 declares a 0..1 target request (MinTargets=0 so modes 0/1
        // don't gate ETB when the unchosen mode 2 carries a target slot).
        // CR 700.2d — "choose one" pick count is 1.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: choose one — scry 2; you gain 3 life; blink creature you own",
            () =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = PickMode(controller, mode);

                switch (chosenMode)
                {
                    case ModeScry:
                        ExecuteScry(controller);
                        break;

                    case ModeGainLife:
                        ExecuteGainLife(controller);
                        break;

                    case ModeBlink:
                        ExecuteBlink(etbTrigger, card, controller, triggers);
                        break;
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // Mode 2 target slot. MinTargets=0 so modes 0 and 1 don't
                // require a target to be chosen (CR 700.2d — only the
                // chosen mode's targeting is relevant).
                new TargetRequest(
                    Description: "another target creature you own",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Owner, owner))
                        .Where(c => !ReferenceEquals(c, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls
    /// back to the captured <paramref name="defaultMode"/> (the factory-
    /// time mode parameter).
    /// </summary>
    private static int PickMode(Player controller, int defaultMode)
    {
        var agent = AgentRegistry.Get(controller);
        if (agent == null) return defaultMode;

        try
        {
            var pick = agent.ChooseModeAsync(
                    ctx: null!,
                    modes: Modes,
                    modeIntents: ModeIntents)
                .GetAwaiter().GetResult();

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back
            // to the deterministic default (same pattern as
            // TirelessProvisionerFactory).
        }

        return defaultMode;
    }

    /// <summary>
    /// Mode 0 — Scry 2 (CR 701.20). Identical to
    /// <see cref="PreordainFactory"/>'s scry body (N=2) without the draw.
    /// </summary>
    private static void ExecuteScry(Player controller)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            // Sync-over-async — same posture as Preordain / Opt factories.
            // TODO: drop sync-over-async once IEffect.Execute becomes async.
            decision = agent.ChooseScryDecisionAsync(null, peeked)
                .GetAwaiter().GetResult();
        }
        else
        {
            // Pre-agent default: all peeked cards to bottom.
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }

    /// <summary>
    /// Mode 1 — You gain 3 life (CR 119.3). Identical to
    /// <see cref="HealerOfTheGladeFactory"/>'s ETB body.
    /// </summary>
    private static void ExecuteGainLife(Player controller)
    {
        controller.GainLife(LifeGainAmount);
    }

    /// <summary>
    /// Mode 2 — Exile another target creature you own. Return it to the
    /// battlefield under your control at the beginning of the next end step
    /// (CR 603.7 / CR 701.21).
    ///
    /// "You own" = <c>target.Owner == controller</c> checked at resolution
    /// (CR 608.2b — illegal-at-resolve target → no-op). Same delayed
    /// end-step pattern as <see cref="PheliaExuberantShepherdFactory"/>.
    /// </summary>
    private static void ExecuteBlink(
        TriggeredAbility trigger,
        Creature source,
        Player controller,
        TriggerManager? triggers)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Creature target) return;

        // CR 608.2b — resolution-time legality re-checks.
        if (target.Zone != ZoneType.Battlefield) return;
        if (ReferenceEquals(target, source)) return;           // "another"

        // "creature you own" — ownership check (not just control).
        // CR 108.3 — ownership is set at game start; for the purpose of
        // mode 2 the caster must OWN the target (distinct from Cloudshift's
        // "you control" predicate).
        if (!ReferenceEquals(target.Owner, controller)) return;

        var targetOwner = target.Owner!;

        // CR 701.21 — Exile. Owner-routed zone moves so LTB events fire.
        targetOwner.Zones.Battlefield.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);

        // CR 603.7 — register a delayed end-step return.
        // Skipped when no TriggerManager is wired (shape-only tests).
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var returnEffect = new Effect(
            $"{CardName}: return exiled creature at next end step (CR 603.7)",
            () =>
            {
                // CR 111.8 — tokens cease to exist when they leave the
                // battlefield; defensively skip if the card has already
                // moved out of exile (e.g. second exile, SBA token cleanup).
                if (target.Zone != ZoneType.Exile) return;

                // "under your control" — return routes through the
                // controller (caster's) zone. Because "you own" was
                // enforced at exile time, controller == owner here.
                targetOwner.Zones.Exile.RemoveCard(target);
                targetOwner.Zones.Battlefield.AddCard(target);
                target.SetZone(ZoneType.Battlefield);
                target.SetController(controller);
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }
}
