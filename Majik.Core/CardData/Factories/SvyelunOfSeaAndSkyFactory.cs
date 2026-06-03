using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Svyelun of Sea and Sky (Kaldheim, {1}{U}{U}).
/// Legendary Creature — Merfolk God 3/4. Oracle text (verified against
/// Scryfall 2026-06-02):
///   "Svyelun has indestructible as long as you control at least two other
///    Merfolk.
///    Whenever Svyelun attacks, draw a card.
///    Other Merfolk you control have ward {1}. (Whenever another Merfolk you
///    control becomes the target of a spell or ability an opponent controls,
///    counter it unless that player pays {1}.)"
///
/// The base shape (name, Legendary Creature — Merfolk God, {1}{U}{U}, 3/4) is
/// materialised from the embedded JSON definition
/// (<c>svyelun-of-sea-and-sky.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three abilities are layered
/// on in C# (the JSON <c>AbilityDefinition</c> schema does not express
/// count-gated indestructible, attack triggers, or lord-granted keywords).
///
/// ## Implemented (v1)
/// - <b>Conditional indestructible (CR 702.12 / 704.5 / 613.1f)</b>:
///   "Svyelun has indestructible as long as you control at least two other
///   Merfolk." Wired via <see cref="IndestructibleGrantStaticEffect"/> whose
///   predicate matches only Svyelun itself and whose <c>activeWhile</c> gate
///   requires Svyelun to be on the battlefield AND its controller to control
///   at least two OTHER Merfolk (<see cref="CountOtherControllerMerfolk"/>).
///   The grant re-syncs on every <see cref="CardMovedEvent"/> when an event
///   bus is supplied, so the count is re-evaluated as Merfolk enter / leave.
/// - <b>Attack-trigger draw (CR 508.1f / 120.2)</b>: "Whenever Svyelun
///   attacks, draw a card." A <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnAttackSelf"/> whose effect moves the top card of
///   the controller's library to hand (empty library flags the draw-from-
///   empty SBA per CR 704.5b) — same draw shape as
///   <see cref="GlintSleeveSiphonerFactory"/>.
/// - <b>Ward {1} grant (CR 702.21)</b>: "Other Merfolk you control have ward
///   {1}." Wired via <see cref="LordStaticEffect"/> granting the
///   <c>"Ward"</c> keyword marker to other controller-Merfolk
///   (<c>matchingSubtype: Merfolk, includeSelf: false, allPlayers: false</c>).
///   Same keyword-marker posture as every printed-ward card in the pool
///   (Aboleth Spawn / Kappa Cannoneer / Tolarian Terror) — see the Deferred
///   note below.
///
/// ## Deferred (v1 gaps)
/// - <b>Ward-trigger enforcement</b>: ward is a keyword-surface marker only.
///   There is no battlefield-attached Ward trigger primitive yet (the whole
///   pool defers this — Aboleth Spawn / Kappa Cannoneer / Tolarian Terror),
///   so an opponent targeting a granted-ward Merfolk is not yet forced to pay
///   {1}. The grant flips <see cref="Creature.HasEffectiveKeyword"/>("Ward")
///   so discovery / future enforcement see it; the {1} cost is not yet
///   consulted.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone changes;
///   its <see cref="ContinuousEffect.IsActive"/> short-circuits when Svyelun
///   isn't on the battlefield so the grant lifts (same posture as Merfolk
///   Mistbinder). The indestructible grant's lifecycle is bus-driven and
///   unregisters on LTB (CR 109.3 — "other").
/// </summary>
[CardName("Svyelun of Sea and Sky")]
public static class SvyelunOfSeaAndSkyFactory
{
    public const string CardName = "Svyelun of Sea and Sky";
    public const string Slug = "svyelun-of-sea-and-sky";

    /// <summary>Minimum OTHER Merfolk the controller must control for the
    /// indestructible grant to switch on (CR 702.12).</summary>
    public const int IndestructibleMerfolkThreshold = 2;

    /// <summary>CR 702.21 — granted Ward cost on other Merfolk: {1}.</summary>
    public const int WardAmount = 1;

    /// <summary>
    /// Construct Svyelun with no live wiring. Suitable for card-shape /
    /// dispatcher tests — the indestructible grant is attached (and is inert
    /// without other Merfolk), the attack-draw trigger is attached but not
    /// registered with a bus, and the ward grant is NOT registered (no layers
    /// service). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Svyelun of Sea and Sky.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Bus for the indestructible grant's
    /// <see cref="CardMovedEvent"/> re-sync. May be null — the grant still
    /// syncs once on attach.</param>
    /// <param name="continuousEffects">Layers service to register the ward
    /// grant against. May be null — no live ward grant.</param>
    /// <param name="triggers">Trigger manager to register the attack-draw
    /// trigger against. May be null — the trigger is attached for inspection
    /// but does not fire on a live bus.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Merfolk God, {1}{U}{U}, 3/4). The JSON carries no abilities — all
        // three are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Svyelun has indestructible as long as you control at least two
        //  other Merfolk." (CR 702.12 / 704.5 / 613.1f.)
        //
        // The predicate matches only Svyelun itself (a self-static) AND
        // re-checks, lazily at every destroy-time gate, that its controller
        // controls >= 2 OTHER Merfolk. Putting the count in the PREDICATE (not
        // the activeWhile lifecycle gate) is deliberate: the destroy gates
        // consult the predicate live (CombatAbilities.HasIndestructible /
        // OracleSpellBinder.MoveToGraveyard), so the Merfolk count is always
        // current at the moment indestructibility matters — without needing a
        // re-sync event for every other Merfolk that enters or leaves (the
        // grant's bus re-sync only fires on events about Svyelun itself). Same
        // lazy-predicate posture as Darksteel Forge's controller check. The
        // default activeWhile gate keys the registration to Svyelun being on
        // the battlefield (source LTB pulls the grant).
        // ----------------------------------------------------------------
        var indestructible = new IndestructibleGrantStaticEffect(
            source: card,
            eventBus: eventBus,
            predicate: c => ReferenceEquals(c, card)
                && card.Zone == ZoneType.Battlefield
                && CountOtherControllerMerfolk(card)
                    >= IndestructibleMerfolkThreshold);
        indestructible.Attach();

        // ----------------------------------------------------------------
        // "Whenever Svyelun attacks, draw a card." (CR 508.1f / 120.2.)
        // OnAttackSelf — the standard per-attacker trigger. On resolution the
        // controller draws one card (library-top move; empty library flags
        // the draw-from-empty SBA per CR 704.5b).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card (attack trigger, CR 508.1f / 120.2)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 704.5b — drawing from an empty library is tracked via
                    // the SBA, resolved when the player next receives priority.
                    controller.MarkTriedToDrawFromEmptyLibrary();
                }
                else
                {
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // "Other Merfolk you control have ward {1}." (CR 702.21 / 613.1f.)
        // Granted via GrantAbilityToGroupLifecycle — a Layer-6 ability-adding
        // static that materialises a "Ward" KeywordAbility marker (arg: {1})
        // on every OTHER Merfolk the controller controls, with live
        // membership recomputed as Merfolk enter / leave (CR 611.2c). This is
        // the keyword-GRANT analogue of LordStaticEffect's grantedKeywords —
        // but at the Abilities layer, so HasEffectiveKeyword("Ward") actually
        // sees it (a LordStaticEffect sits at the PT_Modify layer, which the
        // effective-keyword computation filters out).
        //
        // Ward is a keyword-surface marker only in this engine — every
        // printed-ward card defers the spell-resolution Ward TRIGGER
        // enforcement (Aboleth Spawn / Kappa Cannoneer / Tolarian Terror), so
        // an opponent targeting a granted-ward Merfolk is not yet forced to
        // pay the {1}. The grant flips HasEffectiveKeyword("Ward") so
        // discovery / future enforcement see it.
        //
        // scope: OTHER Merfolk under the source's live controller (includeSelf
        // is excluded by the !ReferenceEquals check — CR 109.5 "Other").
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            var wardGrant = new GrantAbilityToGroupLifecycle(
                source: card,
                layers: continuousEffects,
                eventBus: eventBus,
                scope: p => !ReferenceEquals(p, card)
                            && p is Creature cr
                            && cr.HasSubtype(CardSubtype.Merfolk)
                            && ReferenceEquals(p.Controller, card.Controller),
                abilityFactory: member => new IAbility[]
                {
                    // CR 702.21 — Ward {1} keyword marker (arg: 1).
                    new KeywordAbility(
                        "Ward", member, member.Controller ?? owner, arg: WardAmount),
                },
                membershipProvider: () => ControllerBattlefield(card));
            wardGrant.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 702.12 helper — count Merfolk the source's live
    /// controller controls, excluding the source itself ("two OTHER
    /// Merfolk"). Keys off each candidate's live controller so control-change
    /// effects re-evaluate naturally.
    /// </summary>
    private static int CountOtherControllerMerfolk(Creature source)
    {
        var controller = source.Controller;
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, source)
                        && c is Creature cr
                        && cr.HasSubtype(CardSubtype.Merfolk)
                        && ReferenceEquals(cr.Controller, controller));
    }

    /// <summary>
    /// Live candidate set for the ward grant: every permanent on Svyelun's
    /// controller's battlefield. The <c>scope</c> predicate further filters
    /// to OTHER Merfolk the controller controls.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Creature source)
    {
        var controller = source.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
