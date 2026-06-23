using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enduring Courage (Duskmourn: House of Horror,
/// {2}{R}{R}). Enchantment Creature — Dog Glimmer 3/3. Oracle text (verified
/// against Scryfall 2026-06-23):
///   "Whenever another creature you control enters, it gets +2/+0 and gains
///    haste until end of turn.
///    When Enduring Courage dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Member of the Duskmourn "Enduring" Glimmer cycle. The base shape (name,
/// Creature + Enchantment types, Dog + Glimmer subtypes, {2}{R}{R}, 3/3) is
/// materialised from the embedded JSON definition (<c>enduring-courage.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the ETB anaphoric pump/haste trigger and the dies → return-as-enchantment
/// trigger are layered on here (same JSON-backed-identity + code-attached-
/// behaviour posture as <see cref="EnduringVitalityFactory"/> /
/// <see cref="EnduringInnocenceFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>"Whenever another creature you control enters, it gets +2/+0 and gains
///   haste until end of turn." (CR 603.1 / CR 603.6a / CR 603.7e —
///   "it" / anaphoric self-reference to the triggering object)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> whose
///   predicate (<see cref="Triggers.OnAnotherCreatureYouControlEnters"/>)
///   matches a creature OTHER than this card (CR 109.5) controlled by this
///   card's controller entering the battlefield. The triggering creature is
///   captured off the matched event into <c>pendingCreature</c> (CR 603.7e —
///   the anaphoric "it" refers to the object whose entry caused the trigger).
///   On resolution the captured creature gets a Layer-7c
///   <see cref="PumpUntilEndOfTurnEffect"/> (+2/+0) and a Layer-6
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> ("Haste"), both registered
///   on the supplied game-wide <see cref="ContinuousEffectsService"/> and both
///   self-expiring in the cleanup step (CR 514.2). The Haste grant lifts
///   summoning sickness so the freshly-entered creature can attack / tap this
///   turn (CR 702.10b). Same pump/haste-grant machinery Legion Warboss applies
///   to its begin-combat token.
///
/// - <b>Dies → return as an enchantment (CR 603.6c / 700.4 / 701.20 / 205.2 /
///   613.1d)</b>: identical shape to <see cref="EnduringVitalityFactory"/> — a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnDies"/> with
///   <c>activeZones = {Battlefield, Graveyard}</c> so the trigger survives the
///   death zone-move. On resolution the card is returned from the graveyard to
///   the battlefield under its owner's control
///   (<see cref="Fx.ReturnFromGraveyardToBattlefield"/>) and a captured
///   <c>hasReturned</c> flag flips true, gating a
///   <see cref="Layer4TypeStripEffect"/> that strips
///   <see cref="CardType.Creature"/> ("It's an enchantment. (It's not a
///   creature.)"). The intervening-if "if it was a creature" is satisfied on
///   the first death (still a creature) and fails on a subsequent death once it
///   has already returned as a non-creature enchantment.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Shape-only path</b>: without a live <see cref="TriggerManager"/> the ETB
///   pump/haste trigger is attached to the card shape but not landed on the
///   stack automatically; without a <see cref="ContinuousEffectsService"/> the
///   pump/haste grant body no-ops (nothing to register against). The live
///   wire-up site always supplies both.
/// </summary>
[CardName("Enduring Courage")]
public static class EnduringCourageFactory
{
    public const string CardName = "Enduring Courage";
    public const string Slug = "enduring-courage";

    /// <summary>Power bonus the entering creature gets until end of turn.</summary>
    public const int PowerBonus = 2;

    /// <summary>Toughness bonus the entering creature gets (none — +2/+0).</summary>
    public const int ToughnessBonus = 0;

    /// <summary>
    /// Construct Enduring Courage with no live runtime services. Both triggers
    /// are attached for shape inspection (no live pump/haste grant, no
    /// type-strip). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (<c>NamedCardFactory.CreateGeneratedWithEffects</c>
    /// requires this exact <c>Create(Player, ContinuousEffectsService)</c>
    /// signature). Wires both the ETB pump/haste grant AND the Layer-4 dies →
    /// type-strip against the live game-wide service.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
        => Create(owner, triggers: null, continuousEffects: effects, zoneService: null);

    /// <summary>
    /// Construct Enduring Courage with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so the matching events land them on the stack
    /// automatically. May be null — both triggers are still attached to the
    /// card shape.</param>
    /// <param name="continuousEffects">Game-wide layers service. When supplied,
    /// (a) the ETB trigger's resolution registers the +2/+0 pump + Haste grant
    /// on the entering creature against this service, and (b) the
    /// <see cref="Layer4TypeStripEffect"/> backing "It's an enchantment. (It's
    /// not a creature.)" is registered (gated OFF until the dies trigger returns
    /// the card). When null, neither continuous effect is modelled (shape-only
    /// path used by identity / trigger-shape tests).</param>
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
        // Enchantment types, Dog + Glimmer subtypes, {2}{R}{R}, 3/3). The JSON
        // carries no abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Whenever another creature you control enters, it gets +2/+0 and
        //  gains haste until end of turn." (CR 603.1 / 603.6a / 603.7e).
        //
        // The triggering creature is captured off the matched CardMovedEvent
        // into pendingCreature so the anaphoric "it" (CR 603.7e) resolves to
        // the exact object whose entry fired the trigger — even if another
        // creature enters before this trigger resolves.
        // ----------------------------------------------------------------
        Creature? pendingCreature = null;

        var enterCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // Triggers.OnAnotherCreatureYouControlEnters semantics: a creature
            // OTHER than this card (CR 109.5), controlled by this card's
            // controller, entering the battlefield.
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (e.Card is not Creature other) return false;
            if (ReferenceEquals(other, card)) return false; // CR 109.5 — "another"
            if (!ReferenceEquals(other.Controller, card.Controller ?? owner)) return false;

            // CR 603.7e — capture the specific entering creature for the
            // anaphoric "it" so resolution buffs the right object.
            pendingCreature = other;
            return true;
        });

        var pumpEffect = new Effect(
            $"{CardName}: the entering creature gets +{PowerBonus}/+{ToughnessBonus} and gains haste until end of turn",
            () =>
            {
                if (continuousEffects == null) return; // shape-only path
                var target = pendingCreature;
                if (target == null) return;

                // The pump (Power) and Haste (Keywords) are both read off the
                // entering creature's ActiveEffects layers service, so the
                // creature must be wired to this game-wide service for the
                // grants to surface. In the live engine every permanent shares
                // the one game CES, so this is normally already the case.
                target.ActiveEffects ??= continuousEffects;

                // CR 613.7c (Layer 7c) — +2/+0 until end of turn.
                continuousEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PowerBonus, ToughnessBonus));

                // CR 613.1c (Layer 6) — gains Haste until end of turn, and
                // CR 702.10b — Haste lifts summoning sickness so the entering
                // creature can attack / tap this turn. HasHaste reads the
                // computed keyword set off this service.
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, "Haste"));
                target.HasSummoningSickness = false;
            });

        var enterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: enterCondition,
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(enterTrigger);
        triggers?.RegisterTriggeredAbility(enterTrigger);

        // Captured "the card has returned and is now a non-creature
        // enchantment" flag. Flipped true by the dies trigger after the return;
        // read by both the Layer-4 type-strip predicate and the dies trigger's
        // intervening-if re-check.
        var hasReturned = false;

        // ----------------------------------------------------------------
        // "When Enduring Courage dies, if it was a creature, return it to the
        //  battlefield under its owner's control. It's an enchantment. (It's
        //  not a creature.)" (CR 603.6c / 700.4 / 701.20 / 205.2 / 613.1d).
        // Identical machinery to Enduring Vitality / Enduring Innocence.
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
        // until the dies trigger returns it. Same machinery / rationale as
        // EnduringVitalityFactory.
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

        // CR 608.2 — the card must still be in the graveyard at resolution.
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
