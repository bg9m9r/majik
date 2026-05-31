using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Summoner's Pact (Future Sight, {0}).
///
/// Instant. Oracle text:
///   "Search your library for a green creature card, reveal it, and put it
///    into your hand. Then shuffle.
///    At the beginning of your next upkeep, pay {2}{G}{G}. If you don't,
///    you lose the game."
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {0}, owner / controller.
/// - <b>Search → reveal → hand → shuffle</b>: <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> whose resolve-time effect
///   filters the controller's library to green creature cards (CR 105.2a
///   colour from cost pips via <see cref="CardColors.GetColors"/>;
///   CR 701.19a search), prompts the registered
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the pick
///   (deterministic first-match fallback when no agent is registered),
///   moves the chosen card to the controller's hand, and shuffles the
///   library via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a). Empty candidates or a null agent pick = no-op
///   (CR 701.19a — search permits declining to find).
///   <para>
///   "Reveal" is modelled by the search itself being public; the engine
///   doesn't yet have a dedicated reveal-event surface, mirroring
///   <see cref="AncientStirringsFactory"/>'s reveal handling.
///   </para>
/// - <b>Delayed upkeep pact</b>: mirrors
///   <see cref="PactOfNegationFactory"/> / <see cref="SlaughterPactFactory"/>.
///   When a <see cref="TriggerManager"/> is supplied the resolve effect
///   registers a <see cref="DelayedTriggeredAbility"/> (CR 603.7) that
///   fires on the controller's next <see cref="PhaseStateType.Upkeep"/>
///   <see cref="StepStartedEvent"/>. The trigger calls
///   <see cref="Player.PayMana"/> with {2}{G}{G} against the controller's
///   mana pool; on failure <see cref="Player.MarkLost"/> flags the
///   controller (CR 104.3 / CR 118.3 — "if you don't, you lose the game").
///
/// ## Deferred (v1 gaps)
///
/// - <b>Cost-payment prompt</b>: production callers pre-stage the
///   controller's mana pool to model "yes, I pay". The v1 trigger reads
///   whatever mana is already in the pool — no in-trigger tap-lands
///   prompt. Same gap as the rest of the Pact cycle.
/// - <b>Live zone move</b>: the Library → Hand move is direct zone
///   mutation. No <c>ZoneService</c> threading because no public ETB
///   triggers fire on cards moving to hand (CR 603.6 trigger sources are
///   the relevant zones). Hand-watchers (e.g. <c>CardDrawnEvent</c>
///   subscribers) won't see this — same posture as
///   <see cref="EladamrisCallFactory"/> hand-tutors.
/// </summary>
[CardName("Summoner's Pact")]
public static class SummonersPactFactory
{
    public const string CardName = "Summoner's Pact";
    public const string PrintedManaCost = "{0}";
    public const string DelayedUpkeepCost = "{2}{G}{G}";

    /// <summary>
    /// Construct the Summoner's Pact card shape (Instant, {0}). Resolve
    /// behaviour is built on demand via <see cref="BuildDefinition"/> so
    /// the dispatcher path can produce a shape-only card.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. On resolution
    /// tutors a green creature card from the controller's library into
    /// hand and shuffles, then (when a <see cref="TriggerManager"/> is
    /// supplied) registers a one-shot delayed trigger that fires at the
    /// caster's next upkeep for {2}{G}{G} or loss.
    /// </summary>
    /// <param name="caster">The controller of Summoner's Pact — owner of
    /// the library searched, hand the picked card lands in, and
    /// controller of the delayed upkeep trigger.</param>
    /// <param name="triggers">Optional trigger manager. When null the
    /// delayed upkeep payment / loss is skipped (suitable for
    /// tutor-only shape tests).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[]
            {
                new Effect("Summoner's Pact: tutor green creature → hand + queue delayed upkeep pact", async ctx =>
                {
                    await TutorGreenCreatureToHandAsync(caster, ctx).ConfigureAwait(false);
                    // CR 701.20a — shuffle after the search effect.
                    LibraryShuffle.ShuffleLibrary(caster, "summoners-pact");
                    RegisterDelayedUpkeepPact(caster, triggers);
                }),
            });
    }

    // --- Tutor: pick a green creature to hand (CR 701.19a) ---------------
    private static async ValueTask TutorGreenCreatureToHandAsync(Player caster, ResolutionContext ctx)
    {
        // CR 105.2a — colour derived from cost pips via CardColors.GetColors.
        var candidates = caster.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Creature) &&
                        CardColors.GetColors(c).Contains(ManaColor.Green))
            .ToList();
        if (candidates.Count == 0) return;

        var pick = await PickGreenCreatureAsync(caster, candidates, ctx).ConfigureAwait(false);
        if (pick == null) return;

        // Reveal + move to hand. Direct zone mutation (no public
        // hand-arrival surface yet) — same posture as other hand-tutors.
        caster.Zones.Library.RemoveCard(pick);
        caster.Zones.Hand.AddCard(pick);
        pick.SetZone(ZoneType.Hand);
        pick.SetController(caster);
    }

    private static async ValueTask<ICard?> PickGreenCreatureAsync(Player caster, List<ICard> candidates, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
        if (agent == null) return candidates[0];
        return await agent.ChooseLibraryPickAsync(
            ctx.Game,
            candidates: candidates,
            kindLabel: "green creature card")
            .ConfigureAwait(false);
    }

    // --- Delayed upkeep pact (CR 603.7 / 104.3 / 118.3) ------------------
    private static void RegisterDelayedUpkeepPact(Player caster, TriggerManager? triggers)
    {
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var pactCost = ManaCost.Parse(DelayedUpkeepCost);

        var payOrLoseEffect = new Effect(
            $"Summoner's Pact: pay {DelayedUpkeepCost} at upkeep or lose the game",
            () =>
            {
                if (caster.HasLost) return;
                if (!caster.PayMana(pactCost)) caster.MarkLost();
            });

        var delayed = new DelayedTriggeredAbility(
            source: caster,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.Upkeep
                          && ReferenceEquals(e.Player, caster)
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { payOrLoseEffect });

        triggers.RegisterDelayed(delayed);
    }
}
