using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quantum Riddler (Edge of Eternities, {3}{U}{U}).
///
/// Creature — Sphinx 4/6. Oracle text (verified Scryfall 2026-05-24):
///   "Flying
///    When this creature enters, draw a card.
///    As long as you have one or fewer cards in hand, if you would draw
///    one or more cards, you draw that many cards plus one instead.
///    Warp {1}{U}"
///
/// ## Implemented (v1)
/// - 4/6 Sphinx Creature with mana cost {3}{U}{U}.
/// - <b>Flying (CR 702.9)</b> as a <see cref="KeywordAbility"/> marker
///   (same posture as Abhorrent Oculus / Atraxa / Sprite Dragon — combat
///   pipeline consumes the keyword marker).
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature
///   enters, draw a card." Resolution calls <see cref="Fx.DrawCards"/>(1)
///   on the controller, which moves the top of the library to hand and
///   stamps <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> on
///   empty-library (CR 120.3 / 704.5b). Same shape as Silvergill Adept's
///   ETB draw.
/// - <b>Warp keyword marker</b> (CR 702.??? — Edge of Eternities) as a
///   <see cref="KeywordAbility"/>. Mechanic deferred — same posture as
///   <see cref="PinnacleEmissaryFactory"/>.
/// - <b>Conditional additional-draw replacement (CR 614.12)</b>: "As long
///   as you have one or fewer cards in hand, if you would draw one or more
///   cards, you draw that many cards plus one instead." Wired via
///   <see cref="QuantumRiddlerDrawCountReplacement"/> — an
///   <see cref="IReplacementEffect{DrawCountIntent}"/> registered on the
///   controller's own <see cref="ReplacementBus"/> while Quantum Riddler
///   is on the battlefield. The replacement rides the quantity tier of the
///   draw bus (<see cref="DrawCountIntent"/>, published once per draw
///   instruction by <see cref="Fx.DrawCards"/>): when the controller's hand
///   holds one or fewer cards it returns
///   <c>intent with { Count = intent.Count + 1 }</c>; otherwise it leaves
///   the count unchanged. Lifecycle (ETB register / LTB unregister) is
///   driven by <see cref="QuantumRiddlerDrawReplacementEffect"/> off the
///   supplied <see cref="IEventBus"/>, mirroring Narset / Spirit of the
///   Labyrinth's per-draw restriction lifecycle.
///
/// ## Deferred (v1 gaps)
/// - <b>Warp alt-cost (CR 702.??? — new Edge of Eternities keyword)</b>:
///   deferred infra. See <see cref="PinnacleEmissaryFactory"/>'s xmldoc
///   for the full description of the missing primitive (Warp {cost} +
///   exile-at-next-end-step + cast-from-exile-later, parallels
///   Suspend → Plot → Warp evolution). v1 ships Quantum Riddler at its
///   printed {3}{U}{U} cast cost with a "Warp" keyword marker for
///   card-text inspection.
/// </summary>
[CardName("Quantum Riddler")]
public static class QuantumRiddlerFactory
{
    public const string CardName = "Quantum Riddler";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 6;

    /// <summary>
    /// Construct Quantum Riddler with no live bus / trigger-manager
    /// wiring. The ETB trigger is attached to the card shape so
    /// dispatcher / structural tests can observe it; live firing
    /// requires the (owner, eventBus, triggers) overload. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Quantum Riddler. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a
    /// <see cref="CardMovedEvent"/> to the battlefield automatically
    /// places it on the stack (CR 603.3); otherwise the trigger is
    /// attached structurally but not registered for firing.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
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
        // CR 702.9 — Flying. KeywordAbility marker consumed by the combat
        // block-validation pipeline (mirrors Abhorrent Oculus / Sprite
        // Dragon / Atraxa).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Warp keyword marker (CR 702.??? — Edge of Eternities). The
        // mechanic (alt-cost + exile-at-end-step + cast-from-exile-later)
        // is deferred; the marker surfaces the keyword for card-text
        // inspection — same posture as PinnacleEmissaryFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Warp", card, owner));

        // ----------------------------------------------------------------
        // CR 614.12 — "As long as you have one or fewer cards in hand, if
        // you would draw one or more cards, you draw that many cards plus
        // one instead."
        //
        // Wired as a real IReplacementEffect<DrawCountIntent> on the
        // controller's OWN ReplacementBus while Quantum Riddler is on the
        // battlefield. The quantity tier of the draw bus
        // (Fx.DrawCards publishes one DrawCountIntent per draw instruction)
        // lets the replacement bump the requested count by +1 whenever the
        // controller's hand holds <= 1 card. ETB register / LTB unregister
        // is driven by the lifecycle effect off the supplied event bus —
        // mirrors Narset / Spirit of the Labyrinth's per-draw restriction.
        //
        // A StaticAbility marker is also attached for card-text inspection
        // (so structural / dispatcher tests still observe the printed
        // clause); it carries no applyEffect — the real behaviour rides the
        // replacement.
        // ----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description:
                "As long as you have one or fewer cards in hand, if you would draw "
                + "one or more cards, you draw that many cards plus one instead.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield
                                  && (card.Controller?.Zones.Hand.GetCards().Count() ?? int.MaxValue) <= 1,
            applyEffect: null));

        if (eventBus != null)
        {
            var drawLifecycle = new QuantumRiddlerDrawReplacementEffect(card, eventBus);
            drawLifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        //
        // Resolution: controller draws one card via Fx.DrawCards (top of
        // library → hand; empty-library stamps the SBA loss marker per
        // CR 120.3 / 704.5b). Same shape as Silvergill Adept's ETB.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}

/// <summary>
/// Lifecycle binder for Quantum Riddler's conditional additional-draw
/// replacement (CR 614.12) — "As long as you have one or fewer cards in
/// hand, if you would draw one or more cards, you draw that many cards plus
/// one instead."
///
/// While Quantum Riddler is on the battlefield, registers a
/// <see cref="QuantumRiddlerDrawCountReplacement"/> on the controller's own
/// <see cref="ReplacementBus"/>. The replacement rides the quantity tier of
/// the draw bus (<see cref="DrawCountIntent"/>) and bumps the requested
/// draw count by +1 whenever the controller's hand holds one or fewer
/// cards. ETB register / LTB unregister is driven by
/// <see cref="CardMovedEvent"/> on the supplied event bus — mirrors
/// <c>NarsetDrawRestrictionEffect</c> / <c>SpiritDrawRestrictionEffect</c>.
/// </summary>
public sealed class QuantumRiddlerDrawReplacementEffect
{
    private readonly ICard _source;
    private readonly IEventBus _eventBus;
    private QuantumRiddlerDrawCountReplacement? _registered;
    private bool _attached;
    private bool _currentlyActive;

    public QuantumRiddlerDrawReplacementEffect(ICard source, IEventBus eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>Subscribe to zone-change events and sync the registration
    /// against the controller's bus. Idempotent.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;

        _eventBus.Subscribe<CardMovedEvent>(OnCardMoved);
        SyncRegistration();
    }

    private void OnCardMoved(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        SyncRegistration();
    }

    private void SyncRegistration()
    {
        var controller = _source.Controller;
        var bus = controller?.Replacements;
        var shouldBeActive = _source.Zone == ZoneType.Battlefield && bus != null;

        if (shouldBeActive && !_currentlyActive)
        {
            _registered = new QuantumRiddlerDrawCountReplacement(controller!, _source);
            bus!.Register(_registered);
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            if (_registered != null)
            {
                controller?.Replacements?.Unregister(_registered);
            }
            _registered = null;
            _currentlyActive = false;
        }
    }

    /// <summary>True while the replacement is registered.</summary>
    public bool IsActive => _currentlyActive;
}

/// <summary>
/// Replacement effect for Quantum Riddler's "draw that many cards plus one
/// instead" (CR 614.12). Rides the quantity tier of the draw bus
/// (<see cref="DrawCountIntent"/>). When the controller's hand holds one or
/// fewer cards AND Quantum Riddler is on the battlefield, returns
/// <c>intent with { Count = intent.Count + 1 }</c>; otherwise the intent is
/// let through unchanged. Self-replacement, so it fires at most once per
/// draw instruction (CR 616.1c — the bus's per-intent dedup guarantees
/// this, avoiding an unbounded +1 cascade).
/// </summary>
public sealed class QuantumRiddlerDrawCountReplacement : IReplacementEffect<DrawCountIntent>
{
    private readonly Player _controller;
    private readonly ICard _source;

    public QuantumRiddlerDrawCountReplacement(Player controller, ICard source)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => null;

    public bool Applies(DrawCountIntent intent, IReadOnlyList<object> history)
    {
        if (intent is null) return false;
        if (!ReferenceEquals(intent.Player, _controller)) return false;
        if (_source.Zone != ZoneType.Battlefield) return false;
        // "if you would draw one or more cards" — only when a positive draw
        // is requested.
        if (intent.Count < 1) return false;
        // "As long as you have one or fewer cards in hand" (CR 614.12).
        return _controller.Zones.Hand.GetCards().Count() <= 1;
    }

    public DrawCountIntent? Replace(DrawCountIntent intent, IReadOnlyList<object> history)
        => intent with { Count = intent.Count + 1 };
}
