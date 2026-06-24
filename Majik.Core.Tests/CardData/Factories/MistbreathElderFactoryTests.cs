using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Mistbreath Elder (Bloomburrow, {G} — Creature — Frog Warrior 2/2).
///
///   "At the beginning of your upkeep, return another creature you control to
///    its owner's hand. If you do, put a +1/+1 counter on this creature.
///    Otherwise, you may return this creature to its owner's hand."
///
/// Covers the UNIQUE upkeep "if you do / otherwise" branch (CR 603.6e):
///   - Identity (Frog Warrior, {G}, 2/2, green).
///   - Trigger shape: exactly one upkeep <see cref="TriggeredAbility"/>.
///   - "if you do" branch: a controlled OTHER creature is bounced and this
///     creature gains a +1/+1 counter; the agent chooses WHICH other creature.
///   - "otherwise" branch (no other creature): the agent MAY return this
///     creature — accepts and declines, both clean.
/// </summary>
[Trait("Color", "G")]
public class MistbreathElderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature OtherCreature(Player owner, string name)
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature OnBattlefield(Player owner)
    {
        var elder = MistbreathElderFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);
        return elder;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MistbreathElder_Identity()
    {
        var c = MistbreathElderFactory.Create(_alice);

        c.Name.Should().Be("Mistbreath Elder");
        c.ManaCost.Should().Be("{G}");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MistbreathElder_IsGreenOnly()
    {
        var c = MistbreathElderFactory.Create(_alice);
        var colors = CardColors.GetColors(c);
        colors.Should().ContainSingle().Which.Should().Be(ManaColor.Green);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MistbreathElder_HasExactlyOneUpkeepTrigger()
    {
        var c = MistbreathElderFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // "If you do" branch — return another creature, gain +1/+1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void Upkeep_ReturnsAnotherCreature_AndGainsCounter()
    {
        var elder = OnBattlefield(_alice);
        var other = OtherCreature(_alice, "Grizzly Bears");

        MistbreathElderFactory.ResolveUpkeep(elder, zoneService: null);

        other.Zone.Should().Be(ZoneType.Hand, "the controlled other creature is returned");
        _alice.Zones.Hand.GetCards().Should().Contain(other);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(other);

        elder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "'if you do' puts a +1/+1 counter on this creature (CR 122.1c)");
        elder.Zone.Should().Be(ZoneType.Battlefield, "the source is NOT bounced in this branch");
    }

    [Fact]
    public void Upkeep_AgentChoosesWhichOtherCreatureToReturn()
    {
        var elder = OnBattlefield(_alice);
        var keep = OtherCreature(_alice, "Keep Me");
        var bounce = OtherCreature(_alice, "Bounce Me");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(bounce);
        AgentRegistry.Set(_alice, agent);
        try
        {
            MistbreathElderFactory.ResolveUpkeep(elder, zoneService: null);
        }
        finally { AgentRegistry.Clear(); }

        bounce.Zone.Should().Be(ZoneType.Hand, "the agent's chosen creature is returned");
        keep.Zone.Should().Be(ZoneType.Battlefield, "the unchosen creature stays");
        elder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // "Otherwise" branch — no other creature → MAY return this creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Upkeep_NoOtherCreature_MayReturnSelf_AgentAccepts()
    {
        var elder = OnBattlefield(_alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);
        try
        {
            MistbreathElderFactory.ResolveUpkeep(elder, zoneService: null);
        }
        finally { AgentRegistry.Clear(); }

        elder.Zone.Should().Be(ZoneType.Hand, "with no other creature, the 'may' bounce of self is taken");
        _alice.Zones.Hand.GetCards().Should().Contain(elder);
        elder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the 'if you do' counter does NOT apply in the otherwise branch (CR 603.6e)");
    }

    [Fact]
    public void Upkeep_NoOtherCreature_MayReturnSelf_AgentDeclines()
    {
        var elder = OnBattlefield(_alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);
        try
        {
            MistbreathElderFactory.ResolveUpkeep(elder, zoneService: null);
        }
        finally { AgentRegistry.Clear(); }

        elder.Zone.Should().Be(ZoneType.Battlefield, "declining the optional self-bounce is a clean no-op");
        elder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }
}
