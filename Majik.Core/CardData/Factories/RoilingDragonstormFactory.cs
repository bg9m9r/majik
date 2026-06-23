using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roiling Dragonstorm (Tarkir: Dragonstorm, {1}{U}).
///
/// Enchantment. Oracle text (verified against the embedded Modern seed, which
/// is sourced from Scryfall):
///   "When this enchantment enters, draw two cards, then discard a card.
///    When a Dragon you control enters, return this enchantment to its
///    owner's hand."
///
/// ## Shape source
/// Card identity (name, {1}{U}, Enchantment, blue) is loaded from
/// <c>Majik.Core/CardData/Cards/roiling-dragonstorm.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two printed triggers are layered
/// on in code — the JSON <c>AbilityDefinition</c> schema expresses neither the
/// draw-then-discard "loot" effect nor the Dragon-ETB self-bounce, so they
/// live in the factory (same posture as <see cref="OverlordOfTheFloodpitsFactory"/>
/// for the loot body and <see cref="KorSkyfisherFactory"/> for the self-return).
///
/// ## Implemented (v1)
/// - <b>ETB loot trigger (CR 603.1)</b>: "When this enchantment enters, draw
///   two cards, then discard a card." Gated on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; on resolution draws two
///   cards (CR 120) then discards one (CR 701.16) via <see cref="Fx.DrawCards"/>
///   + <see cref="Fx.Discard"/>. Discard is the engine's deterministic
///   first-card-in-hand pick (the agent-driven which-card-to-discard choice is
///   the same v1 gap as Faithless Looting / Overlord of the Floodpits).
/// - <b>Dragon-ETB self-bounce trigger (CR 603.1)</b>: "When a Dragon you
///   control enters, return this enchantment to its owner's hand." Fires on a
///   <see cref="CardMovedEvent"/> into <see cref="ZoneType.Battlefield"/> when
///   the entering card (a) has the <see cref="CardSubtype.Dragon"/> subtype and
///   (b) is controlled by this enchantment's controller — read dynamically off
///   <see cref="Card.Controller"/> so a control-change of the enchantment
///   redirects "you control" (CR 109.5). On resolution the enchantment itself
///   is returned to its owner's hand (CR 701.10). CR 608.2b — if the
///   enchantment is no longer on the battlefield at resolution, the ability
///   does nothing. This is not a targeted ability; the entering Dragon
///   (including this enchantment's own controller's Dragons, and the
///   "you control" includes a Dragon that enters the same time another Dragon
///   does) is the trigger event, not a target.
///
/// ## Deferred (v1 gaps)
/// - None for the gameplay payload. The which-card-to-discard agent choice is
///   the engine-wide deterministic-pick gap, not specific to this card.
/// </summary>
[CardName("Roiling Dragonstorm")]
public static class RoilingDragonstormFactory
{
    public const string CardName = "Roiling Dragonstorm";
    public const string Slug = "roiling-dragonstorm";

    /// <summary>Cards drawn by the ETB loot trigger.</summary>
    public const int DrawCount = 2;

    /// <summary>Cards discarded by the ETB loot trigger.</summary>
    public const int DiscardCount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Roiling Dragonstorm with both triggers attached for shape
    /// inspection. No ZoneService wiring — the self-bounce uses a raw zone
    /// move. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Roiling Dragonstorm.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">When supplied, the self-bounce is routed
    /// through <see cref="ZoneService.MoveCard"/> so the replacement bus fires
    /// and a <see cref="CardMovedEvent"/> is published. May be null — a raw
    /// zone move is used as fallback.</param>
    /// <param name="eventBus">Event bus for downstream consumers. May be null.</param>
    /// <param name="triggers">When supplied, both triggers are registered so the
    /// matching <see cref="CardMovedEvent"/>s land their abilities on the stack
    /// automatically.</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {1}{U}). The JSON carries no abilities — both triggers are layered
        // on below.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB loot trigger — "When this enchantment enters, draw two cards,
        // then discard a card." (CR 603.1 / CR 120 / CR 701.16)
        // The draw-for / discard-for player is read dynamically off the
        // enchantment's controller (CR 603.3c).
        // ----------------------------------------------------------------
        var lootEffect = new Effect(
            $"{CardName}: enters — draw {DrawCount}, discard {DiscardCount}",
            _ =>
            {
                var lootFor = card.Controller ?? owner;
                Fx.DrawCards(lootFor, DrawCount);
                Fx.Discard(lootFor, DiscardCount);
                return default;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Dragon-ETB self-bounce trigger — "When a Dragon you control enters,
        // return this enchantment to its owner's hand." (CR 603.1 / 701.10)
        //
        // Condition: a card entering the battlefield that (a) is a Dragon
        // (CardSubtype.Dragon) and (b) is controlled by THIS enchantment's
        // controller ("you control", read dynamically — CR 109.5). This
        // enchantment is not itself a Dragon, so it never self-triggers on its
        // own ETB.
        // ----------------------------------------------------------------
        var bounceEffect = new Effect(
            $"{CardName}: a Dragon you control entered — return this enchantment to its owner's hand",
            () =>
            {
                // CR 608.2b — if the enchantment has already left the
                // battlefield at resolution, do nothing.
                if (card.Zone != ZoneType.Battlefield) return;

                var cardOwner = card.Owner ?? owner;

                // CR 701.10 — return to owner's hand.
                if (zoneService != null)
                {
                    // Full path: replacement bus fires, CardMovedEvent published.
                    zoneService.MoveCard(card, ZoneType.Battlefield, ZoneType.Hand);
                }
                else
                {
                    // Raw fallback: direct zone manipulation (shape tests /
                    // dispatcher path with no ZoneService).
                    var fromController = card.Controller ?? cardOwner;
                    fromController.Zones.Battlefield.RemoveCard(card);
                    cardOwner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                    card.SetController(cardOwner);
                }
            });

        var dragonTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasSubtype(CardSubtype.Dragon)
                && ReferenceEquals(e.Card.Controller, card.Controller ?? owner)),
            effects: new IEffect[] { bounceEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(dragonTrigger);
        triggers?.RegisterTriggeredAbility(dragonTrigger);

        return card;
    }
}
