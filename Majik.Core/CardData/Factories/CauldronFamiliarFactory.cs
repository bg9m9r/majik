using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cauldron Familiar (Throne of Eldraine, {B}).
///
/// Creature — Cat 1/1. Oracle text (Scryfall, verified):
///   "When this creature enters, each opponent loses 1 life and you gain
///    1 life.
///    Sacrifice a Food: Return this card from your graveyard to the
///    battlefield."
///
/// Cauldron Familiar is the Food-recursion half of the Witch's Oven /
/// Cauldron combo: the oven turns it into a Food, the familiar's
/// graveyard ability sacks a Food to come back, and its ETB drains each
/// opponent (a Blood-Artist-style life swing on a loop). The ETB drain
/// reuses Zulaport Cutthroat's "each opponent loses 1, you gain 1"
/// resolver convention; the graveyard-return ability reuses Underworld
/// Cookbook's <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/>.
///
/// The base shape (name, Creature, Cat subtype, {B}, 1/1) is materialised
/// from the embedded JSON definition (<c>cauldron-familiar.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no
/// abilities — the ETB drain trigger and the Food-sacrifice graveyard
/// return are layered on here (same posture as
/// <see cref="TwinSilkSpiderFactory"/>, whose JSON is shape-only).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Cat at {B}, owner/controller wired.
/// - <b>ETB drain trigger (CR 603.6a)</b>: a single
///   <see cref="TriggeredAbility"/> firing on this card's own ETB
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>). On resolution each
///   opponent loses 1 life and the controller gains 1 life. Opponents are
///   enumerated via the optional <paramref name="opponentResolver"/>
///   (mirrors <see cref="ZulaportCutthroatFactory"/>'s resolver
///   convention — single-arg <c>Create(owner)</c> silently no-ops the
///   opponent-drain side; the lifegain side ALWAYS fires per the printed
///   "and you gain 1 life" clause). CR 119.3 — the loss and gain are
///   discrete life events.
/// - <b>"Sacrifice a Food: Return this card from your graveyard to the
///   battlefield." (CR 602 / CR 113.6)</b>: an <see cref="ActivatedAbility"/>
///   whose only cost is
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> (no mana —
///   the printed cost is the Food sacrifice alone). The ability functions
///   while Cauldron Familiar is in its controller's graveyard
///   (CR 113.6 — an ability that explicitly references the graveyard is
///   usable from there); its <c>activeZones</c> is {Graveyard}. On
///   resolution the card moves Graveyard → Battlefield via
///   <see cref="ZoneService.MoveCard"/> when supplied (so its own ETB
///   <see cref="CardMovedEvent"/> publishes and the drain trigger re-fires,
///   CR 603.6a), otherwise via direct-zone mutation.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities are attached
///   for shape observability; the ETB trigger is not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring, no
///   opponent resolver (drain side no-ops, lifegain still fires). This is
///   the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, Func{IReadOnlyList{Player}}?, ZoneService?, TriggerManager?)"/>
///   — fully wired.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-a-Food target prompt</b>: the embedded
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> picks the
///   first Food the controller controls deterministically (shared v1
///   sacrifice-picker posture). Agent-driven Food selection is the shared
///   gap, not specific to this card.
/// - <b>Each-opponent enumeration</b>: same resolver convention as
///   Zulaport Cutthroat — the live game supplies <c>Game.Players</c> minus
///   the controller via <paramref name="opponentResolver"/>.
/// </summary>
[CardName("Cauldron Familiar")]
public static class CauldronFamiliarFactory
{
    public const string CardName = "Cauldron Familiar";
    public const string Slug = "cauldron-familiar";
    public const string PrintedManaCost = "{B}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Cauldron Familiar with no live runtime services. The ETB
    /// drain trigger and the Food-sacrifice graveyard-return ability are
    /// attached for shape observability; the trigger is not registered with
    /// any <see cref="TriggerManager"/>, no <see cref="ZoneService"/> is
    /// wired (the return uses direct-zone mutation), and no opponent
    /// resolver is supplied (the drain side no-ops, lifegain still fires).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Cauldron Familiar with optional runtime services.
    /// <paramref name="opponentResolver"/> supplies the player list the ETB
    /// trigger drains 1 life from (typically every <c>Game.Players</c> entry
    /// that isn't the controller). <paramref name="triggers"/> registers the
    /// ETB trigger so the bus drives it automatically.
    /// <paramref name="zoneService"/> routes the graveyard-return zone move
    /// (and the ETB trigger registration) so <see cref="CardMovedEvent"/>
    /// publishes.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Cat
        // subtype, {B}, 1/1). The JSON carries no abilities — both abilities
        // are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB drain trigger — CR 603.6a.
        //   "When this creature enters, each opponent loses 1 life and you
        //    gain 1 life."
        // Same drain math / resolver convention as Zulaport Cutthroat, but
        // on this card's OWN enters-the-battlefield event rather than a
        // dies event.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses 1 life + controller gains 1 life",
            () =>
            {
                var controller = card.Controller ?? owner;
                var opponents = opponentResolver?.Invoke();
                if (opponents != null)
                {
                    foreach (var opp in opponents)
                    {
                        if (ReferenceEquals(opp, controller)) continue;
                        // CR 119.3 — life loss is a discrete event.
                        opp.LoseLife(DrainAmount);
                    }
                }
                // CR 119.3 — lifegain is a separate discrete event and fires
                // unconditionally per the printed "and you gain 1 life".
                controller.GainLife(GainAmount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "Sacrifice a Food: Return this card from your graveyard to the
        // battlefield." — CR 602 activated ability with NO mana cost; the
        // printed cost is the Food sacrifice alone. CR 113.6 — the ability
        // explicitly references the graveyard, so it is usable while the
        // card is in its controller's graveyard (activeZones = {Graveyard}).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return this card from your graveyard to the battlefield",
            () => ResolveGraveyardReturn(card, owner, zoneService));

        var returnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new UnderworldCookbookFactory.SacrificeAFoodCost(),
            },
            effects: new IEffect[] { returnEffect });

        // CR 113.6 — this ability functions only from the graveyard.
        card.AddAbility(returnAbility);

        return card;
    }

    /// <summary>
    /// Resolve "Return this card from your graveyard to the battlefield."
    /// CR 603.6a / CR 608 — validates Cauldron Familiar is still in its
    /// controller's graveyard at resolution (clean no-op otherwise, CR
    /// 608.2b), then moves it Graveyard → Battlefield. The ZoneService path
    /// publishes the ETB <see cref="CardMovedEvent"/> so the drain trigger
    /// re-fires; the direct-zone fallback keeps shape-test semantics.
    /// </summary>
    private static void ResolveGraveyardReturn(
        Creature card,
        Player owner,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" resolves to the source's controller.
        var controller = card.Controller ?? owner;

        // CR 608.2b — the card must still be in the controller's graveyard.
        if (card.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(card)) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(card);
            controller.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
        }
    }
}
