using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Priest of Fell Rites (Modern Horizons 3, {W}{B}).
///
/// Creature — Human Warlock 2/2. Oracle text (verified against Scryfall +
/// the embedded seed, current printing):
///   "{T}, Pay 3 life, Sacrifice this creature: Return target creature card
///    from your graveyard to the battlefield. Activate only as a sorcery.
///    Unearth {3}{W}{B} ({3}{W}{B}: Return this card from your graveyard to
///    the battlefield. It gains haste. Exile it at the beginning of the next
///    end step or if it would leave the battlefield. Unearth only as a
///    sorcery.)"
///
/// ## Implemented (v1)
/// - <b>Creature — Human Warlock {W}{B} 2/2</b>, owner/controller wired.
/// - <b>"{T}, Pay 3 life, Sacrifice this creature: Return target creature card
///   from your graveyard to the battlefield. Activate only as a sorcery."</b> —
///   a battlefield-zone <see cref="ActivatedAbility"/> whose costs are
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.PayLife"/>(3)
///   + <see cref="AdditionalCost.Sacrifice"/>(self) (CR 118 / 701.16 / 119.3).
///   The reanimation target is a creature CARD in the controller's graveyard
///   (CR 110.4); chosen via a <see cref="TargetRequest"/> + CandidateGatherer
///   and read at resolution from <see cref="ResolutionContext.ChosenTargets"/>
///   (CR 601.2c), re-validated per CR 608.2b. Sorcery-speed (CR 117.1a / 307.5).
///   <para>
///   <b>RE-SOURCE-SAFE (agatha-resolutioncontext-source migration; closes the
///   priest-of-fell-rites-exile-from-gy-reanimate-rebind deferral).</b> The
///   reanimation effect reads its source / controller off the live
///   <see cref="ResolutionContext.Source"/> (<c>(ctx.Source as Permanent) ??
///   card</c>) rather than capturing the authoring <c>card</c>, and the ability
///   is marked <see cref="ActivatedAbility.RebindSafe"/> = true. Its three costs
///   are all source-capturing <see cref="AdditionalCost"/>s (Tap + Sacrifice
///   carry the permanent; PayLife carries no source), so
///   <see cref="ActivatedAbility.RebindTo"/> Stage-1 re-homes the Tap /
///   Sacrifice to the BEARER via <see cref="AdditionalCost.RebindSource"/>.
///   Agatha's Soul Cauldron therefore re-homes the REAL ability to a
///   counter-bearing creature (CR 707.2 / 613.1f / 702.49): the bearer taps +
///   pays 3 life + sacrifices ITSELF, and reanimates from the bearer's
///   controller's graveyard. NOTE — the current printing's cost is a plain
///   self-sacrifice (an <see cref="AdditionalCost"/> the Stage-1 seam already
///   covers); the prior printing's "Exile Priest of Fell Rites from your
///   graveyard" (a zone-and-name-bound graveyard self-exile cost that needed a
///   bespoke rebind seam) is no longer on the card, so the deferral resolves
///   onto the existing AdditionalCost rebind path with no new cost primitive.
///   </para>
/// - <b>Unearth {3}{W}{B} (CR 702.84)</b> — a graveyard-activated, sorcery-speed
///   <see cref="ActivatedAbility"/> with a {3}{W}{B} <see cref="ManaCostCost"/>
///   that returns this card from the graveyard to the battlefield, grants Haste
///   (CR 702.10), and (when a <see cref="TriggerManager"/> is supplied)
///   registers a one-shot delayed end-step EXILE (CR 702.84c). Same shape as
///   <see cref="ScrapworkMuttFactory"/> / Hellspark Elemental. NOT marked
///   RebindSafe — Unearth is intrinsically a graveyard ability of THIS card and
///   is not a grant target (an imprinted card's Unearth is meaningless on a
///   battlefield bearer).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent target / "Pay 3 life or not" prompt</b>: the battlefield ability
///   declares a real <see cref="TargetRequest"/> the live trigger/activation
///   path fills; factory-direct shape tests set <see cref="ResolutionContext"/>
///   targets explicitly.
/// - <b>Zone-scoped activated abilities</b>: Unearth is enumerable while the
///   card is in any zone; the resolution body guards on the graveyard zone so
///   spurious activations are no-op-shaped (engine zone-scoping still future
///   work).
/// </summary>
[CardName("Priest of Fell Rites")]
public static class PriestOfFellRitesFactory
{
    public const string CardName = "Priest of Fell Rites";
    public const string PrintedManaCost = "{W}{B}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int PayLifeAmount = 3;
    public const string UnearthCost = "{3}{W}{B}";
    public const string Haste = "Haste";

    /// <summary>
    /// Construct Priest of Fell Rites with no live runtime wiring (the shape /
    /// dispatcher path). Both activated abilities are attached; the battlefield
    /// reanimation uses raw zone moves when resolved without a registered
    /// ZoneService, and Unearth registers no delayed exile trigger. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Priest of Fell Rites with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, the graveyard → battlefield
    /// reanimation routes through <see cref="ZoneService.MoveCard"/> so ETB
    /// triggers on the reanimated creature fire (CR 603.6a). When
    /// <paramref name="triggers"/> is supplied, Unearth's delayed end-step exile
    /// is registered (CR 702.84c).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warlock });

        card.SetOwner(owner);
        card.SetController(owner);

        AddReanimationAbility(card, owner);
        AddUnearthAbility(card, owner, zoneService, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // "{T}, Pay 3 life, Sacrifice this creature: Return target creature card
    //  from your graveyard to the battlefield. Activate only as a sorcery."
    //
    // RE-SOURCE-SAFE: the effect reads (ctx.Source as Permanent) ?? card for
    // the live source + its controller, never the captured `card`. The Tap +
    // Sacrifice costs are AdditionalCosts that RebindTo re-homes (Stage 1), so
    // Agatha's Soul Cauldron re-homes the REAL ability to a bearer. Marked
    // rebindSafe: true.
    // -----------------------------------------------------------------------
    private static void AddReanimationAbility(Creature card, Player owner)
    {
        // CR 601.2c — the chosen target is read at resolution from
        // ResolutionContext.ChosenTargets[0][0]; the candidate pool is the
        // controller's graveyard creature CARDS (CR 110.4).
        var targetRequest = new TargetRequest(
            Description: "target creature card from your graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ => GraveyardCreatureCards(card.Controller ?? owner));

        var reanimateEffect = new Effect(
            $"{CardName}: return target creature card from your graveyard to the battlefield",
            ctx =>
            {
                // RE-SOURCE-SAFE — the live source (the bearer after a RebindTo;
                // otherwise this Priest) and its controller drive "your
                // graveyard".
                var controller = ResolveController(ctx, card, owner);

                // CR 608.2b — read the chosen target; re-validate at resolution.
                var pick = ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0
                    ? ctx.ChosenTargets[0][0] as ICard
                    : null;
                if (pick == null) return ValueTask.CompletedTask;

                // Still a creature card in the controller's graveyard.
                if (pick.Zone != ZoneType.Graveyard) return ValueTask.CompletedTask;
                if (!controller.Zones.Graveyard.ContainsCard(pick)) return ValueTask.CompletedTask;
                if (!pick.HasType(CardType.Creature)) return ValueTask.CompletedTask;

                // CR 701.20 — reanimate to the controller's battlefield, routed
                // through ZoneService when registered so ETB triggers fire.
                var zones = ZoneServiceRegistry.Get(controller);
                Fx.ReturnFromGraveyardToBattlefield(pick, controller, zones);
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                AdditionalCost.PayLife(PayLifeAmount),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { reanimateEffect },
            targetRequests: new[] { targetRequest },
            // "Activate only as a sorcery." (CR 117.1a / 307.5)
            sorcerySpeed: true,
            // Agatha's Soul Cauldron re-home soundness — every effect reads the
            // live ResolutionContext.Source; the Tap / Sacrifice costs re-home
            // via AdditionalCost.RebindSource (Stage 1).
            rebindSafe: true));
    }

    /// <summary>
    /// The live source's controller (the bearer's controller after a RebindTo,
    /// else this Priest's controller, else the authoring owner). Drives "your
    /// graveyard" so a re-homed ability reanimates from the BEARER's controller's
    /// graveyard, never the exiled imprinted card's.
    /// </summary>
    private static Player ResolveController(ResolutionContext ctx, Creature card, Player owner) =>
        (ctx.Source as Permanent)?.Controller ?? card.Controller ?? owner;

    /// <summary>
    /// Candidate pool for the reanimation target — creature CARDS in the
    /// controller's graveyard (CR 110.4 — a card in a graveyard, not a permanent).
    /// </summary>
    private static IReadOnlyList<object> GraveyardCreatureCards(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Creature))
            .Cast<object>()
            .ToList();

    // -----------------------------------------------------------------------
    // Unearth {3}{W}{B} — CR 702.84. Graveyard-activated, sorcery-speed.
    // Returns this card from graveyard → battlefield, grants Haste, registers
    // a delayed end-step EXILE. Mirrors ScrapworkMutt / Hellspark Elemental.
    // -----------------------------------------------------------------------
    private static void AddUnearthAbility(
        Creature card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        var unearthEffect = new Effect(
            $"{CardName}: unearth — return from graveyard, gain haste, exile next end step",
            () => ResolveUnearth(card, owner, zoneService, triggers));

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(UnearthCost) },
            effects: new IEffect[] { unearthEffect },
            // CR 702.84a — "Unearth only as a sorcery."
            sorcerySpeed: true));
    }

    /// <summary>
    /// CR 702.84 — resolve the Unearth activation. Returns the card from its
    /// owner's graveyard to the battlefield under the controller's control,
    /// grants Haste (CR 702.10 / 613.1c), clears summoning sickness, and (when
    /// <paramref name="triggers"/> is supplied) registers a one-shot delayed
    /// end-step trigger that EXILES the creature (CR 702.84c). No-ops cleanly
    /// when the card is not in its owner's graveyard.
    /// </summary>
    private static void ResolveUnearth(
        Creature card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        if (card.Zone != ZoneType.Graveyard) return;
        if (card.Owner == null || !ReferenceEquals(card.Owner, owner)) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

        // Graveyard → battlefield (CR 702.84a). ZoneService routes the publish
        // so ETB triggers fire (CR 603.6a).
        if (zoneService != null)
        {
            zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
        }
        else
        {
            owner.Zones.Graveyard.RemoveCard(card);
            owner.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
            card.SetController(owner);
        }

        // "It gains haste." CR 702.84a / CR 613.1c (Layer 6).
        if (card.ActiveEffects != null)
        {
            card.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(card, Haste));
        }
        card.HasSummoningSickness = false;

        // "Exile it at the beginning of the next end step." CR 702.84c / 603.7.
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var exileEffect = new Effect(
            $"{CardName}: unearth — exile at next end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var bfPlayer = card.Controller ?? owner;
                if (!bfPlayer.Zones.Battlefield.GetCards().Contains(card)) return;
                var exileOwner = card.Owner ?? owner;

                if (zoneService != null)
                {
                    zoneService.MoveCard(card, ZoneType.Battlefield, ZoneType.Exile, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(card);
                    exileOwner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            });

        var delayedExile = new DelayedTriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { exileEffect });

        triggers.RegisterDelayed(delayedExile);
    }
}
