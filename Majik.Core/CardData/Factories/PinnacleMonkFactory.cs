using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pinnacle Monk (Tarkir: Dragonstorm, {3}{R}{R}).
///
/// Creature — Djinn Monk 2/2. Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When this creature enters, return target instant or sorcery card from
///    your graveyard to your hand."
///
/// ## Implementation
///
/// - 2/2 Djinn Monk with mana cost {3}{R}{R}, mana value 5.
/// - <b>Prowess (CR 702.108)</b>: KeywordAbility marker for shape-only
///   inspection (dispatcher tests, bot keyword scans). Prowess trigger wired
///   via <see cref="ProwessFactory.Build"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied. Mirrors
///   <see cref="MonasterySwiftspearFactory"/>'s Prowess wiring.
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>:
///   "When this creature enters, return target instant or sorcery card from
///    your graveyard to your hand." Fires on self-ETB. Declares a 1..1
///   <see cref="TargetRequest"/> for an instant or sorcery card in the
///   controller's graveyard; on resolution, moves the chosen card to the
///   controller's hand via <see cref="Fx.ReturnFromGraveyardToHand"/>.
///   CR 603.10b — if the target is no longer a legal instant/sorcery in the
///   graveyard at resolution, the effect no-ops.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Prowess keyword marker
///   attached; ETB trigger attached for shape inspection. Neither trigger is
///   registered with a <see cref="TriggerManager"/>; Prowess mechanic NOT
///   wired (no effects service). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied; ETB trigger registered when <paramref name="triggers"/> is
///   supplied.
///
/// ## Rules citations
/// - CR 702.108 — Prowess.
/// - CR 603.1 / CR 603.6a — Triggered ability; battlefield-active.
/// - CR 603.10b — Illegal-on-resolution: remove trigger with no effect.
/// - CR 701.11 — Return to hand.
/// </summary>
[CardName("Pinnacle Monk")]
public static class PinnacleMonkFactory
{
    public const string CardName = "Pinnacle Monk";
    public const string PrintedManaCost = "{3}{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Pinnacle Monk with no live wiring. Prowess keyword marker
    /// is attached; ETB trigger is attached for shape inspection. Suitable
    /// for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Pinnacle Monk with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly; reserved for future
    /// lifecycle subscribers.</param>
    /// <param name="triggers">TriggerManager for both the Prowess trigger
    /// and the ETB graveyard-recur trigger. May be null — triggers are still
    /// attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// effect (CR 613.1f, Layer 7c). May be null — Prowess trigger is not
    /// wired when null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Djinn, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Prowess (CR 702.108). KeywordAbility marker for shape-only
        // inspection (dispatcher tests, bot keyword scans). Independent of
        // the actual trigger wiring below — same posture as Monastery
        // Swiftspear's keyword marker alongside the combat-validator-driven
        // mechanics.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        // Prowess mechanic — whenever you cast a noncreature spell,
        // Pinnacle Monk gets +1/+1 until end of turn. Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is supplied.
        // card.ActiveEffects is set so that card.Power / card.Toughness
        // reads flow through the layers compute (CR 613 — Layer 7c applies
        // ProwessPumpEffect).
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, return target instant or sorcery
        //    card from your graveyard to your hand."
        // Self-ETB via EventTriggerCondition<CardMovedEvent> (same condition
        // shape as SnapcasterMageFactory). Declares a 1..1 TargetRequest;
        // at resolution, CR 603.10b applied: the target must still be an
        // instant or sorcery in the controller's graveyard.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: return target instant or sorcery card from your graveyard to your hand (ETB)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Card target) return;

                // CR 603.10b — illegal-on-resolution check. The target must
                // still be (a) in the controller's graveyard and (b) an
                // instant or sorcery card.
                if (target.Zone != ZoneType.Graveyard) return;
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(target.Owner, controller)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

                // CR 701.11 — return to hand.
                Fx.ReturnFromGraveyardToHand(target);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
