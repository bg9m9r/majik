using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FesteringMummyFactory"/>
/// (Amonkhet, {B}).
///
/// Creature — Zombie 1/1. Oracle text:
///   "When this creature dies, you may put a -1/-1 counter on target
///    creature."
///
/// Covers:
///   - Identity (name, cost, P/T, Zombie subtype, mono-black, owner /
///     controller).
///   - Dies trigger shape (single TargetRequest for "target creature",
///     active zones include Battlefield + Graveyard).
///   - Resolution stamps a -1/-1 counter on the agent-chosen target.
///   - No-target / deterministic-opponent fallback paths.
///
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests — not re-tested here.)
/// </summary>
[Trait("Color", "B")]
public class FesteringMummyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void FesteringMummy_Identity()
    {
        var c = FesteringMummyFactory.Create(_alice);

        c.Name.Should().Be("Festering Mummy");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Festering Mummy is a plain mono-black Creature, not an Artifact");
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // Dies trigger shape
    // -------------------------------------------------------------------------

    [Fact]
    public void FesteringMummy_HasOneDiesTrigger()
    {
        var c = FesteringMummyFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Festering Mummy prints one triggered ability: dies → may -1/-1 counter");
    }

    [Fact]
    public void FesteringMummy_DiesTrigger_HasSingleTargetCreatureRequest()
    {
        var c = FesteringMummyFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    [Fact]
    public void FesteringMummy_DiesTrigger_ActiveZones_IncludeBattlefieldAndGraveyard()
    {
        // The trigger source is Festering Mummy itself; at resolution time its
        // zone is Graveyard (it just died). active-zones must include Graveyard
        // so the trigger's zone-guard passes post-move (mirrors Necropede /
        // Persist / Nihil Spellbomb).
        var c = FesteringMummyFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }

    // -------------------------------------------------------------------------
    // Dies trigger resolution — counter placement
    // -------------------------------------------------------------------------

    [Fact]
    public void FesteringMummy_DiesTrigger_StampsMinusOneMinusOneOnChosenTarget()
    {
        var mummy = FesteringMummyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mummy);
        mummy.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Test Victim", "{2}", 3, 3);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var trigger = mummy.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { victim },
        });

        // Simulate the Mummy dying.
        _alice.Zones.Battlefield.RemoveCard(mummy);
        _alice.Zones.Graveyard.AddCard(mummy);
        mummy.SetZone(ZoneType.Graveyard);

        foreach (var e in trigger.Effects) e.Execute();

        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "CR 122 — a -1/-1 counter is placed on the chosen creature");
    }

    [Fact]
    public void FesteringMummy_DiesTrigger_NoLegalTarget_NoOp()
    {
        var mummy = FesteringMummyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mummy);
        mummy.SetZone(ZoneType.Battlefield);

        _alice.Zones.Battlefield.RemoveCard(mummy);
        _alice.Zones.Graveyard.AddCard(mummy);
        mummy.SetZone(ZoneType.Graveyard);

        var trigger = mummy.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void FesteringMummy_DiesTrigger_FallsBackToOpponentCreatureWhenNoAgentChoice()
    {
        // No agent-chosen target is set; the deterministic fallback picks the
        // first opponent-controlled creature on the controller's local view.
        var mummy = FesteringMummyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mummy);
        mummy.SetZone(ZoneType.Battlefield);

        var stolen = new Creature("Stolen Bear", "{1}{G}", 2, 2);
        stolen.SetOwner(_bob);
        stolen.SetController(_bob);
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        _alice.Zones.Battlefield.RemoveCard(mummy);
        _alice.Zones.Graveyard.AddCard(mummy);
        mummy.SetZone(ZoneType.Graveyard);

        var trigger = mummy.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stolen.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "fallback picks the first opponent-controlled creature on the " +
            "controller's local battlefield");
    }
}
