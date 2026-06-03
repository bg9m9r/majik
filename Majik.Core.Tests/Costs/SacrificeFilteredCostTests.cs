using FluentAssertions;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for the generic <see cref="SacrificeFilteredCost"/> (CR 117 /
/// CR 701.16) — "Sacrifice a &lt;filtered&gt; permanent" as a non-mana
/// activation cost. Covers the two production filters that unblock cards:
/// "Sacrifice a token" (Fountainport) and "Sacrifice a Desert"
/// (Scavenger Grounds), plus the JSON <c>sacrifice_permanent</c>
/// <see cref="SacrificePermanentCostDef"/> build path.
/// </summary>
public class SacrificeFilteredCostTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land DesertLand(string name)
    {
        var l = new Land(name, subtypes: new[] { CardSubtype.Desert })
        { Owner = _alice, Controller = _alice };
        l.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(l);
        return l;
    }

    private Land PlainLand(string name)
    {
        var l = new Land(name) { Owner = _alice, Controller = _alice };
        l.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(l);
        return l;
    }

    private Creature TokenCreature(string name)
    {
        var c = new Creature(name, "", 1, 1) { Owner = _alice, Controller = _alice };
        c.MarkAsToken();
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Creature NontokenCreature(string name)
    {
        var c = new Creature(name, "{G}", 2, 2) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ---------------- token filter (Fountainport) ----------------

    [Fact]
    public void Token_CanPay_WhenATokenIsControlled()
    {
        TokenCreature("Fish");
        SacrificeFilteredCost.ForToken().CanPay(_alice)
            .Should().BeTrue("a token is on the battlefield (CR 111.8)");
    }

    [Fact]
    public void Token_CannotPay_WhenOnlyNontokensControlled()
    {
        NontokenCreature("Grizzly Bears");
        SacrificeFilteredCost.ForToken().CanPay(_alice)
            .Should().BeFalse("no token to sacrifice");
    }

    [Fact]
    public void Token_Pay_SacrificesTheToken_NotTheNontoken()
    {
        var bears = NontokenCreature("Grizzly Bears");
        var fish = TokenCreature("Fish");

        var cost = SacrificeFilteredCost.ForToken();
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(bears);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fish);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fish);
        fish.Zone.Should().Be(ZoneType.Graveyard, "CR 701.16 — sacrificed to its owner's graveyard");
        cost.Target.Should().Be(fish);
    }

    // ---------------- subtype filter (Scavenger Grounds) ----------------

    [Fact]
    public void Subtype_CanPay_WhenAMatchingPermanentControlled()
    {
        DesertLand("Scavenger Grounds");
        SacrificeFilteredCost.ForSubtype(CardSubtype.Desert).CanPay(_alice)
            .Should().BeTrue("a Desert is on the battlefield (CR 701.16)");
    }

    [Fact]
    public void Subtype_CannotPay_WhenNoMatchingSubtype()
    {
        PlainLand("Forest");
        SacrificeFilteredCost.ForSubtype(CardSubtype.Desert).CanPay(_alice)
            .Should().BeFalse("no Desert to sacrifice");
    }

    [Fact]
    public void Subtype_SourceCanSacrificeItself_WhenItMatchesFilter()
    {
        // CR 701.16 — Scavenger Grounds is itself a Desert, so it is a legal
        // sacrifice for its own "Sacrifice a Desert" cost.
        var grounds = DesertLand("Scavenger Grounds");
        var cost = SacrificeFilteredCost.ForSubtype(CardSubtype.Desert);
        cost.Pay(_alice);

        _alice.Zones.Graveyard.GetCards().Should().Contain(grounds);
        grounds.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Pay_HonoursPreSetTarget()
    {
        var a = DesertLand("Desert A");
        var b = DesertLand("Desert B");

        var cost = SacrificeFilteredCost.ForSubtype(CardSubtype.Desert);
        cost.Target = b;
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(a);
        _alice.Zones.Graveyard.GetCards().Should().Contain(b);
    }

    // ---------------- JSON sacrifice_permanent build path ----------------

    [Fact]
    public void Json_TokenVariant_BuildsFilteredCost()
    {
        var card = new Land("Fountainport") { Owner = _alice, Controller = _alice };
        var cost = new SacrificePermanentCostDef { Token = true }.ToCost()(card);
        cost.Should().BeOfType<SacrificeFilteredCost>();
        cost.Description.Should().Be("sacrifice a token");
    }

    [Fact]
    public void Json_SubtypeVariant_BuildsFilteredCost()
    {
        var card = new Land("Scavenger Grounds") { Owner = _alice, Controller = _alice };
        var cost = new SacrificePermanentCostDef { Subtype = "Desert" }.ToCost()(card);
        cost.Should().BeOfType<SacrificeFilteredCost>();
        cost.Description.Should().Be("sacrifice a Desert");
    }

    [Fact]
    public void Json_UnknownSubtype_Throws()
    {
        var card = new Land("X") { Owner = _alice, Controller = _alice };
        var act = () => new SacrificePermanentCostDef { Subtype = "Bogus" }.ToCost()(card);
        act.Should().Throw<NotSupportedException>();
    }
}
