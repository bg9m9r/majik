using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Samwise Gamgee (The Lord of the Rings: Tales of
/// Middle-earth, {1}{W}).
///
/// Legendary Creature — Halfling Peasant 2/1. Oracle text (Scryfall,
/// verified):
///   "Whenever another nontoken creature you control enters, create a Food
///    token. (It's an artifact with "{2}, {T}, Sacrifice this token: You gain
///    3 life.")
///    Sacrifice three Foods: Return target historic card from your graveyard
///    to your hand. (Artifacts, legendaries, and Sagas are historic.)"
///
/// The base shape (name, Legendary supertype, Creature, Halfling + Peasant
/// subtypes, {1}{W}, 2/1) is materialised from the embedded JSON definition
/// (<c>samwise-gamgee.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities —
/// the two printed behaviours are layered on here (same posture as
/// <see cref="CauldronFamiliarFactory"/>, whose JSON is shape-only).
///
/// ## Implemented (v1)
/// - 2/1 Legendary Creature — Halfling Peasant, mana cost {1}{W},
///   owner/controller wired.
/// - <b>ETB-Food trigger (CR 603.6a)</b>: a single <see cref="TriggeredAbility"/>
///   over <see cref="CardMovedEvent"/> firing when ANOTHER nontoken creature
///   the controller controls enters the battlefield. The condition mirrors
///   <see cref="Triggers.OnAnotherCreatureYouControlEnters"/> but adds the
///   printed "nontoken" filter (CR 111.1 — a token is excluded). On
///   resolution a Food token is created via <see cref="TokenFactory.CreateFood"/>
///   (CR 111.10), threading the optional <see cref="ZoneService"/> so the
///   Food's own ETB <see cref="CardMovedEvent"/> fires for downstream
///   subscribers.
/// - <b>"Sacrifice three Foods: Return target historic card from your
///   graveyard to your hand." (CR 602 / CR 117.1)</b>: an
///   <see cref="ActivatedAbility"/> whose cost is THREE
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> instances (no
///   mana — the printed cost is the three Food sacrifices alone). The whole
///   cost is payable only when the controller controls at least three Foods
///   (CR 117.1 — see <see cref="CanPayAllFoodCosts"/>). On resolution a
///   <em>historic</em> card (Artifact / Legendary / Saga — CR 205.2b / 205.4 /
///   714, via <see cref="MonumentalHengeFactory.IsHistoric"/>) is returned
///   from the controller's graveyard to their hand (EternalWitness-style
///   Graveyard → Hand move, CR 608.2b illegal-on-resolution guard).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities are attached;
///   the ETB trigger is not registered with any <see cref="TriggerManager"/>,
///   no <see cref="ZoneService"/> wiring (the Food creation / graveyard
///   return use direct-zone fallbacks). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired: the Food token + the graveyard return route through the
///   ZoneService, and the ETB trigger is registered with the bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-Food target prompt</b>: the embedded
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> picks the
///   first Food the controller controls deterministically (shared v1
///   sacrifice-picker posture). Agent-driven Food selection is the shared
///   gap, not specific to this card.
/// - <b>"Target historic card" prompt</b>: the resolve body honours an
///   agent-set <see cref="ActivatedAbility.ChosenTargets"/> if present,
///   otherwise falls back to the first historic card in the controller's
///   graveyard (same first-match posture as
///   <see cref="EternalWitnessFactory"/>).
/// </summary>
[CardName("Samwise Gamgee")]
public static class SamwiseGamgeeFactory
{
    public const string CardName = "Samwise Gamgee";
    public const string Slug = "samwise-gamgee";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Number of Foods the graveyard-return ability sacrifices.</summary>
    public const int FoodSacrificeCount = 3;

    /// <summary>
    /// Construct Samwise Gamgee with no live runtime services. The ETB-Food
    /// trigger and the Sacrifice-three-Foods graveyard-return ability are
    /// attached for shape observability; the trigger is not registered with
    /// any <see cref="TriggerManager"/> and no <see cref="ZoneService"/> is
    /// wired (Food creation + graveyard return use direct-zone fallbacks).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Samwise Gamgee with optional runtime services.
    /// <paramref name="zoneService"/> routes the created Food token's ETB and
    /// the graveyard-return zone move so <see cref="CardMovedEvent"/>
    /// publishes. <paramref name="triggers"/> registers the ETB-Food trigger
    /// so the bus drives it automatically when another nontoken creature
    /// enters.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Halfling + Peasant subtypes, {1}{W}, 2/1). The JSON
        // carries no abilities — both are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-Food trigger — CR 603.6a.
        //   "Whenever another nontoken creature you control enters, create a
        //    Food token."
        // Same shape as Triggers.OnAnotherCreatureYouControlEnters but with
        // the printed "nontoken" filter (CR 111.1 — a token is a Permanent
        // with IsToken set; exclude it).
        // ----------------------------------------------------------------
        var foodTriggerCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (!e.Card.HasType(CardType.Creature)) return false;
                // "another" — Samwise's own ETB does not fire it (CR 603.6e).
                if (ReferenceEquals(e.Card, card)) return false;
                // "you control" — the entering creature is the controller's.
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(e.Card.Controller, controller)) return false;
                // "nontoken" — exclude tokens (CR 111.1).
                if (e.Card is Permanent perm && perm.IsToken) return false;
                return true;
            });

        var foodEffect = new Effect(
            $"{CardName}: create a Food token",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111.10 — Food token.
                TokenFactory.CreateFood(controller, zoneService);
            });

        var foodTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: foodTriggerCondition,
            effects: new IEffect[] { foodEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(foodTrigger);
        triggers?.RegisterTriggeredAbility(foodTrigger);

        // ----------------------------------------------------------------
        // "Sacrifice three Foods: Return target historic card from your
        // graveyard to your hand." — CR 602 activated ability with NO mana
        // cost; the printed cost is three Food sacrifices alone (CR 117.1).
        // ----------------------------------------------------------------
        ActivatedAbility? returnAbility = null;

        var returnEffect = new Effect(
            $"{CardName}: return target historic card from your graveyard to your hand",
            () => ResolveHistoricReturn(card, owner, returnAbility, zoneService));

        var costs = new ICost[FoodSacrificeCount];
        for (var i = 0; i < FoodSacrificeCount; i++)
        {
            costs[i] = new UnderworldCookbookFactory.SacrificeAFoodCost();
        }

        returnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: costs,
            effects: new IEffect[] { returnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target historic card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    // CR 205.2b / 205.4 / 714 — only historic cards are legal.
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(MonumentalHengeFactory.IsHistoric)
                        .Cast<object>().ToList()),
            });

        card.AddAbility(returnAbility);

        return card;
    }

    /// <summary>
    /// CR 117.1 — the whole activated cost (three independent
    /// "Sacrifice a Food" costs) is payable only if every cost can be paid.
    /// Because each <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/>
    /// gates on "at least one Food" rather than reserving the Food it will
    /// sacrifice, naively calling <c>CanPay</c> on each returns true even with
    /// a single Food. This helper models the real requirement: the controller
    /// must control at least as many Foods as there are Food-sacrifice costs.
    /// </summary>
    public static bool CanPayAllFoodCosts(ActivatedAbility ability, Player player)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (player == null) return false;

        var foodCosts = ability.Costs
            .OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Count();
        if (foodCosts == 0) return false;

        var foods = player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Count(p => p.HasType(CardType.Artifact)
                     && p.HasSubtype(CardSubtype.Food));

        return foods >= foodCosts;
    }

    /// <summary>
    /// Resolve "Return target historic card from your graveyard to your hand."
    /// Reads the ability's <see cref="ActivatedAbility.ChosenTargets"/>; falls
    /// back to the first HISTORIC card in the controller's graveyard when no
    /// target was set (deterministic single-arg posture — mirrors
    /// <see cref="EternalWitnessFactory"/>'s first-candidate fallback).
    /// Validates the chosen card is still a historic card in the controller's
    /// graveyard at resolution (CR 608.2b — illegal target → clean no-op),
    /// then moves it Graveyard → Hand.
    /// </summary>
    private static void ResolveHistoricReturn(
        Creature card,
        Player owner,
        ActivatedAbility? ability,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" resolves to the source's controller.
        var controller = card.Controller ?? owner;

        ICard? picked = null;

        // 1) Honour an agent-set target if present (production path).
        if (ability != null && ability.ChosenTargets.Count > 0
            && ability.ChosenTargets[0].Count > 0
            && ability.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first HISTORIC card in the controller's
        // graveyard (single-arg dispatcher path / no-agent posture).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(MonumentalHengeFactory.IsHistoric);

        // No historic card → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b — target must still be a historic card in the
        // controller's graveyard at resolution.
        if (!MonumentalHengeFactory.IsHistoric(picked)) return;
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a CardMovedEvent
        // so any "leaves graveyard" triggers fire (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(picked);
            controller.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
