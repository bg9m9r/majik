using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ConclaveTribunalFactory"/>.
///
/// Conclave Tribunal is structurally Banishing Light + Convoke. The
/// exile / return tests cover the shared
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> path
/// from the Tribunal entry point so a future change in the shared
/// wiring can't silently break either card. Convoke wiring is asserted
/// via the keyword-ability marker + <see cref="ConvokeAdditionalCost"/>
/// build path (same shape as Chord of Calling's tests).
/// </summary>
[Trait("Color", "W")]
public class ConclaveTribunalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ConclaveTribunal_Identity()
    {
        var c = ConclaveTribunalFactory.Create(_alice);

        c.Name.Should().Be("Conclave Tribunal");
        c.ManaCost.Should().Be("{4}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // One Convoke keyword marker + ETB + LTB triggers.
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Convoke");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void ConclaveTribunal_Etb_ExilesOpponentPermanent()
    {
        var trib = ConclaveTribunalFactory.Create(_alice);
        trib.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(trib);

        var bobsCreature = new Creature("Death's Shadow", "{B}", 13, 13);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = trib.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
    }

    [Fact]
    public void ConclaveTribunal_Ltb_ReturnsExiledCardToBattlefield()
    {
        var trib = ConclaveTribunalFactory.Create(_alice);
        trib.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(trib);

        var bobsCreature = new Creature("Death's Shadow", "{B}", 13, 13);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = trib.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        var ltb = trib.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield);
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
    }

    [Fact]
    public void ConclaveTribunal_BuildAdditionalCost_BuildsConvokeCost()
    {
        var trib = ConclaveTribunalFactory.Create(_alice);

        // Build with an empty tap list — the cost shape just exists,
        // the per-tap reduction work is exercised by the
        // ConvokeAdditionalCost tests.
        var addCost = ConclaveTribunalFactory.BuildAdditionalCost(
            trib, Array.Empty<Creature>());

        addCost.Should().NotBeNull();
        addCost.Should().BeOfType<ConvokeAdditionalCost>();
    }
}
