using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 702.169 — Bargain. Reusable <see cref="BargainAdditionalCost"/>: optional
/// "sacrifice an artifact, enchantment, or token as you cast this" additional
/// cost that stamps the spell's <see cref="Card.WasBargained"/> rider.
/// </summary>
public class BargainAdditionalCostTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Artifact Artifact(Player owner, string name = "Treasure")
    {
        var a = new Artifact(name, "") { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    [Fact]
    public void CanPay_True_WhenControllingAnArtifact()
    {
        Artifact(_alice);
        var spell = new Sorcery("Pitiless Carnage", "3B") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_True_WhenControllingAnEnchantment()
    {
        var ench = new Enchantment("Aura", "") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var spell = new Sorcery("Back for Seconds", "2B") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_True_WhenControllingAToken()
    {
        var token = new Creature("Goblin", "", 1, 1) { Owner = _alice, Controller = _alice, IsToken = true };
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var spell = new Sorcery("Thunderous Debut", "6GG") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        cost.CanPay(_alice).Should().BeTrue("a token is bargainable even if it's a creature token");
    }

    [Fact]
    public void CanPay_False_WhenNothingBargainable()
    {
        // A nontoken creature is NOT bargainable (only artifact/enchantment/token).
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var spell = new Sorcery("Pitiless Carnage", "3B") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_SacrificesAndStampsBargained()
    {
        var artifact = Artifact(_alice);
        var spell = new Sorcery("Pitiless Carnage", "3B") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        spell.WasBargained.Should().BeFalse();
        cost.Pay(_alice).Should().BeTrue();

        artifact.Zone.Should().Be(ZoneType.Graveyard, "the bargained artifact is sacrificed");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(artifact);
        cost.Sacrificed.Should().BeSameAs(artifact);
        spell.WasBargained.Should().BeTrue("the spell is stamped so its 'if bargained' rider fires");
    }

    [Fact]
    public void Pay_False_WhenNothingToSacrifice_DoesNotStamp()
    {
        var spell = new Sorcery("Pitiless Carnage", "3B") { Owner = _alice, Zone = ZoneType.Stack };
        var cost = new BargainAdditionalCost(spell);

        cost.Pay(_alice).Should().BeFalse();
        spell.WasBargained.Should().BeFalse();
    }

    [Fact]
    public void ClearWasBargained_ResetsTheSentinel()
    {
        var spell = new Sorcery("Pitiless Carnage", "3B") { Owner = _alice };
        spell.SetWasBargained(true);
        spell.WasBargained.Should().BeTrue();

        spell.ClearWasBargained();
        spell.WasBargained.Should().BeFalse("CR 400.7 — the sentinel clears after resolution");
    }
}
