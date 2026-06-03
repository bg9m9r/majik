using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scrying Sheets (Coldsnap).
///
/// Snow Land. Oracle text (Scryfall-confirmed 2026-06-02):
///   "{T}: Add {C}.
///    {1}{S}, {T}: Look at the top card of your library. If that card is
///    snow, you may reveal it and put it into your hand. ({S} can be paid
///    with one mana from a snow source.)"
///
/// ## Implemented (v1)
/// - <b>Snow Land identity</b> — nonbasic Land with the
///   <see cref="CardSupertype.Snow"/> supertype (CR 205.4 / 205.4a). No
///   subtype (Scrying Sheets is not a basic land type).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
///   {C} colourless mana is tracked as generic in the pool, same as the
///   Snow-Covered Wastes mana ability.
/// - <b>{1}{S}, {T}: snow-gated look-at-top + reveal</b> — modelled as an
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{1}{S}"), AdditionalCost.Tap(self)]</c>. Resolution
///   (<see cref="LookAndReveal"/>):
///     1. Look at the top card of the controller's library (CR 701.20 —
///        "look at"; the card stays where it is, visible only to the
///        controller).
///     2. If that card has the <see cref="CardSupertype.Snow"/> supertype
///        (CR 205.4a — "snow" matches any card with the Snow supertype),
///        the controller <em>may</em> reveal it (CR 701.16) and put it into
///        their hand. This is pure card advantage (the new bit this factory
///        pays down: the snow-supertype filter on the inspected card), so
///        the optional "may" auto-accepts in the deterministic resolve path
///        — same upside-"may" posture as Castle Vantress / Castle Locthwain
///        et al.
///     3. A non-snow top card stays on top of the library untouched; an
///        empty library is a clean no-op.
///   When an <see cref="IEventBus"/> is supplied a
///   <see cref="CardRevealedEvent"/> (From = <see cref="ZoneType.Library"/>)
///   is published on reveal so clients can flash the revealed card.
///
/// ## Deferred (v1 gaps — shared engine-wide)
/// - <b>Snow-source {S} payment gating</b>: deferred engine-wide — {S}
///   parses as +1 generic in <see cref="ManaCost.Parse"/> (CR 107.4g), so
///   the activation cost is payable from any mana for now. Same posture as
///   Frostwalk Bastion / Defile and every other {S} card. This deferral is
///   about the <em>cost</em> symbol, independent of the snow filter on the
///   <em>revealed</em> card, which is fully implemented here.
/// </summary>
[CardName("Scrying Sheets")]
public static class ScryingSheetsFactory
{
    public const string CardName = "Scrying Sheets";
    public const string ActivationManaCost = "{1}{S}";

    /// <summary>
    /// Construct Scrying Sheets without an <see cref="IEventBus"/> wired.
    /// The reveal still moves the snow card to hand; only the
    /// <see cref="CardRevealedEvent"/> publish is skipped. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Scrying Sheets.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a
    /// <see cref="CardRevealedEvent"/> is published when a snow top card is
    /// revealed. May be null.</param>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Snow nonbasic Land — Snow supertype, no land subtype (Scrying
        // Sheets is not a basic land type; its mana ability produces {C}).
        var land = new Land(CardName, supertypes: new[] { CardSupertype.Snow }, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C} — vanilla mana ability (CR 605.1). {C} colourless
        // mana is tracked as generic in the pool.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {1}{S}, {T}: Look at the top card of your library. If that card
        // is snow, you may reveal it and put it into your hand.
        //
        // CR 602 — ordinary activated ability. Cost = {1}{S} mana + tap
        // self. The effect lambda captures `land` so live controller
        // tracking (land.Controller) picks up control-change effects at
        // resolution time.
        // ----------------------------------------------------------------
        var lookAndRevealEffect = new Effect(
            $"{CardName}: look at top card; if snow, may reveal + put into hand",
            () => LookAndReveal(land.Controller ?? owner, eventBus));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { lookAndRevealEffect }));

        return land;
    }

    /// <summary>
    /// Resolve the snow-gated look-at-top + reveal:
    ///   1. Look at the top card of <paramref name="controller"/>'s library
    ///      (CR 701.20).
    ///   2. If it has the <see cref="CardSupertype.Snow"/> supertype
    ///      (CR 205.4a), reveal it (CR 701.16) and put it into hand. This is
    ///      pure card advantage, so the optional "may" auto-accepts.
    ///   3. Non-snow top card → stays on top of library; empty library →
    ///      clean no-op.
    /// </summary>
    /// <param name="controller">The player resolving the ability — whose
    /// library is inspected and whose hand receives the revealed card.</param>
    /// <param name="eventBus">When supplied, a <see cref="CardRevealedEvent"/>
    /// (From = <see cref="ZoneType.Library"/>) is published on reveal.</param>
    public static void LookAndReveal(Player controller, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 701.20 — look at the top card. The card is not moved; the
        // controller simply inspects it. FirstOrDefault on the library is
        // the top card (Library.AddCard inserts at position 0).
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — clean no-op.

        // CR 205.4a — "snow" matches any card with the Snow supertype.
        // Non-snow top card → it stays on top, no reveal, no move.
        if (!top.HasSupertype(CardSupertype.Snow)) return;

        // CR 701.16 — reveal the snow card (controller "may"; upside, so we
        // accept). Publish CardRevealedEvent so clients can flash it.
        eventBus?.Publish(new CardRevealedEvent(
            top, controller, ZoneType.Library, reason: CardName));

        // Put it into hand: remove from library, add to hand, fix zone.
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
