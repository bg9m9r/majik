using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for CR 702.44 — Sunburst, implemented via
/// <see cref="SunburstFactory"/>.
///
/// Covers:
///   - Keyword marker attached with the literal "Sunburst" string.
///   - ETB +1/+1 counters for creature permanents (CR 702.44a).
///   - ETB charge counters for non-creature artifact permanents
///     (CR 702.44a).
///   - Counter count = distinct colors in <see cref="Card.PendingCastColors"/>.
///   - Zero / null colors → zero counters (CR 702.44b).
///   - PendingCastColors stamp is consumed after the ETB effect runs.
/// </summary>
public class SunburstTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeSunburstCreature(string name, Player owner,
        ReplacementBus? replacements = null)
    {
        var c = new Creature(name, "{4}", 1, 1);
        c.AddCardType(CardType.Artifact);
        c.SetOwner(owner);
        c.SetController(owner);
        SunburstFactory.Build(c, replacements);
        return c;
    }

    private static Artifact MakeSunburstArtifact(string name, Player owner,
        ReplacementBus? replacements = null)
    {
        var a = new Artifact(name, "{4}");
        a.SetOwner(owner);
        a.SetController(owner);
        SunburstFactory.Build(a, replacements);
        return a;
    }

    // -------------------------------------------------------------------------
    // Keyword marker
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_AttachesKeywordMarker()
    {
        var oracle = MakeSunburstCreature("Test Sunburst Creature", _alice);

        var marker = oracle.Abilities.OfType<KeywordAbility>().SingleOrDefault();
        marker.Should().NotBeNull("Sunburst ships a KeywordAbility marker");
        marker!.Keyword.Should().Be("Sunburst");
    }

    [Fact]
    public void Build_AttachesEtbTriggeredAbility()
    {
        var oracle = MakeSunburstCreature("Test Sunburst Creature", _alice);

        oracle.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sunburst surfaces a single ETB triggered ability");
    }

    // -------------------------------------------------------------------------
    // ETB counters — creature branch (CR 702.44a)
    // -------------------------------------------------------------------------

    [Fact]
    public void Creature_CastWithZeroColors_EntersWithZeroCounters()
    {
        var c = MakeSunburstCreature("Sunburst Creature", _alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.SetPendingCastColors(Array.Empty<ManaColor>());

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 702.44b — zero colored mana spent → zero Sunburst counters");
        c.Counters.Count(CounterType.Charge).Should().Be(0);
        c.PendingCastColors.Should().BeNull(
            "the ETB effect consumes + clears the stamp");
    }

    [Fact]
    public void Creature_CastWithThreeColors_EntersWithThreePlusOnePlusOneCounters()
    {
        var c = MakeSunburstCreature("Sunburst Creature", _alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.SetPendingCastColors(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Red,
        });

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "CR 702.44a — creature branch lands +1/+1 counters");
        c.Counters.Count(CounterType.Charge).Should().Be(0);
        c.PendingCastColors.Should().BeNull();
    }

    [Fact]
    public void Creature_CastWithFiveColors_EntersWithFivePlusOnePlusOneCounters()
    {
        var c = MakeSunburstCreature("Sunburst Creature", _alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.SetPendingCastColors(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        });

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(5,
            "all five colors → five +1/+1 counters");
    }

    [Fact]
    public void Creature_NullPendingCastColors_EntersWithZeroCounters()
    {
        // Non-cast entry (blink, reanimation) leaves PendingCastColors
        // null — CR 702.44b says Sunburst only adds counters from the
        // stack as a resolving spell.
        var c = MakeSunburstCreature("Sunburst Creature", _alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        c.PendingCastColors.Should().BeNull("non-cast entry");

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-cast battlefield entry → zero Sunburst counters");
    }

    // -------------------------------------------------------------------------
    // ETB counters — non-creature artifact branch (CR 702.44a)
    // -------------------------------------------------------------------------

    [Fact]
    public void NonCreatureArtifact_CastWithTwoColors_EntersWithTwoChargeCounters()
    {
        var a = MakeSunburstArtifact("Sunburst Artifact", _alice);
        _alice.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        a.SetPendingCastColors(new[] { ManaColor.Green, ManaColor.White });

        var etb = a.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        a.Counters.Count(CounterType.Charge).Should().Be(2,
            "CR 702.44a — non-creature branch lands charge counters");
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void NonCreatureArtifact_CastWithZeroColors_EntersWithZeroCharge()
    {
        var a = MakeSunburstArtifact("Sunburst Artifact", _alice);
        _alice.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        a.SetPendingCastColors(Array.Empty<ManaColor>());

        var etb = a.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        a.Counters.Count(CounterType.Charge).Should().Be(0,
            "no colored mana spent → no charge counters per CR 702.44b");
    }
}
