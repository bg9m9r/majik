using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yorion, Sky Nomad (Ikoria, {3}{W/U}{W/U}).
///
/// Legendary Creature — Bird Serpent 4/5. Oracle text:
///   "Companion — Your starting deck contains at least twenty cards
///    more than the minimum deck size."
///   "Flying"
///   "When Yorion enters, exile any number of other nonland permanents
///    you own and control. Return those cards to the battlefield at
///    the beginning of the next end step."
///
/// ## Implementation (v1)
///
/// - 4/5 Legendary Bird Serpent at {3}{W/U}{W/U} (hybrid pip cost
///   parsed via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>).
/// - Flying <see cref="KeywordAbility"/> marker (CR 702.9).
/// - <b>ETB triggered ability</b> (CR 603.6a): exiles any number of
///   "other nonland permanents you own and control" and registers a
///   <see cref="DelayedTriggeredAbility"/> that returns each exiled
///   card to the battlefield under the controller's control at the
///   beginning of the next end step (CR 603.7). The "any number" choice
///   is supplied via an injectable <c>pickPermanents</c> callback —
///   defaults to all owner-and-controller-side nonland permanents
///   (mirrors the deterministic auto-pick used by other v1 factories
///   like Atraxa's reveal-and-take). The original Yorion is excluded
///   ("other"). Tokens that get exiled by Yorion cease to exist
///   permanently per CR 111.8 — the delayed trigger checks
///   <c>Card.Zone == ZoneType.Exile</c> before each return, so tokens
///   that already evaporated skip cleanly.
/// - <b>Companion deck-construction</b>: <see cref="CompanionRestriction"/>
///   exposes a singleton <see cref="ICompanionRestriction"/> that
///   <see cref="Majik.Core.Rules.CompanionValidator"/> consumes at
///   deck-registration time. Yorion requires the starting deck to be
///   at least <see cref="DeckValidator.ConstructedMinimum"/> + 20 cards
///   (so 80 in 60-card Constructed). Singleton/4-of restrictions are
///   the format default — not part of Yorion's printed clause.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Any number" agent prompt</b>: CR 700.2 — the controller
///   chooses any number of qualifying permanents at resolution.
///   v1 auto-picks via the injected callback; the engine has no
///   variable-cardinality target prompt surface yet.
/// - <b>Companion runtime cast</b> (cast Yorion from outside the
///   game for {3} more): the sideboard / outside-the-game cast
///   pipeline lives in <see cref="Majik.Core.Game.SpellCastFlow"/>
///   but is wired generically for any companion. Yorion benefits
///   automatically once a host registers him as the companion.
///   Deck-construction validation is the only Yorion-specific half.
/// - <b>Aura / Equipment attachment loss on exile</b>: when an
///   exiled bearer returns, attached auras / equipment fell off
///   (they didn't follow). v1 returns the permanent untyped — the
///   re-attach for the now-on-battlefield cards is correct (auras
///   were sent to graveyards as their attachments left the
///   battlefield, equipment fell off but stays). Matches generic
///   blink semantics already used by Skyclave Apparition.
/// </summary>
[CardName("Yorion, Sky Nomad")]
public static class YorionSkyNomadFactory
{
    public const string CardName = "Yorion, Sky Nomad";
    public const string PrintedManaCost = "{3}{W/U}{W/U}";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>
    /// CR 702.139 — Yorion's companion deck-construction predicate:
    /// "Your starting deck contains at least twenty cards more than
    /// the minimum deck size." Exposed as a static singleton so
    /// deck-registration call sites can validate without instantiating
    /// Yorion. The reference minimum is
    /// <see cref="DeckValidator.ConstructedMinimum"/> (60), so the
    /// effective threshold is 80 cards for Constructed.
    /// </summary>
    public static ICompanionRestriction CompanionRestriction { get; } =
        new YorionCompanionRestriction();

    /// <summary>
    /// Excess-card-count threshold over the minimum deck size (CR 100.2a).
    /// Surfaced as a constant so tests and call sites don't repeat the
    /// magic number.
    /// </summary>
    public const int ExcessCardThreshold = 20;

    /// <summary>
    /// Constructs Yorion with no runtime services. The Flying keyword
    /// + ETB triggered ability are attached to the card shape; neither
    /// the trigger nor the delayed return ability is registered against
    /// a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null, pickPermanents: null);

    /// <summary>
    /// Constructs Yorion with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="eventBus">Bus used for <see cref="StepStartedEvent"/>
    /// to wire the delayed end-step return. Required for the
    /// return-half to fire end-to-end.</param>
    /// <param name="triggers">Manager that registers the ETB trigger
    /// and (at ETB resolution time) the delayed end-step return.
    /// Required for both halves to fire end-to-end.</param>
    /// <param name="zoneService">Used to move exiled / returned cards
    /// so <see cref="CardMovedEvent"/> publishes for downstream
    /// listeners. Null → falls back to direct zone mutation.</param>
    /// <param name="pickPermanents">Selector that, given Yorion's
    /// controller and the current battlefield, returns the set of
    /// non-Yorion nonland permanents to exile. Null → auto-pick every
    /// non-Yorion nonland permanent the controller owns AND controls
    /// (CR 700.2 deterministic v1).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<Player, Creature, IReadOnlyList<Permanent>>? pickPermanents)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            // CR 205.3m — Yorion is printed "Bird Serpent". The Serpent
            // subtype is not yet enumerated in CardSubtype; ship the Bird
            // subtype now and grow the enum in a follow-up that retrofits
            // every Bird Serpent / Sea Serpent / etc.
            subtypes: new[] { CardSubtype.Bird });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Yorion enters, exile any number of other nonland
        //    permanents you own and control. Return those cards to the
        //    battlefield at the beginning of the next end step."
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — exile any number of other nonland permanents you own & control; return at next end step (CR 603.7)",
            () => ResolveEtb(card, owner, eventBus, triggers, zoneService, pickPermanents));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Resolve helper exposed for direct test invocation. Performs the
    /// exile half + registers the delayed end-step return on the
    /// supplied <see cref="TriggerManager"/>.
    /// </summary>
    public static IReadOnlyList<Permanent> ResolveEtb(
        Creature yorion,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<Player, Creature, IReadOnlyList<Permanent>>? pickPermanents)
    {
        ArgumentNullException.ThrowIfNull(yorion);
        ArgumentNullException.ThrowIfNull(owner);

        var picker = pickPermanents ?? DefaultPicker;
        var picks = picker(owner, yorion);
        if (picks == null || picks.Count == 0) return Array.Empty<Permanent>();

        var exiled = new List<Permanent>();
        foreach (var p in picks)
        {
            if (p == null) continue;
            if (ReferenceEquals(p, yorion)) continue;          // "other"
            if (p.HasType(CardType.Land)) continue;            // "nonland"
            if (!ReferenceEquals(p.Owner, owner)) continue;    // "you own"
            if (!ReferenceEquals(p.Controller, owner)) continue; // "you control"
            if (p.Zone != ZoneType.Battlefield) continue;

            if (zoneService != null)
            {
                zoneService.MoveCard(p, ZoneType.Battlefield, ZoneType.Exile);
            }
            else
            {
                owner.Zones.Battlefield.RemoveCard(p);
                owner.Zones.Exile.AddCard(p);
                p.SetZone(ZoneType.Exile);
            }
            exiled.Add(p);
        }

        // ----------------------------------------------------------------
        // Delayed end-step return (CR 603.7). Registered only when both
        // a TriggerManager and event bus are supplied — the delayed
        // trigger needs the bus to observe StepStartedEvent.
        // ----------------------------------------------------------------
        if (exiled.Count > 0 && triggers != null && eventBus != null)
        {
            var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
            var returnEffect = new Effect(
                $"{CardName} — return {exiled.Count} exiled card(s) to the battlefield",
                () =>
                {
                    foreach (var card in exiled)
                    {
                        // CR 111.8 — tokens that left the battlefield
                        // cease to exist; if a token was in the exile
                        // pile it has since been removed by SBAs.
                        if (card.Zone != ZoneType.Exile) continue;

                        if (zoneService != null)
                        {
                            zoneService.MoveCard(card, ZoneType.Exile, ZoneType.Battlefield, owner);
                        }
                        else
                        {
                            owner.Zones.Exile.RemoveCard(card);
                            owner.Zones.Battlefield.AddCard(card);
                            card.SetZone(ZoneType.Battlefield);
                            card.SetController(owner);
                        }
                    }
                });

            var delayed = new DelayedTriggeredAbility(
                source: yorion,
                controller: owner,
                condition: new EventTriggerCondition<StepStartedEvent>(
                    (e, _) => e.StepType == PhaseStateType.End
                              && e.Timestamp > resolvedAt),
                effects: new IEffect[] { returnEffect });

            triggers.RegisterDelayed(delayed);
        }

        return exiled;
    }

    /// <summary>
    /// Default "any number" picker (CR 700.2 v1 deterministic auto-
    /// pick): every non-Yorion nonland permanent on the controller's
    /// battlefield that the controller also owns.
    /// </summary>
    private static IReadOnlyList<Permanent> DefaultPicker(Player owner, Creature yorion) =>
        owner.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p =>
                !ReferenceEquals(p, yorion)
                && !p.HasType(CardType.Land)
                && ReferenceEquals(p.Owner, owner)
                && ReferenceEquals(p.Controller, owner))
            .ToList();
}

/// <summary>
/// CR 702.139 — Yorion's deck-construction predicate: "Your starting
/// deck contains at least twenty cards more than the minimum deck
/// size." Minimum is <see cref="DeckValidator.ConstructedMinimum"/>
/// (60); effective threshold is 80 cards.
/// </summary>
internal sealed class YorionCompanionRestriction : ICompanionRestriction
{
    public string Description =>
        $"Your starting deck contains at least " +
        $"{YorionSkyNomadFactory.ExcessCardThreshold} cards more than the " +
        $"minimum deck size.";

    public bool IsSatisfiedBy(IEnumerable<ICard> startingDeck)
    {
        ArgumentNullException.ThrowIfNull(startingDeck);
        var count = 0;
        foreach (var card in startingDeck)
        {
            if (card == null) continue;
            count++;
        }
        return count >= DeckValidator.ConstructedMinimum
                        + YorionSkyNomadFactory.ExcessCardThreshold;
    }
}
