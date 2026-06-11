using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Jadelight Ranger ({1}{G}, Creature — Merfolk Scout Ranger 2/1),
/// the "explores, then it explores again" ETB-double-explore card (CR 701.40).
/// Wired through the shared <see cref="ExploreEtb"/> helper with
/// <c>exploreCount: 2</c> — the same primitive the single-explore ETB family
/// (Seekers' Squire / Merfolk Branchwalker) runs, repeated twice.
/// </summary>
public class JadelightRangerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    [Fact]
    public void JadelightRanger_Identity()
    {
        var c = JadelightRangerFactory.Create(_alice);
        c.Name.Should().Be("Jadelight Ranger");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1, "the ETB double-explore trigger");
    }

    [Fact]
    public void JadelightRanger_Etb_TwoNonLands_TwoCountersOnSelf()
    {
        // Two non-land cards on top → each explore places a +1/+1 counter on
        // Jadelight Ranger itself (CR 701.40c), so it ends a 4/3.
        var top = new Creature("First", "{G}", 3, 3);
        var second = new Creature("Second", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(second);
        _alice.Zones.Library.AddCard(top); // FirstOrDefault() => last added on top

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        agent.QueueExploreKeepOnTop(true);
        AgentRegistry.Set(_alice, agent);

        var ranger = JadelightRangerFactory.Create(_alice);
        ExecuteEtb(ranger);

        ranger.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "CR 701.40 — Jadelight Ranger explores twice; two non-land reveals = two +1/+1 counters");
    }

    [Fact]
    public void JadelightRanger_Etb_TwoLands_BothToHand_NoCounters()
    {
        var land1 = new Land("Forest");
        var land2 = new Land("Island");
        _alice.Zones.Library.AddCard(land2);
        _alice.Zones.Library.AddCard(land1); // land1 on top

        var ranger = JadelightRangerFactory.Create(_alice);
        ExecuteEtb(ranger);

        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { land1, land2 },
            "CR 701.40b — both revealed lands go to hand across the two explores");
        ranger.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.40b — a revealed land places no +1/+1 counter");
    }

    [Fact]
    public void JadelightRanger_Etb_EmptyLibrary_TwoCounters_NoCrash()
    {
        var ranger = JadelightRangerFactory.Create(_alice);

        var act = () => ExecuteEtb(ranger);

        act.Should().NotThrow("CR 701.40d — an empty library still places a counter per explore");
        ranger.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "two explores, empty library = two +1/+1 counters (CR 701.40d)");
    }

    private static void ExecuteEtb(Creature card)
    {
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();
    }
}
