using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enduring Curiosity (Foundations, {2}{U}{U}).
/// Enchantment Creature — Cat Glimmer 4/3. Oracle text (verified against
/// Scryfall):
///   "Flash
///    Whenever a creature you control deals combat damage to a player, draw a card.
///    When Enduring Curiosity dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// The base shape (name, Creature + Enchantment types, Cat + Glimmer subtypes,
/// {2}{U}{U}, 4/3) is materialised from the embedded JSON definition
/// (<c>enduring-curiosity.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the Flash marker, the combat-damage draw trigger, and the dies → return-as-
/// enchantment trigger are layered on here (same JSON-backed-identity +
/// code-attached-behaviour posture as <see cref="GlissaSunslayerFactory"/> and
/// <see cref="OverlordOfTheBalemurkFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Flash (CR 702.8)</b>: a <see cref="KeywordAbility"/> marker (same
///   marker-keyword posture used for the other parsed keywords on JSON-backed
///   creatures, e.g. first strike / deathtouch on
///   <see cref="GlissaSunslayerFactory"/>). The flash-cast-timing path itself
///   reads this marker.
///
/// - <b>"Whenever a creature you control deals combat damage to a player,
///   draw a card." (CR 510, CR 603.1)</b>: a <see cref="TriggeredAbility"/>
///   over <see cref="CombatDamageDealtEvent"/> whose predicate matches when
///   the damaging creature's controller is THIS card's controller and the
///   damage was dealt to a player (<see cref="CombatDamageDealtEvent.TargetPlayer"/>
///   non-null). Unlike <see cref="GlissaSunslayerFactory"/> (which keys off
///   <c>e.Source == self</c>), this keys off <c>e.Source.Controller ==
///   controller</c> — ANY creature the controller controls, including this
///   one, fires it. On resolution the controller draws one card
///   (<see cref="Fx.DrawCards"/>; empty library flags the player for the
///   state-based loss per CR 704.5b).
///
/// - <b>Dies → return as an enchantment (CR 603.6c, CR 700.4, CR 701.20,
///   CR 205.2 / 613.1d)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> with <c>activeZones = {Battlefield,
///   Graveyard}</c> so the trigger survives the death zone-move (same
///   battlefield→graveyard self condition + dual-active-zone shape proven by
///   <see cref="MalakirRebirthFactory"/> / Persist). The printed
///   "if it was a creature" intervening-if is satisfied because the card is
///   still a creature on the battlefield when it dies (the type-strip only
///   applies AFTER the return). On resolution the card is returned from the
///   graveyard to the battlefield under its owner's control
///   (<see cref="Fx.ReturnFromGraveyardToBattlefield"/>, ZoneService-routed
///   when supplied so ETB triggers fire per CR 603.6a) and a captured
///   <c>hasReturned</c> flag flips true, which gates a
///   <see cref="Layer4TypeStripEffect"/> registered at construction. From
///   that point the Layer-4 effect strips <see cref="CardType.Creature"/> from
///   the card's layered characteristics — "It's an enchantment. (It's not a
///   creature.)" — exactly the machinery Heliod's devotion gate uses
///   (<see cref="HeliodSunCrownedFactory"/>).
///
/// ## Why a captured flag + Layer-4 strip (not a printed-type mutation)
///
/// CR 613.1d makes "isn't a creature" a continuous type-changing effect, not a
/// rewrite of the card's printed types. Modelling it as a
/// <see cref="Layer4TypeStripEffect"/> means every effective-type consumer
/// (combat, targeting, SBAs) reads the stripped type through the layer system,
/// and the printed Creature type stays intact (so a later effect that reads
/// printed characteristics — or a re-return — still sees the original card).
/// The strip is source-anchored: when the card LTBs again the effect is inert
/// (<see cref="Layer4TypeStripEffect.IsActive"/> gates on the source being on
/// the battlefield). The <c>hasReturned</c> predicate keeps the strip OFF
/// until the dies trigger actually returns the card, so the card is a normal
/// creature on its first time on the battlefield.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real Flash cast-timing</b> is supplied by the cast flow reading the
///   Flash marker; the factory only attaches the marker (same posture as the
///   other keyword markers on JSON-backed creatures).
/// - <b>"once it's an enchantment, dying again does not re-trigger the return"</b>:
///   the dies trigger's body re-checks the intervening-if via the same
///   <c>hasReturned</c> flag — once returned, a subsequent death finds the
///   card already non-creature (the strip is live) and the intervening-if
///   ("if it was a creature") fails, so it stays in the graveyard. This
///   matches the printed once-only return.
/// </summary>
[CardName("Enduring Curiosity")]
public static class EnduringCuriosityFactory
{
    public const string CardName = "Enduring Curiosity";
    public const string Slug = "enduring-curiosity";

    /// <summary>Cards drawn per combat-damage-to-a-player trigger.</summary>
    public const int DrawCount = 1;

    /// <summary>
    /// Construct Enduring Curiosity with no live runtime services. The Flash
    /// marker + both triggers are attached for shape inspection. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct Enduring Curiosity with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so the matching events land them on the stack automatically.</param>
    /// <param name="continuousEffects">When supplied, the
    /// <see cref="Layer4TypeStripEffect"/> backing "It's an enchantment.
    /// (It's not a creature.)" is registered on this service (gated OFF until
    /// the card has returned via the dies trigger). When null, the
    /// type-strip is not modelled — the card stays a creature after a return
    /// (shape-only path used by identity / trigger-shape tests).</param>
    /// <param name="zoneService">When supplied, the dies trigger's graveyard →
    /// battlefield return routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a); raw-zone fallback otherwise.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Cat + Glimmer subtypes, {2}{U}{U}, 4/3). The JSON
        // carries no abilities — Flash + the two triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.8 — Flash. Marker keyword read by the cast-timing path.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // Captured "the card has returned and is now a non-creature
        // enchantment" flag. Flipped true by the dies trigger after the
        // return; read by both the Layer-4 type-strip predicate and the dies
        // trigger's intervening-if re-check.
        var hasReturned = false;

        // ----------------------------------------------------------------
        // "Whenever a creature you control deals combat damage to a player,
        //  draw a card." (CR 510 / CR 603.1).
        // Predicate keys off the DAMAGING creature's controller (any of your
        // creatures, including this one) and a player target — NOT off this
        // source card. Controller is read live so a control-change re-points
        // the "you control" / "you draw" scope correctly (CR 109.5).
        // ----------------------------------------------------------------
        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var controller = card.Controller ?? owner;
                return ReferenceEquals(e.Source.Controller, controller);
            }),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: a creature you control hit a player — draw {DrawCount}",
                    () => { Fx.DrawCards(card.Controller ?? owner, DrawCount); }),
            },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // ----------------------------------------------------------------
        // "When Enduring Curiosity dies, if it was a creature, return it to
        //  the battlefield under its owner's control. It's an enchantment.
        //  (It's not a creature.)" (CR 603.6c / CR 700.4 / CR 701.20 /
        //  CR 205.2 / 613.1d).
        // ----------------------------------------------------------------
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: dies — if it was a creature, return it as a (non-creature) enchantment",
                    () => ReturnAsEnchantment(card, zoneService, ref hasReturned)),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // Layer 4 type-strip backing "It's an enchantment. (It's not a
        // creature.)" — CR 205.2 / 613.1d. Registered up-front but gated OFF
        // by the captured hasReturned flag, so the card is a normal creature
        // until the dies trigger returns it. After the return the predicate is
        // true and the Creature type is stripped from the layered
        // characteristics — same machinery Heliod's devotion gate uses
        // (HeliodSunCrownedFactory). Source-anchored: inert once the card LTBs.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () => hasReturned));
        }

        return card;
    }

    /// <summary>
    /// Resolve the dies trigger: if the card was a creature when it died,
    /// return it from the graveyard to the battlefield under its owner's
    /// control and flip <paramref name="hasReturned"/> so the Layer-4
    /// type-strip engages. Exposed for direct invocation by tests.
    /// </summary>
    /// <param name="card">The dead Enduring Curiosity (expected in the
    /// graveyard at resolution).</param>
    /// <param name="zoneService">Optional ZoneService for ETB-trigger-firing
    /// returns (CR 603.6a).</param>
    /// <param name="hasReturned">Captured flag flipped true on a successful
    /// return — gates the Layer-4 "isn't a creature" strip.</param>
    public static void ReturnAsEnchantment(
        Creature card,
        ZoneService? zoneService,
        ref bool hasReturned)
    {
        ArgumentNullException.ThrowIfNull(card);

        // CR 603.6c — intervening "if": only return if it was still a creature
        // when it died. Once it has already returned as a (non-creature)
        // enchantment, a subsequent death fails this check, so it stays put.
        if (hasReturned) return;

        // CR 608.2 — the card must still be in the graveyard at resolution
        // (a later effect could have moved it elsewhere).
        if (card.Zone != ZoneType.Graveyard) return;

        var owner = card.Owner;
        if (owner == null) return;

        // CR 701.20 — graveyard → battlefield under its owner's control.
        Fx.ReturnFromGraveyardToBattlefield(card, owner, zoneService);
        if (card.Zone != ZoneType.Battlefield) return;

        // CR 205.2 / 613.1d — from now on "It's an enchantment. (It's not a
        // creature.)" The Layer4TypeStripEffect registered at construction
        // reads this flag and strips the Creature type on every Compute pass.
        hasReturned = true;
    }
}
