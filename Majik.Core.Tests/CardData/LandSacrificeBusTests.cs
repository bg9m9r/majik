using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
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
}
