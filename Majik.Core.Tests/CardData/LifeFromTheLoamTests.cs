using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Life from the Loam (Ravnica: City of Guilds, {1}{G}).
///
/// Covers:
///   - Card identity (sorcery, mana cost, Green).
///   - NamedCardFactory dispatch.
///   - Dredge 3 keyword marker (CR 702.52) with Arg = 3.
///   - Lands-to-hand resolve body: up-to-three lands returned from
///     graveyard to hand; non-lands are filtered.
///   - Empty-graveyard / no-lands-in-graveyard = clean no-op.
///   - Custom selector path picks the supplied lands.
/// </summary>
public class LifeFromTheLoamTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Loam_Is_Sorcery_At_1G()
    {
        var loam = LifeFromTheLoamFactory.Create(_alice);

        loam.Name.Should().Be("Life from the Loam");
        loam.ManaCost.Should().Be("{1}{G}");
        loam.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Loam()
    {
        var card = NamedCardFactory.Create("Life from the Loam", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Life from the Loam");
    }

    [Fact]
    public void Loam_HasDredge3Marker()
    {
        var loam = LifeFromTheLoamFactory.Create(_alice);

        loam.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge")
            .Which.Arg.Should().Be(3);
    }

    [Fact]
    public void Loam_BuildResolveEffect_DefaultPicksFirstThreeLands()
    {
        // Stage 4 lands + 1 non-land in graveyard.
        var lands = new List<Land>();
        for (int i = 0; i < 4; i++)
        {
            var l = new Land($"Forest-{i}");
            l.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(l);
            l.SetZone(ZoneType.Graveyard);
            lands.Add(l);
        }
        var sorcery = new Sorcery("Test", "{1}");
        sorcery.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Graveyard);

        var effects = LifeFromTheLoamFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(3,
            "default selector returns up to three lands");
        _alice.Zones.Hand.GetCards().Should().OnlyContain(c => c.HasType(CardType.Land));
        _alice.Zones.Graveyard.GetCards().Should().Contain(sorcery,
            "non-lands are filtered and remain in graveyard");
    }

    [Fact]
    public void Loam_BuildResolveEffect_EmptyGraveyard_NoOp()
    {
        var effects = LifeFromTheLoamFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(0);
    }

    [Fact]
    public void Loam_BuildResolveEffect_CustomSelector_PicksSuppliedLands()
    {
        var l1 = new Land("Forest-1"); l1.SetOwner(_alice);
        var l2 = new Land("Forest-2"); l2.SetOwner(_alice);
        var l3 = new Land("Forest-3"); l3.SetOwner(_alice);
        var l4 = new Land("Forest-4"); l4.SetOwner(_alice);
        foreach (var l in new[] { l1, l2, l3, l4 })
        {
            _alice.Zones.Graveyard.AddCard(l);
            l.SetZone(ZoneType.Graveyard);
        }

        var effects = LifeFromTheLoamFactory.BuildResolveEffect(
            _alice,
            landSelector: _ => new ICard[] { l2, l4 });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(new ICard[] { l2, l4 });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new ICard[] { l1, l3 });
    }
}
