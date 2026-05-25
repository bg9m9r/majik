using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EtchedOracleFactory"/> (Fifth Dawn, {4}).
///
/// Covers:
/// - Identity (Artifact Creature — Human Wizard, {4} 1/1, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Sunburst ETB lands +1/+1 counters (CR 702.44a — creature branch).
/// - {2}, Remove three +1/+1 counters: each player draws three cards.
/// </summary>
public class EtchedOracleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler-{p.Name}-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_Identity()
    {
        var eo = EtchedOracleFactory.Create(_alice);

        eo.Name.Should().Be("Etched Oracle");
        eo.ManaCost.Should().Be("{4}");
        eo.HasType(CardType.Artifact).Should().BeTrue();
        eo.HasType(CardType.Creature).Should().BeTrue();
        eo.Subtypes.Should().Contain(CardSubtype.Human);
        eo.Subtypes.Should().Contain(CardSubtype.Wizard);
        eo.BasePower.Should().Be(1);
        eo.BaseToughness.Should().Be(1);
        eo.Owner.Should().BeSameAs(_alice);
        eo.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EtchedOracle_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Etched Oracle", _alice);

        card.Should().BeOfType<Creature>("Etched Oracle is an Artifact Creature");
        card.Name.Should().Be("Etched Oracle");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Sunburst",
                "Sunburst keyword marker surfaced");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sunburst ETB trigger is surfaced for shape");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}, remove three +1/+1 counters: each player draws 3 is surfaced");
    }

    // -----------------------------------------------------------------------
    // Sunburst ETB (CR 702.44a — creature branch)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_EtbWithThreeColorsPaid_AddsThreePlusOnePlusOneCounters()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);

        eo.SetPendingCastColors(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Red,
        });

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "three colors of mana spent → three +1/+1 counters (CR 702.44a)");
    }

    [Fact]
    public void EtchedOracle_EtbWithZeroColors_AddsNoCounters()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.SetPendingCastColors(Array.Empty<ManaColor>());

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {2}, remove three +1/+1 counters: each player
    // draws three cards.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_Activated_RemovesCounters_AndDrawsForController()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.Counters.Add(CounterType.PlusOnePlusOne, 3);

        SeedLibrary(_alice, 10);

        var aliceHandBefore = _alice.Zones.Hand.GetCards().Count();

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "three +1/+1 counters were the activation cost");
        (_alice.Zones.Hand.GetCards().Count() - aliceHandBefore).Should().Be(3,
            "controller draws three cards");
    }

    [Fact]
    public void EtchedOracle_Activated_WithAllPlayersResolver_DrawsForEveryPlayer()
    {
        var eo = EtchedOracleFactory.Create(
            _alice,
            replacements: null,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.Counters.Add(CounterType.PlusOnePlusOne, 3);

        SeedLibrary(_alice, 10);
        SeedLibrary(_bob, 10);

        var aliceBefore = _alice.Zones.Hand.GetCards().Count();
        var bobBefore = _bob.Zones.Hand.GetCards().Count();

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        (_alice.Zones.Hand.GetCards().Count() - aliceBefore).Should().Be(3,
            "Alice draws three");
        (_bob.Zones.Hand.GetCards().Count() - bobBefore).Should().Be(3,
            "Bob also draws three");
    }

    [Fact]
    public void EtchedOracle_Activated_InsufficientCounters_IsCleanNoOp()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.Counters.Add(CounterType.PlusOnePlusOne, 2);
        SeedLibrary(_alice, 10);

        var aliceBefore = _alice.Zones.Hand.GetCards().Count();

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "insufficient counters → no removal");
        (_alice.Zones.Hand.GetCards().Count() - aliceBefore).Should().Be(0,
            "insufficient counters → no draw");
    }
}
