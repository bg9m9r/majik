using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bristlebud Farmer (Bloomburrow, {2}{G}{G}).
///
/// Creature — Plant Druid 5/5. Oracle text (verified against the embedded
/// Scryfall seed):
///   "Trample
///    When this creature enters, create two Food tokens. (They're artifacts
///    with "{2}, {T}, Sacrifice this token: You gain 3 life.")
///    Whenever this creature attacks, you may sacrifice a Food. If you do,
///    mill three cards. You may put a permanent card from among them into
///    your hand."
///
/// Bristlebud Farmer is the Food-producing green beater of Bloomburrow: it
/// makes two Food on entry (same <see cref="TokenFactory.CreateFood"/>
/// production as <see cref="TirelessProvisionerFactory"/> /
/// <see cref="SamwiseGamgeeFactory"/>), then on each attack converts a Food
/// into card selection — milling three and recurring a permanent from the
/// mill (the reveal-and-choose half mirrors
/// <see cref="SatyrWayfinderFactory"/>, filtered to <i>permanent</i> cards
/// rather than lands; the sacrifice-a-Food cost reuses
/// <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/>).
///
/// The base shape (name, Creature, Plant + Druid subtypes, {2}{G}{G}, 5/5,
/// Trample keyword marker — CR 702.19) is materialised from the embedded JSON
/// definition (<c>bristlebud-farmer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two triggered abilities are
/// layered on here (the JSON AbilityDefinition schema expresses neither the
/// ETB token creation nor the attack mill-and-recur).
///
/// ## Implemented (v1)
/// - 5/5 <see cref="Creature"/> — Plant Druid at {2}{G}{G}, green, Trample,
///   owner / controller stamped.
/// - <b>ETB-two-Food trigger (CR 603.6a / CR 111.10)</b>: a single
///   <see cref="TriggeredAbility"/> firing on this card's own ETB
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>). On resolution it creates
///   two Food tokens via <see cref="TokenFactory.CreateFood"/>, threading the
///   optional <see cref="ZoneService"/> so each Food's own ETB
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires for downstream
///   subscribers.
/// - <b>Attack mill-and-recur trigger (CR 508.1f / CR 701.13b)</b>: a single
///   <see cref="TriggeredAbility"/> firing when Bristlebud Farmer attacks
///   (<see cref="Triggers.OnAttackSelf"/>). On resolution it <em>may</em>
///   sacrifice a Food the controller controls; if it does, it mills three
///   cards (the top three of the controller's library into the graveyard,
///   CR 701.13b) and <em>may</em> put a permanent card (CR 110.1 —
///   artifact / creature / enchantment / land / planeswalker / battle) from
///   among the three milled into the controller's hand. The mill-then-pick is
///   routed through the shared
///   <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> primitive
///   (top three → permanent to hand / rest to graveyard), the identical
///   closure <see cref="SatyrWayfinderFactory"/> uses — handling library
///   underflow (CR 121.2 — fewer than three cards mills what's there), the
///   "you may put" opt-out (CR 116.1b), and routing the zone moves through the
///   registered <c>ZoneService</c>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + both triggers attached (neither
///   registered with a <see cref="TriggerManager"/>; Food creation + mill use
///   direct-zone fallbacks). The overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired: the Food tokens + the mill zone moves route through the
///   ZoneService, and both triggers register with the bus.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may sacrifice a Food" opt-out</b>: the attack trigger
///   deterministically sacrifices the first Food the controller controls when
///   one is present (the upside line — mill + recur a permanent is strictly
///   card advantage, so a v1 bot always takes it). The "you may" decline and
///   agent-driven Food selection are the shared Food-sacrifice prompt gap
///   (same posture as <see cref="WickedWolfFactory"/> /
///   <see cref="SamwiseGamgeeFactory"/>), not specific to this card. With no
///   Food in play the trigger is a clean no-op ("if you do" — CR 603.6a).
/// - <b>"You may put a permanent card" prompt</b>: routed through
///   <see cref="RevealAndChoose"/>, which prompts the registered agent and
///   falls back to the first permanent in mill order when no agent is wired
///   (shared reveal-and-choose posture).
/// </summary>
[CardName("Bristlebud Farmer")]
public static class BristlebudFarmerFactory
{
    public const string CardName = "Bristlebud Farmer";
    public const string Slug = "bristlebud-farmer";
    public const string PrintedManaCost = "{2}{G}{G}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>How many Food tokens the ETB trigger creates.</summary>
    public const int EtbFoodCount = 2;

    /// <summary>How many cards the attack trigger mills.</summary>
    public const int MillCount = 3;

    /// <summary>
    /// Construct Bristlebud Farmer with no live runtime services. Both
    /// triggered abilities (ETB-two-Food and the attack mill-and-recur) are
    /// attached for shape observability; neither is registered with any
    /// <see cref="TriggerManager"/> and no <see cref="ZoneService"/> is wired
    /// (Food creation + the mill use direct-zone fallbacks). Suitable for
    /// shape / dispatcher tests — the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Bristlebud Farmer with optional runtime services.
    /// <paramref name="zoneService"/> routes the created Food tokens' ETB and
    /// the mill zone moves so <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// publishes. <paramref name="triggers"/> registers both triggers so the
    /// bus drives them automatically (ETB on entry, the mill-and-recur on
    /// attack).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Plant +
        // Druid subtypes, {2}{G}{G}, 5/5, Trample keyword marker). The JSON
        // carries no abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-two-Food trigger — CR 603.6a + CR 111.10.
        //   "When this creature enters, create two Food tokens."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create {EtbFoodCount} Food tokens",
            () =>
            {
                var controller = card.Controller ?? owner;
                for (var i = 0; i < EtbFoodCount; i++)
                {
                    // CR 111.10 — Food token. ZoneService (when wired) fires
                    // each Food's own ETB CardMovedEvent.
                    TokenFactory.CreateFood(controller, zoneService);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack mill-and-recur trigger — CR 508.1f + CR 701.13b.
        //   "Whenever this creature attacks, you may sacrifice a Food. If you
        //    do, mill three cards. You may put a permanent card from among
        //    them into your hand."
        //
        // v1: deterministically sacrifices the first Food the controller
        // controls (the upside line — see class xmldoc deferred note). With no
        // Food it is a clean no-op ("if you do"). The mill-then-pick is the
        // reveal-and-choose primitive: mill three (top three → graveyard,
        // CR 701.13b) and may put a PERMANENT card (CR 110.1) into hand.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: may sacrifice a Food — if you do, mill {MillCount} " +
            "and may put a permanent card into hand",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // "you may sacrifice a Food" — v1 takes the line whenever a
                // Food is available (CR 116.1b decline = shared Food-prompt
                // gap). The SacrificeAFoodCost picks + sacrifices the first
                // Food the controller controls.
                var foodCost = new UnderworldCookbookFactory.SacrificeAFoodCost();
                if (!foodCost.CanPay(controller))
                {
                    // No Food → "if you do" fails; mill + recur don't happen.
                    return;
                }

                foodCost.Pay(controller);

                // "If you do, mill three cards. You may put a permanent card
                // from among them into your hand." (CR 701.13b mill = top N
                // into graveyard; here all three go to graveyard then one
                // permanent moves to hand — mechanically the reveal-and-choose
                // shape, restDestination = Graveyard.)
                await RevealAndChoose.RevealTopAndChooseAsync(
                    ctx: ctx,
                    caster: controller,
                    count: MillCount,
                    // CR 110.1 — a permanent card: artifact, creature,
                    // enchantment, land, planeswalker, or battle.
                    eligiblePredicate: IsPermanentCard,
                    optional: true,
                    label: "Permanent card to put into hand",
                    pickedDestination: ZoneType.Hand,
                    restDestination: ZoneType.Graveyard,
                    sourceTag: Slug).ConfigureAwait(false);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 110.1 — a permanent card is a card with one of the permanent card
    /// types: artifact, creature, enchantment, land, or planeswalker (Battle
    /// is a permanent type in CR 110.1 but is not yet modelled in
    /// <see cref="CardType"/>; no Modern-pool card relies on it here).
    /// </summary>
    private static bool IsPermanentCard(ICard card) =>
        card.HasType(CardType.Artifact)
        || card.HasType(CardType.Creature)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Land)
        || card.HasType(CardType.Planeswalker);
}
