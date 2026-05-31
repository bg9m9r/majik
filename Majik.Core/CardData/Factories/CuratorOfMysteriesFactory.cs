using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Curator of Mysteries (Amonkhet, {2}{U}{U}).
///
/// Creature — Sphinx 4/4. Oracle text (Scryfall):
///   "Flying
///    Whenever you cycle or discard another card, scry 1.
///    Cycling {U} ({U}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Sphinx {2}{U}{U} 4/4</b>.
/// - <b>Flying</b> (CR 702.9) wired as a <see cref="KeywordAbility"/>
///   marker — consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>.
/// - <b>Cycling {U}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{U}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers.
/// - <b>"Whenever you cycle ... another card, scry 1" trigger</b>
///   (CR 603.1): wired as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> filtered to
///   <c>e.Player == card.Controller</c> AND <c>!ReferenceEquals(e.Card, card)</c>
///   (the "another card" gate — cycling Curator itself does NOT fire its
///   own trigger, matching the printed wording). On resolve invokes
///   <see cref="ScryAction"/> for N=1 — agent-driven partition when an
///   <see cref="IPlayerAgent"/> is registered, default-to-bottom fallback
///   otherwise (same shape as <see cref="PreordainFactory"/>'s scry leg).
///
/// ## Discard surface deferral
///
/// The "or discard a card" half of the printed trigger is NOT wired in
/// v1. The engine has no dedicated <c>DiscardedEvent</c> (only
/// <c>NecropotenceFactory</c> needs the surface today and uses a
/// hand→graveyard <see cref="CardMovedEvent"/> replacement instead). The
/// trigger only fires on cycle events. Cycling alone already covers the
/// Living End / Astral Slide / Lightning Rift cycle-shell payoff
/// pattern this card was printed for; the discard half is a small future
/// wire-up once <c>DiscardedEvent</c> ships.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Cycle-or-discard
///   trigger attached for shape inspection; cycling ability attached
///   with no event bus (shape-only — no CardCycledEvent publication).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. The cycle trigger is registered so CardCycledEvent
///   publications auto-queue it; cycling resolve publishes against the
///   supplied bus.
///
/// CR rule references: 205.3m (Sphinx subtype), 603.1 (triggered
/// ability), 701.20 (Scry 1), 702.9 (Flying), 702.32 (Cycling).
/// </summary>
[CardName("Curator of Mysteries")]
public static class CuratorOfMysteriesFactory
{
    public const string CardName = "Curator of Mysteries";
    public const string PrintedManaCost = "{2}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 4;
    public const string CyclingCost = "{U}";
    public const int ScryAmount = 1;

    /// <summary>
    /// Construct Curator of Mysteries with no live wiring. Cycle trigger
    /// attached for shape inspection; cycling ability attached without
    /// an event bus (shape-only — no CardCycledEvent publication).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Curator of Mysteries. When <paramref name="triggers"/>
    /// is supplied the cycle trigger is registered so
    /// <see cref="CardCycledEvent"/> publications auto-queue it. When
    /// <paramref name="eventBus"/> is supplied the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> on resolve.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Sphinx });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 — Flying. KeywordAbility marker consumed by
        // CombatAbilities.HasFlying.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "Whenever you cycle ... another card, scry 1." (CR 603.1)
        //
        // EventTriggerCondition<CardCycledEvent> gated to:
        //   1. e.Player == card.Controller — "you cycle" (CR 109.5).
        //   2. !ReferenceEquals(e.Card, card) — "another card" gate.
        //      Cycling Curator itself does NOT fire its own trigger.
        // ActiveZones = Battlefield so Curator in hand / library does
        // not fire the trigger (matches printed shape — abilities on
        // creature cards function from the battlefield only per
        // CR 113.6).
        //
        // Discard-half deferred — engine has no DiscardedEvent surface
        // today (see class doc). The cycle leg alone covers the Living
        // End / cycle-shell payoff role this card was printed for.
        // ----------------------------------------------------------------
        var cycleEffect = new Effect(
            $"{CardName}: scry {ScryAmount}",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var peeked = ScryAction.Peek(controller, ScryAmount);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    decision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                }
                else
                {
                    // Pre-agent default: send to bottom (matches
                    // PreordainFactory / LibrarySpellFactory.ScryNSpell).
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(controller, peeked.Count, decision);
            });

        var cycleCondition = new EventTriggerCondition<CardCycledEvent>(
            (e, _) =>
                ReferenceEquals(e.Player, card.Controller ?? owner)
                && !ReferenceEquals(e.Card, card));

        var cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cycleCondition,
            effects: new IEffect[] { cycleEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        // ----------------------------------------------------------------
        // Cycling {U} — CR 702.32.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
