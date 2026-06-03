using FluentAssertions;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>sacrifice_artifact</c> activation cost
/// (<see cref="SacrificeArtifactCostDef"/>, CR 117 / CR 701.16) — "Sacrifice an
/// artifact" as a non-mana activation cost. Routes through the pre-existing
/// <see cref="SacrificeAnArtifactCost"/> rail (the same one Arcbound Ravager /
/// Atog use). Sibling of the self-targeting <c>sacrifice_self</c> cost
/// (<see cref="SacrificeSelfCostDef"/>).
/// </summary>
public class JsonSacrificeArtifactCostTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature Atog()
    {
        var c = new Creature("Atog", "{1}{R}", 1, 2) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Artifact Trinket(string name)
    {
        var a = new Artifact(name, "{1}") { Owner = _alice, Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);
        return a;
    }

    [Fact]
    public void SacrificeArtifactCost_BuildsSacrificeAnArtifactRail()
    {
        var atog = Atog();
        var cost = new SacrificeArtifactCostDef().ToCost()(atog);
        cost.Should().BeOfType<SacrificeAnArtifactCost>();
    }

    [Fact]
    public void SacrificeArtifactCost_CanPay_WhenAnArtifactIsControlled()
    {
        var atog = Atog();
        Trinket("Ornithopter");

        var cost = new SacrificeArtifactCostDef().ToCost()(atog);
        cost.CanPay(_alice).Should().BeTrue("an artifact is on the battlefield (CR 117.3)");
    }

    [Fact]
    public void SacrificeArtifactCost_CannotPay_WhenNoArtifactControlled()
    {
        var atog = Atog(); // Atog is NOT an artifact

        var cost = new SacrificeArtifactCostDef().ToCost()(atog);
        cost.CanPay(_alice).Should().BeFalse("no artifact to sacrifice (CR 117.3)");
    }

    [Fact]
    public void SacrificeArtifactCost_Pay_MovesArtifactToGraveyard()
    {
        var atog = Atog();
        var trinket = Trinket("Ornithopter");

        var cost = new SacrificeArtifactCostDef().ToCost()(atog);
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().NotContain(trinket);
        _alice.Zones.Graveyard.GetCards().Should().Contain(trinket);
        trinket.Zone.Should().Be(ZoneType.Graveyard, "CR 701.16 — sacrificed to its owner's graveyard");
    }
}
