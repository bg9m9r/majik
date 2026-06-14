using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the land/binder subset of the sac-cost-bus deferral
/// (sac-cost-bus-land-and-binder-payers). Lands route through the binder chain
/// in production — NOT their <c>[CardName]</c> factory
/// (named-factory-vs-binder-chain) — so the self-sacrifice leg of a fetch-land /
/// utility-land cost was constructed with the bus-less
/// <see cref="AdditionalCost.Sacrifice(Permanent)"/> overload and paying it
/// published nothing. "Whenever a/an [player] sacrifices …" aristocrat payoffs
/// never observed a binder-bound land sacrifice (CR 701.16a).
///
/// <para>The fix threads the production <see cref="IEventBus"/> (already in scope
/// at <c>DeckCardBuilder.BindCardAbilities</c>) into both land binders
/// (<see cref="OracleLandActivatedAbilityBinder"/> +
/// <see cref="LandActivatedAbilityBinder"/>), which forward it to
/// <see cref="AdditionalCost.Sacrifice(Permanent, IEventBus?)"/> so paying the
/// self-sacrifice cost publishes a <see cref="PermanentSacrificedEvent"/>
/// crediting the cost-payer. The bus-less binder calls (no event bus) preserve
/// the legacy publish-nothing posture.</para>
/// </summary>
public class LandSacrificeBusTests
{
    private static (EventBus bus, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, seen);
    }

    private static AdditionalCost SacCost(ICard land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    // ---------------------------------------------------------------------
    // OracleLandActivatedAbilityBinder — fetch-land cycle (self-sac leg).
    // ---------------------------------------------------------------------

    [Fact]
    public void FetchLand_BinderThreadsBus_SacCostPublishes()
    {
        var alice = new Player("Alice", 20);
        var (bus, seen) = Wired();

        var fetch = new Land("Misty Rainforest") { Owner = alice, Controller = alice };
        var entity = new CardEntity
        {
            Name = "Misty Rainforest",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life, Sacrifice Misty Rainforest: Search your library " +
                         "for a Forest or Island card, put it onto the battlefield, then shuffle.",
        };

        var bound = OracleLandActivatedAbilityBinder.Bind(fetch, entity, alice, bus);
        bound.Should().BeTrue();

        alice.Zones.Battlefield.AddCard(fetch);
        fetch.SetZone(ZoneType.Battlefield);

        // Pay only the sacrifice leg in isolation (the bus-bearing cost).
        SacCost(fetch).Pay(alice);

        fetch.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == fetch
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Fact]
    public void FetchLand_BinderWithoutBus_StaysBusLess_NoPublish()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        var fetch = new Land("Misty Rainforest") { Owner = alice, Controller = alice };
        var entity = new CardEntity
        {
            Name = "Misty Rainforest",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life, Sacrifice Misty Rainforest: Search your library " +
                         "for a Forest or Island card, put it onto the battlefield, then shuffle.",
        };

        // No bus threaded (legacy posture).
        OracleLandActivatedAbilityBinder.Bind(fetch, entity, alice);

        alice.Zones.Battlefield.AddCard(fetch);
        fetch.SetZone(ZoneType.Battlefield);

        SacCost(fetch).Pay(alice);

        fetch.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the bus-less binder call");
    }

    // ---------------------------------------------------------------------
    // LandActivatedAbilityBinder — utility-land self-sac (Treasure Vault).
    // "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens."
    // ---------------------------------------------------------------------

    [Fact]
    public void UtilityLand_BinderThreadsBus_SacCostPublishes()
    {
        var alice = new Player("Alice", 20);
        var (bus, seen) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var land = new Land("Treasure Vault") { Owner = alice, Controller = alice };
        var entity = new CardEntity
        {
            Name = "Treasure Vault",
            TypeLine = "Artifact Land",
            OracleText = "{T}: Add {C}.\n" +
                         "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens.",
        };

        var bound = LandActivatedAbilityBinder.Bind(land, entity, alice, effects, triggers: null, eventBus: bus);
        bound.Should().BeTrue();

        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SacCost(land).Pay(alice);

        land.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == land
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Fact]
    public void UtilityLand_BinderWithoutBus_StaysBusLess_NoPublish()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        var effects = new ContinuousEffectsService(bus);

        var land = new Land("Treasure Vault") { Owner = alice, Controller = alice };
        var entity = new CardEntity
        {
            Name = "Treasure Vault",
            TypeLine = "Artifact Land",
            OracleText = "{T}: Add {C}.\n" +
                         "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens.",
        };

        // No bus threaded (legacy posture) — note the default eventBus param.
        LandActivatedAbilityBinder.Bind(land, entity, alice, effects);

        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SacCost(land).Pay(alice);

        land.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the bus-less binder call");
    }

    // ---------------------------------------------------------------------
    // LandActivatedAbilityBinder — TYPED NON-SELF land sacrifice
    // (sacrifice-a-desert-typed-non-self-land-cost).
    //
    // Ramunap Ruins' "{2}{R}{R}, {T}, Sacrifice a Desert: This land deals 2
    // damage to each opponent" is the TYPED non-self sacrifice cost (CR 701.16):
    // unlike "Sacrifice this land", the controller sacrifices ANY Desert they
    // control — the source land qualifies (it IS a Desert) but is not the only
    // legal choice. The binder must bind a real SacrificeFilteredCost over the
    // controller's Deserts, NOT a self-only AdditionalCost.Sacrifice stub. Lands
    // route through the binder chain in prod, never their [CardName] factory
    // (named-factory-vs-binder-chain), so this is the live path.
    // ---------------------------------------------------------------------

    private static SacrificeFilteredCost TypedSacCost(ICard land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<SacrificeFilteredCost>()
            .First();

    private static Land DesertLand(string name, Player owner)
    {
        var l = new Land(name, subtypes: new[] { CardSubtype.Desert })
        { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Land BindRamunapRuins(Player owner, ContinuousEffectsService effects, IEventBus? bus)
    {
        var land = new Land("Ramunap Ruins", subtypes: new[] { CardSubtype.Desert })
        { Owner = owner, Controller = owner };
        var entity = new CardEntity
        {
            Name = "Ramunap Ruins",
            TypeLine = "Land — Desert",
            // Exact Scryfall oracle text.
            OracleText = "{T}: Add {C}.\n" +
                         "{T}, Pay 1 life: Add {R}.\n" +
                         "{2}{R}{R}, {T}, Sacrifice a Desert: This land deals 2 damage to each opponent.",
        };
        var bound = LandActivatedAbilityBinder.Bind(land, entity, owner, effects, triggers: null, eventBus: bus);
        bound.Should().BeTrue("the binder recognises the {2}{R}{R}, {T}, Sacrifice a Desert ability");
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void TypedSacrifice_RamunapRuins_BindsRealFilteredCost_NotSelfStub()
    {
        var alice = new Player("Alice", 20);
        var (bus, _) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var land = BindRamunapRuins(alice, effects, bus);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<SacrificeFilteredCost>().Should().ContainSingle(
            "\"Sacrifice a Desert\" binds a typed non-self SacrificeFilteredCost (CR 701.16)");
        // It must NOT be modelled as a self-only AdditionalCost.Sacrifice stub.
        ability.Costs.OfType<AdditionalCost>()
            .Where(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().BeEmpty("the typed cost is non-self — no self-sac AdditionalCost is added");
    }

    [Fact]
    public void TypedSacrifice_RamunapRuins_CanSacrificeAnotherDesert_NotItself()
    {
        var alice = new Player("Alice", 20);
        var (bus, seen) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var ruins = BindRamunapRuins(alice, effects, bus);
        // A SECOND Desert the controller controls — the non-self choice.
        var otherDesert = DesertLand("Sunscorched Desert", alice);

        var sac = TypedSacCost(ruins);
        sac.CanPay(alice).Should().BeTrue("the controller controls at least one Desert");

        // Agent pre-picks the OTHER Desert (CR 701.16 — controller's choice).
        sac.Target = otherDesert;
        sac.Pay(alice);

        otherDesert.Zone.Should().Be(ZoneType.Graveyard, "the chosen non-self Desert is sacrificed");
        ruins.Zone.Should().Be(ZoneType.Battlefield, "Ramunap Ruins itself survives — it was not the pick");
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == otherDesert
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Fact]
    public void TypedSacrifice_RamunapRuins_SacrificesItself_WhenOnlyDesert_AndPublishes()
    {
        var alice = new Player("Alice", 20);
        var (bus, seen) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var ruins = BindRamunapRuins(alice, effects, bus);

        var sac = TypedSacCost(ruins);
        // No Target pre-set + Ramunap Ruins is the only Desert → it qualifies and
        // is the deterministic v1 pick (CR 701.16 — the source qualifies).
        sac.Pay(alice);

        ruins.Zone.Should().Be(ZoneType.Graveyard, "the only Desert is sacrificed (itself)");
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == ruins
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Fact]
    public void TypedSacrifice_RamunapRuins_WithoutBus_StaysBusLess_NoPublish()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        var effects = new ContinuousEffectsService(bus);

        // No bus threaded into the binder (legacy posture).
        var land = BindRamunapRuins(alice, effects, bus: null);

        TypedSacCost(land).Pay(alice);

        land.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the bus-less binder call");
    }
}
