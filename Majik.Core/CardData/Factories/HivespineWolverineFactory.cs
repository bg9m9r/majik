using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hivespine Wolverine (Bloomburrow, {3}{G}{G}).
///
/// Creature — Elemental Wolverine 5/4. Oracle text (Scryfall, verified):
///   "When this creature enters, choose one —
///    • Put a +1/+1 counter on target creature you control.
///    • This creature fights target creature token.
///    • Destroy target artifact or enchantment."
///
/// ## Card shape (JSON)
/// The 5/4 Elemental Wolverine body, {3}{G}{G} cost, and types come from the
/// embedded <c>hivespine-wolverine.json</c> definition built through
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
/// The modal ETB trigger is hand-rolled and attached afterward — same
/// shape-from-JSON + bespoke-modal-ETB posture as
/// <see cref="KnightOfAutumnFactory"/>.
///
/// ## ETB modal triggered ability (CR 603.1 / CR 603.6a / CR 700.2d)
/// "Choose one —" with three modes, one chosen at stack entry via the declared
/// <see cref="ModeRequest"/> (CR 700.2d / CR 603.3) and surfaced on
/// <see cref="ResolutionContext.ChosenMode"/>; <see cref="PickModeAsync"/> reads
/// it, falling back to a registered agent then the factory-captured default.
/// Each mode declares its own 0..1 target slot (MinTargets=0 so the unchosen
/// modes' slots don't gate the ETB — only the chosen mode's targeting is
/// relevant, CR 700.2d).
///
/// ## Modes
/// - <b>Mode 0 — Put a +1/+1 counter on target creature you control</b>
///   (CR 122 / CR 613.7b): slot 0 gathers the controller's battlefield
///   creatures; the counter-driven +1/+1 resolves in the
///   <see cref="ContinuousEffectsService"/> counter postlude. Same target-counter
///   body as <see cref="GenerousVisitorFactory"/>.
/// - <b>Mode 1 — This creature fights target creature token</b>
///   (CR 701.12 Fight): slot 1 gathers battlefield creature <em>tokens</em>
///   (<see cref="Permanent.IsToken"/>, CR 111). Routed through
///   <see cref="Fx.Fight"/> so each creature deals damage equal to its
///   (pre-fight) power to the other simultaneously (CR 701.12a), honouring
///   deathtouch / lifelink. CR 608.2b — if Hivespine itself or the token has
///   left the battlefield (or the target is no longer a token creature) the
///   fight does nothing. Same Fight primitive as
///   <see cref="PreyUponFactory"/> / the fight templates.
/// - <b>Mode 2 — Destroy target artifact or enchantment</b> (CR 701.7): slot 2
///   gathers battlefield artifacts + enchantments; destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (indestructible cancels, CR 702.12).
///   Identical destroy body to <see cref="KnightOfAutumnFactory"/>'s mode 1.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + ETB trigger, no agent, no
///   TriggerManager. Defaults to mode 2 (destroy) — a single-target mode whose
///   target slot the dispatcher-path candidate fallback fills deterministically.
/// - <see cref="Create(Player, int, TriggerManager?)"/> — sets the mode index at
///   factory time via a captured closure; supplying a
///   <see cref="TriggerManager"/> additionally registers the ETB for bus-driven
///   firing.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode + target prompt</b>: the mode is captured at
///   factory time for test convenience and the target falls back to the first
///   legal candidate when no agent set one. Production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt before
///   triggers resolve. Same posture as <see cref="KnightOfAutumnFactory"/>.
/// </summary>
[CardName("Hivespine Wolverine")]
public static class HivespineWolverineFactory
{
    public const string CardName = "Hivespine Wolverine";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "hivespine-wolverine";

    /// <summary>Mode index for "Put a +1/+1 counter on target creature you control."</summary>
    public const int ModeCounter = 0;
    /// <summary>Mode index for "This creature fights target creature token."</summary>
    public const int ModeFight = 1;
    /// <summary>Mode index for "Destroy target artifact or enchantment."</summary>
    public const int ModeDestroy = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Put a +1/+1 counter on target creature you control.",
        "This creature fights target creature token.",
        "Destroy target artifact or enchantment.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Buff,     // +1/+1 counter on a creature you control.
        BotIntent.Removal,  // Fight a creature token (remove it).
        BotIntent.Removal,  // Destroy artifact / enchantment.
    };

    /// <summary>
    /// Construct Hivespine Wolverine from the JSON card shape and attach the
    /// modal ETB trigger. Supplying a <see cref="TriggerManager"/> additionally
    /// registers the ETB on the bus.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=counter, 1=fight, 2=destroy).
    /// Overridden by an engine-recorded / agent-chosen mode when present.</param>
    /// <param name="triggers">TriggerManager — required for bus-driven ETB
    /// firing. May be null.</param>
    public static Creature Create(Player owner, int mode = ModeDestroy, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner, replacements: null);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // ETB modal triggered ability (CR 603.1 / CR 603.6a / CR 700.2d).
        // Three modes; each declares its own 0..1 target slot (MinTargets=0
        // so the unchosen modes' slots never gate the ETB — CR 700.2d, only
        // the chosen mode's targeting matters).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: choose one — +1/+1 counter on target creature you control; "
            + "fight target creature token; destroy target artifact/enchantment",
            async ctx =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = await PickModeAsync(controller, mode, ctx).ConfigureAwait(false);

                switch (chosenMode)
                {
                    case ModeCounter:
                        ExecuteCounter(controller, etbTrigger);
                        break;

                    case ModeFight:
                        ExecuteFight(card, etbTrigger, ctx);
                        break;

                    case ModeDestroy:
                        ExecuteDestroy(controller, etbTrigger, ctx);
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
            modeRequest: new ModeRequest(
                Modes: Modes,
                MinModes: 1,
                MaxModes: 1,
                ModeIntents: ModeIntents),
            targetRequests: new[]
            {
                // Slot 0 — mode 0: target creature you control.
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => (ctx.Self ?? owner)
                        .Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
                // Slot 1 — mode 1: target creature token (any controller; CR 111).
                new TargetRequest(
                    Description: "target creature token",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.IsToken)
                        .Cast<object>()
                        .ToList()),
                // Slot 2 — mode 2: target artifact or enchantment (any controller).
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode resolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. PREFERS the engine-recorded mode the
    /// controller's agent chose at STACK ENTRY (CR 700.2d / CR 603.3, surfaced on
    /// <see cref="ResolutionContext.ChosenMode"/>); falls back to a registered
    /// agent at resolve time, then to the captured <paramref name="defaultMode"/>
    /// (the no-agent dispatcher path). Mirrors <see cref="KnightOfAutumnFactory"/>.
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player controller, int defaultMode, ResolutionContext ctx)
    {
        if (ctx.ChosenMode is { } recorded && recorded >= 0 && recorded < Modes.Count)
        {
            return recorded;
        }

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null) return defaultMode;

        try
        {
            var pick = await agent.ChooseModeAsync(
                    ctx.Game!,
                    modes: Modes,
                    modeIntents: ModeIntents)
                .ConfigureAwait(false);

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back to
            // the deterministic default (same pattern as Knight of Autumn).
        }

        return defaultMode;
    }

    // ------------------------------------------------------------------
    // Mode bodies
    // ------------------------------------------------------------------

    /// <summary>
    /// Mode 0 — Put a +1/+1 counter on target creature you control (CR 122).
    /// Honours the agent-set target (slot 0); falls back to the first legal
    /// controller-creature deterministically (no-agent dispatcher posture). The
    /// counter-driven +1/+1 resolves in the <see cref="ContinuousEffectsService"/>
    /// postlude (CR 613.7b). Same body as <see cref="GenerousVisitorFactory"/>.
    /// </summary>
    private static void ExecuteCounter(Player controller, TriggeredAbility etb)
    {
        Creature? target = ChosenAt(etb, slot: ModeCounter) as Creature;

        target ??= controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();

        if (target == null) return;

        // CR 608.2b — illegal-on-resolution check (still a battlefield creature).
        if (target.Zone != ZoneType.Battlefield) return;

        target.Counters.Add(CounterType.PlusOnePlusOne, 1);
    }

    /// <summary>
    /// Mode 1 — This creature fights target creature token (CR 701.12). Honours
    /// the agent-set target (slot 1); falls back to the first legal battlefield
    /// creature token deterministically. CR 608.2b — both Hivespine and the
    /// token must still be battlefield creatures (and the target still a token)
    /// or the fight does nothing. Routes through <see cref="Fx.Fight"/>.
    /// </summary>
    private static void ExecuteFight(Creature self, TriggeredAbility etb, ResolutionContext ctx)
    {
        Creature? token = ChosenAt(etb, slot: ModeFight) as Creature;

        token ??= (ctx.Game?.AllPlayers ?? Array.Empty<Player>())
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken);

        if (token == null) return;

        // CR 608.2b — both fighters must still be battlefield creatures and the
        // target must still be a token, or the fight does nothing.
        if (self.Zone != ZoneType.Battlefield) return;
        if (token.Zone != ZoneType.Battlefield || !token.IsToken) return;

        // CR 701.12a — simultaneous mutual damage (Fx snapshots both powers).
        Fx.Fight(self, token);
    }

    /// <summary>
    /// Mode 2 — Destroy target artifact or enchantment (CR 701.7). Honours the
    /// agent-set target (slot 2); falls back to the first legal battlefield
    /// artifact / enchantment deterministically. Validates the target is still a
    /// legal artifact / enchantment on the battlefield (CR 608.2b) before
    /// destroying. Identical body to <see cref="KnightOfAutumnFactory"/>'s mode 1.
    /// </summary>
    private static void ExecuteDestroy(Player controller, TriggeredAbility etb, ResolutionContext ctx)
    {
        Permanent? picked = ChosenAt(etb, slot: ModeDestroy) as Permanent;

        picked ??= (ctx.Game?.AllPlayers ?? new[] { controller })
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .FirstOrDefault(c => c.HasType(CardType.Artifact)
                              || c.HasType(CardType.Enchantment));

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact)
              || picked.HasType(CardType.Enchantment))) return;

        // CR 701.7 — destroy (indestructible CR 702.12 cancels; a regeneration
        // shield CR 701.15 is consumed).
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }

    /// <summary>
    /// Read the agent-set chosen target at <paramref name="slot"/> from the
    /// trigger (production path), or null when no target was set there.
    /// </summary>
    private static object? ChosenAt(TriggeredAbility etb, int slot)
    {
        if (etb.ChosenTargets.Count > slot
            && etb.ChosenTargets[slot].Count > 0)
        {
            return etb.ChosenTargets[slot][0];
        }

        return null;
    }
}
