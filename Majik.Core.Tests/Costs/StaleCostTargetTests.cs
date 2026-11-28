using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 701.16a / 400.7 — a sacrifice or discard cost pays with an object that
/// is where the cost needs it NOW. A cost object lives on its ability and is
/// paid again on every activation; the object it sacrificed last time has
/// since moved on (to the graveyard, then perhaps the library), so it must not
/// be "sacrificed" again. The full-game gate caught Arcbound Ravager doing
/// that to a Mox Opal that Endurance had put back in the library: the card
/// ended up in both the library and the graveyard.
/// </summary>
public class StaleCostTargetTests
{
    private readonly Player _alice = new("Alice", 20);

    private T OnBattlefield<T>(T card) where T : Card
    {
        card.SetOwner(_alice); card.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private void Move(Card card, ZoneType from, ZoneType to)
    {
        _alice.Zones.GetZone(from).RemoveCard(card);
        _alice.Zones.GetZone(to).AddCard(card);
        card.SetZone(to);
    }

    [Fact]
    public void SacrificeAnArtifact_PaidAgain_DoesNotReuseTheArtifactItSacrificedLastTime()
    {
        var opal = OnBattlefield(new Artifact("Mox Opal", "{0}"));
        var cost = new SacrificeAnArtifactCost();
        cost.Target = opal;
        cost.Pay(_alice);
        Move(opal, ZoneType.Graveyard, ZoneType.Library); // Endurance

        var bauble = OnBattlefield(new Artifact("Mishra's Bauble", "{0}"));
        cost.Pay(_alice);

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bauble);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(opal);
        opal.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void SacrificeAnotherCreature_PaidAgain_DoesNotReuseTheCreatureItSacrificedLastTime()
    {
        var source = OnBattlefield(new Creature("Carrion Feeder", "{B}", 1, 1));
        var bear = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2));
        var cost = new SacrificeAnotherCreatureCost(source);
        cost.Target = bear;
        cost.Pay(_alice);
        Move(bear, ZoneType.Graveyard, ZoneType.Hand);

        var elk = OnBattlefield(new Creature("Elk", "{1}{G}", 3, 3));
        cost.Pay(_alice);

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(elk);
        bear.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void ASacrifice_OfACardNotOnThePayersBattlefield_IsRefused()
    {
        var opal = new Artifact("Mox Opal", "{0}");
        opal.SetOwner(_alice); opal.SetController(_alice);
        _alice.Zones.Library.AddCard(opal); opal.SetZone(ZoneType.Library);
        var cost = new SacrificeAnArtifactCost { Target = opal };

        var act = () => cost.Pay(_alice);

        act.Should().Throw<InvalidOperationException>("only an artifact on the battlefield can be sacrificed (CR 701.16a)");
        opal.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
