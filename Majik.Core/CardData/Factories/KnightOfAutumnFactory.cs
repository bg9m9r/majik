using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight of Autumn (Guilds of Ravnica, {1}{G}{W}).
///
/// Creature — Dryad Knight 2/1. Oracle text:
///   "When this creature enters, choose one —
///    • Put two +1/+1 counters on this creature.
///    • Destroy target artifact or enchantment.
///    • You gain 4 life."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Dryad Knight, mana cost {1}{G}{W}. Color identity
///   green/white (derived from {G}+{W} pips per CR 202.2c). Mana value 3
///   (CR 202.3).
/// - <b>ETB modal triggered ability</b> (CR 700.2d — "Choose one —",
///   CR 603.1 / CR 603.6a): three modes, one chosen at resolve time via
///   <see cref="IPlayerAgent.ChooseModeAsync"/> (same modal-ETB posture as
///   <see cref="CharmingPrinceFactory"/>). A per-card mode index is captured
///   at factory time so tests can supply a deterministic mode without
///   registering a full agent.
///
/// ## Modes
/// - <b>Mode 0 — Put two +1/+1 counters on this creature</b>: adds two
///   <see cref="CounterType.PlusOnePlusOne"/> counters to Knight itself
///   (CR 122 / CR 613.7b — counter-driven P/T resolves in the
///   <see cref="ContinuousEffectsService"/> counter postlude). Same
///   counter-add posture as <see cref="GenerousVisitorFactory"/>.
/// - <b>Mode 1 — Destroy target artifact or enchantment</b>: bespoke 1..1
///   <see cref="TargetRequest"/> restricted to artifacts + enchantments,
///   destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///   cancels per CR 702.12). Identical destroy body to
///   <see cref="ReclamationSageFactory"/>.
/// - <b>Mode 2 — You gain 4 life</b> (CR 119.3): controller calls
///   <c>controller.GainLife(4)</c>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only, no agent, no TriggerManager.
///   Defaults to mode 0 (two +1/+1 counters — no-target, safest default).
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, int, TriggerManager?)"/> — sets the mode
///   index at factory time via a captured closure; supplying a
///   <see cref="TriggerManager"/> additionally registers the ETB for
///   bus-driven firing.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode prompt</b>: the mode is captured at factory
///   time for test convenience. When the engine's "mode choice on stack
///   entry" infrastructure ships (CR 700.2d prompt surface), the captured
///   <c>mode</c> closure becomes the wiring point for the agent call. Same
///   posture as <see cref="CharmingPrinceFactory"/>.
/// - <b>Real agent-driven target prompt (mode 1)</b>: production callers
///   wire <see cref="TriggeredAbility.SetChosenTargets"/> from an agent
///   prompt before triggers resolve; the factory falls back to the first
///   legal target deterministically. Same gap as
///   <see cref="ReclamationSageFactory"/>.
/// </summary>
[CardName("Knight of Autumn")]
public static class KnightOfAutumnFactory
{
    public const string CardName = "Knight of Autumn";
    public const string PrintedManaCost = "{1}{G}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Mode index for "Put two +1/+1 counters on this creature."</summary>
    public const int ModeCounters = 0;
    /// <summary>Mode index for "Destroy target artifact or enchantment."</summary>
    public const int ModeDestroy = 1;
    /// <summary>Mode index for "You gain 4 life."</summary>
    public const int ModeGainLife = 2;

    private const int CounterCount = 2;
    private const int LifeGainAmount = 4;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Put two +1/+1 counters on this creature.",
        "Destroy target artifact or enchantment.",
        "You gain 4 life.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Buff,      // Two +1/+1 counters — grow the body.
        BotIntent.Removal,   // Destroy artifact / enchantment.
        BotIntent.Heal,      // Gain 4 life.
    };

    /// <summary>
    /// Construct Knight of Autumn. The ETB modal trigger is attached for
    /// shape inspection; supplying a <see cref="TriggerManager"/> additionally
    /// registers it on the bus.
    /// </summary>
    /// <remarks>
    /// <paramref name="mode"/> defaults to <see cref="ModeCounters"/> since
    /// modes 0 and 2 need no target — the bare <c>Create(owner)</c> call is
    /// suitable for dispatcher / structural tests. The mode is captured into
    /// the ETB effect closure so tests exercise each arm without a full agent.
    /// </remarks>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=counters, 1=destroy, 2=gain
    /// life). Overridden by a registered <see cref="IPlayerAgent"/> if one is
    /// present in <see cref="AgentRegistry"/> / the resolution context.</param>
    /// <param name="triggers">TriggerManager — required for bus-driven ETB
    /// firing. May be null.</param>
    public static Creature Create(Player owner, int mode = ModeCounters, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dryad, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB modal triggered ability (CR 603.1 / CR 603.6a / CR 700.2d).
        //   "When this creature enters, choose one —
        //    • Put two +1/+1 counters on this creature.
        //    • Destroy target artifact or enchantment.
        //    • You gain 4 life."
        // Mode resolved via the resolution context's agent (when present) or
        // the captured mode parameter. Mode 1 declares a 0..1 target request
        // (MinTargets=0 so modes 0/2 don't gate the ETB when the unchosen
        // mode 1 carries a target slot).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: choose one — two +1/+1 counters; destroy artifact/enchantment; gain 4 life",
            async ctx =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = await PickModeAsync(controller, mode, ctx).ConfigureAwait(false);

                switch (chosenMode)
                {
                    case ModeCounters:
                        ExecuteAddCounters(card);
                        break;

                    case ModeDestroy:
                        ExecuteDestroy(card, owner, etbTrigger);
                        break;

                    case ModeGainLife:
                        ExecuteGainLife(controller);
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
                // Mode 1 target slot. MinTargets=0 so modes 0 and 2 don't
                // require a target (CR 700.2d — only the chosen mode's
                // targeting is relevant).
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
    // Mode helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls back
    /// to the captured <paramref name="defaultMode"/>. Mirrors
    /// <see cref="CharmingPrinceFactory"/>'s mode picker.
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player controller, int defaultMode, ResolutionContext ctx)
    {
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
            // the deterministic default (same pattern as Charming Prince).
        }

        return defaultMode;
    }

    /// <summary>
    /// Mode 0 — Put two +1/+1 counters on Knight of Autumn itself (CR 122).
    /// The counter-driven P/T (+2/+2) is applied in the
    /// <see cref="ContinuousEffectsService"/> counter postlude (CR 613.7b).
    /// </summary>
    private static void ExecuteAddCounters(Creature card)
    {
        card.Counters.Add(CounterType.PlusOnePlusOne, CounterCount);
    }

    /// <summary>
    /// Mode 1 — Destroy target artifact or enchantment (CR 701.7). Honours
    /// the agent-set target via <see cref="TriggeredAbility.ChosenTargets"/>;
    /// falls back to the first legal target deterministically (no-agent
    /// dispatcher posture). Validates the chosen target is still a legal
    /// artifact / enchantment on the battlefield (CR 608.2b) before
    /// destroying. Identical destroy body to
    /// <see cref="ReclamationSageFactory"/>.
    /// </summary>
    private static void ExecuteDestroy(Creature card, Player owner, TriggeredAbility etb)
    {
        Permanent? picked = null;

        // 1) Honour agent-set target (production path).
        if (etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is Permanent chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first legal artifact/enchantment on the
        //    controller's battlefield (no-agent dispatcher posture).
        picked ??= owner.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(c => c.HasType(CardType.Artifact)
                              || c.HasType(CardType.Enchantment));

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact)
              || picked.HasType(CardType.Enchantment))) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; an active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }

    /// <summary>
    /// Mode 2 — You gain 4 life (CR 119.3).
    /// </summary>
    private static void ExecuteGainLife(Player controller)
    {
        controller.GainLife(LifeGainAmount);
    }
}
