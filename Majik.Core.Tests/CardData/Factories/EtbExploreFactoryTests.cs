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
/// Tests for the "When this creature enters, it explores" ETB-explore
/// factories (CR 701.40): Seekers' Squire ({1}{B} 1/2 Human Scout) and
/// Merfolk Branchwalker ({1}{G} 2/1 Merfolk Scout), both wired through the
/// shared <see cref="ExploreEtb"/> helper.
/// </summary>
public class EtbExploreFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeekersSquire_Identity()
    {
        var c = SeekersSquireFactory.Create(_alice);
        c.Name.Should().Be("Seekers' Squire");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{B}");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1, "the ETB explore trigger");
    }

    [Fact]
    public void MerfolkBranchwalker_Identity()
    {
        var c = MerfolkBranchwalkerFactory.Create(_alice);
        c.Name.Should().Be("Merfolk Branchwalker");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1, "the ETB explore trigger");
    }

    // -----------------------------------------------------------------------
    // ETB explore — land reveal goes to hand, no counter on the explorer.
    // -----------------------------------------------------------------------

    [Fact]
    public void SeekersSquire_Etb_LandOnTop_GoesToHand_NoCounter()
    {
        var land = new Land("Swamp");
        _alice.Zones.Library.AddCard(land);

        var squire = SeekersSquireFactory.Create(_alice);
        ExecuteEtb(squire);

        _alice.Zones.Hand.GetCards().Should().Contain(land);
        squire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.40b — a revealed land places no +1/+1 counter");
    }

    // -----------------------------------------------------------------------
    // ETB explore — non-land reveal lands the +1/+1 counter on the explorer.
    // -----------------------------------------------------------------------

    [Fact]
    public void MerfolkBranchwalker_Etb_NonLandOnTop_CounterOnSelf_KeepOnTop()
    {
        var spell = new Creature("Spell", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        AgentRegistry.Set(_alice, agent);

        var branchwalker = MerfolkBranchwalkerFactory.Create(_alice);
        ExecuteEtb(branchwalker);

        branchwalker.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter goes on the exploring permanent (itself)");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell,
            "the agent kept the revealed card on top");
    }

    [Fact]
    public void SeekersSquire_Etb_NonLandOnTop_Graveyard()
    {
        var spell = new Creature("Spell", "{B}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(false);
        AgentRegistry.Set(_alice, agent);

        var squire = SeekersSquireFactory.Create(_alice);
        ExecuteEtb(squire);

        squire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(spell,
            "the agent put the revealed card into the graveyard");
    }

    // -----------------------------------------------------------------------
    // ETB explore — empty library is a clean counter-only no-crash.
    // -----------------------------------------------------------------------

    [Fact]
    public void SeekersSquire_Etb_EmptyLibrary_CounterOnly_NoCrash()
    {
        var squire = SeekersSquireFactory.Create(_alice);

        var act = () => ExecuteEtb(squire);

        act.Should().NotThrow("CR 701.40d — an empty library still places the counter");
        squire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    private static void ExecuteEtb(Creature card)
    {
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();
    }
}
