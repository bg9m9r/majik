using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FelidarGuardianFactory"/>.
///
/// Covers:
/// - Identity (Creature, 1/4, Cat Beast, {2}{W}, Flash marker).
/// - NamedCardFactory dispatch.
/// - ETB triggered ability shape — 0..1 "another target permanent you
///   control", Protection intent.
/// - Resolve: exiles + immediately returns the targeted permanent
///   (CR 701.21 + CR 614). Any permanent type qualifies — exercised
///   against a creature target and a Land target.
/// - Resolve: opponent-controlled target fizzles (CR 608.2b).
/// - Resolve: zero-target "may" branch is a clean no-op.
/// </summary>
[Trait("Color", "W")]
public class FelidarGuardianFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FelidarGuardian_HasCorrectShape()
    {
        var c = FelidarGuardianFactory.Create(_alice);

        c.Name.Should().Be("Felidar Guardian");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain("Flash");
    }
    [Fact]
    public void FelidarGuardian_HasEtbTriggerWithUpToOneTarget()
    {
        var c = FelidarGuardianFactory.Create(_alice);

        var triggered = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(1);

        var etb = triggered[0];
        etb.TargetRequests.Should().HaveCount(1);
        var tr = etb.TargetRequests[0];
        tr.MinTargets.Should().Be(0, "'may' rider");
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("permanent");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile-then-return
    // -----------------------------------------------------------------------

    [Fact]
    public void FelidarGuardian_Resolve_FlickersTargetedCreature()
    {
        var fel = NewControlledFelidarOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        SetEtbTargets(fel, new object[] { bear });
        FireEtbEffect(fel);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — Felidar Guardian returns the exiled permanent in the same resolution");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FelidarGuardian_Resolve_FlickersTargetedLand()
    {
        // "target permanent you control" — any permanent type works.
        var fel = NewControlledFelidarOnBattlefield(_alice);
        var land = new Land("Plains");
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SetEtbTargets(fel, new object[] { land });
        FireEtbEffect(fel);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "land returns to the battlefield — CR 614");
        _alice.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    [Fact]
    public void FelidarGuardian_Resolve_OpponentControlledTarget_Fizzles()
    {
        var fel = NewControlledFelidarOnBattlefield(_alice);
        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        SetEtbTargets(fel, new object[] { bobBear });
        FireEtbEffect(fel);

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target violates 'you control' → CR 608.2b no-effect");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
    }

    [Fact]
    public void FelidarGuardian_Resolve_NoTargetChosen_DeclineMay_NoOp()
    {
        var fel = NewControlledFelidarOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        SetEtbTargets(fel, Array.Empty<object>());
        FireEtbEffect(fel);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewControlledFelidarOnBattlefield(Player owner)
    {
        var fel = FelidarGuardianFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(fel);
        fel.SetZone(ZoneType.Battlefield);
        return fel;
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private static TriggeredAbility EtbTrigger(Creature fel) =>
        fel.Abilities.OfType<TriggeredAbility>().First(t => t.TargetRequests.Count > 0);

    private static void SetEtbTargets(Creature fel, IReadOnlyList<object> targets)
    {
        EtbTrigger(fel).SetChosenTargets(new[] { targets });
    }

    private static void FireEtbEffect(Creature fel)
    {
        foreach (var eff in EtbTrigger(fel).Effects)
        {
            eff.Execute();
        }
    }
}
