using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the Lost Caverns of Ixalan explore-token creatures:
/// Cenote Scout ({G} 1/1, ETB explore), Spyglass Siren ({U} 1/1 Flying, ETB
/// make a Map) and Sentinel of the Nameless City ({2}{G} 3/4 Vigilance,
/// enters-or-attacks make a Map). CR 701.40 / CR 111.10.
/// </summary>
public class MapTokenCreaturesTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private static void ExecuteEtb(Creature card) =>
        ExecuteTrigger(card.Abilities.OfType<TriggeredAbility>().First());

    private static void ExecuteTrigger(TriggeredAbility trigger)
    {
        foreach (var effect in trigger.Effects) effect.Execute();
    }

    // ── Cenote Scout — ETB explore ────────────────────────────────────────

    [Fact]
    public void CenoteScout_Identity()
    {
        var c = CenoteScoutFactory.Create(_alice);
        c.Name.Should().Be("Cenote Scout");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.ManaCost.Should().Be("{G}");
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle("the ETB explore trigger");
    }

    [Fact]
    public void CenoteScout_Etb_NonLandOnTop_CounterOnSelf()
    {
        var spell = new Creature("Big", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(spell);
        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        AgentRegistry.Set(_alice, agent);

        var scout = CenoteScoutFactory.Create(_alice);
        ExecuteEtb(scout);

        scout.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter lands on the exploring creature (itself)");
    }

    [Fact]
    public void CenoteScout_Etb_LandOnTop_GoesToHand()
    {
        var land = new Land("Forest");
        _alice.Zones.Library.AddCard(land);

        var scout = CenoteScoutFactory.Create(_alice);
        ExecuteEtb(scout);

        _alice.Zones.Hand.GetCards().Should().Contain(land);
        scout.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // ── Spyglass Siren — Flying + ETB make a Map ──────────────────────────

    [Fact]
    public void SpyglassSiren_Identity_Flying()
    {
        var c = SpyglassSirenFactory.Create(_alice);
        c.Name.Should().Be("Spyglass Siren");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.ManaCost.Should().Be("{U}");
        c.HasSubtype(CardSubtype.Siren).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        CombatAbilities.HasFlying(c).Should().BeTrue("CR 702.9 — Flying");
        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle("the ETB make-a-Map trigger");
    }

    [Fact]
    public void SpyglassSiren_Etb_CreatesOneMapToken()
    {
        var siren = SpyglassSirenFactory.Create(_alice);
        ExecuteEtb(siren);

        var maps = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.Name == "Map").ToList();
        maps.Should().ContainSingle("CR 111.10 — one Map token is created on ETB");
        maps[0].HasType(CardType.Artifact).Should().BeTrue();
        maps[0].HasSubtype(CardSubtype.Map).Should().BeTrue();
    }

    // ── Sentinel of the Nameless City — Vigilance + enters/attacks Map ─────

    [Fact]
    public void Sentinel_Identity_Vigilance_TwoTriggers()
    {
        var c = SentinelOfTheNamelessCityFactory.Create(_alice);
        c.Name.Should().Be("Sentinel of the Nameless City");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{2}{G}");
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        CombatAbilities.HasVigilance(c).Should().BeTrue("CR 702.20 — Vigilance");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one enters trigger + one attacks trigger");
    }

    [Fact]
    public void Sentinel_EtbTrigger_CreatesOneMap()
    {
        var sentinel = SentinelOfTheNamelessCityFactory.Create(_alice);
        ExecuteEtb(sentinel);

        _alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map").Should()
            .Be(1, "CR 111.10 — the enters trigger makes one Map");
    }

    [Fact]
    public void Sentinel_BothTriggers_CreateTwoMaps()
    {
        var sentinel = SentinelOfTheNamelessCityFactory.Create(_alice);
        foreach (var t in sentinel.Abilities.OfType<TriggeredAbility>())
        {
            ExecuteTrigger(t);
        }

        _alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map").Should()
            .Be(2, "enters + attacks each make a Map");
    }
}
