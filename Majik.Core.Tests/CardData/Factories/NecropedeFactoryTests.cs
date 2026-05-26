using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NecropedeFactory"/>
/// (Scars of Mirrodin, {2}).
///
/// Artifact Creature — Insect 1/1. Oracle text:
///   "Infect
///    When Necropede dies, you may put a -1/-1 counter on target
///    creature."
///
/// Covers:
///   - Identity (name, cost, P/T, dual Artifact + Creature, subtype
///     Insect, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Infect keyword marker.
///   - Dies trigger shape (single TargetRequest for "target creature",
///     active zones include Battlefield + Graveyard).
///   - Resolution stamps a -1/-1 counter on the agent-chosen target.
///   - No-target / deterministic-opponent fallback paths.
/// </summary>
public class NecropedeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Necropede_Identity()
    {
        var c = NecropedeFactory.Create(_alice);

        c.Name.Should().Be("Necropede");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Necropede_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Necropede", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Necropede");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Infect
    // -------------------------------------------------------------------------

    [Fact]
    public void Necropede_HasInfectKeywordMarker()
    {
        var c = NecropedeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");
    }

    // -------------------------------------------------------------------------
    // Dies trigger shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Necropede_HasOneDiesTrigger()
    {
        var c = NecropedeFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Necropede prints one triggered ability: dies → may -1/-1 counter");
    }

    [Fact]
    public void Necropede_DiesTrigger_HasSingleTargetCreatureRequest()
    {
        var c = NecropedeFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    [Fact]
    public void Necropede_DiesTrigger_ActiveZones_IncludeBattlefieldAndGraveyard()
    {
        // The trigger source is Necropede itself; at resolution time its zone
        // is Graveyard (it just died). active-zones must include Graveyard so
        // the trigger's zone-guard passes post-move (mirrors Modular / Persist
        // / Nihil Spellbomb).
        var c = NecropedeFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }

    // -------------------------------------------------------------------------
    // Dies trigger resolution — counter placement
    // -------------------------------------------------------------------------

    [Fact]
    public void Necropede_DiesTrigger_StampsMinusOneMinusOneOnChosenTarget()
    {
        // Alice has Necropede + a victim creature on her battlefield; the
        // agent picks the victim as the death-trigger target.
        var necropede = NecropedeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(necropede);
        necropede.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Test Victim", "{2}", 3, 3);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var trigger = necropede.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { victim },
        });

        // Simulate Necropede dying.
        _alice.Zones.Battlefield.RemoveCard(necropede);
        _alice.Zones.Graveyard.AddCard(necropede);
        necropede.SetZone(ZoneType.Graveyard);

        foreach (var e in trigger.Effects) e.Execute();

        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "CR 122 — a -1/-1 counter is placed on the chosen creature");
    }

    [Fact]
    public void Necropede_DiesTrigger_NoLegalTarget_NoOp()
    {
        // Necropede dies but no other creature exists — the trigger resolves
        // as a no-op.
        var necropede = NecropedeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(necropede);
        necropede.SetZone(ZoneType.Battlefield);

        _alice.Zones.Battlefield.RemoveCard(necropede);
        _alice.Zones.Graveyard.AddCard(necropede);
        necropede.SetZone(ZoneType.Graveyard);

        var trigger = necropede.Abilities.OfType<TriggeredAbility>().Single();

        // Should not throw; no creature exists to receive a counter.
        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Necropede_DiesTrigger_FallsBackToOpponentCreatureWhenNoAgentChoice()
    {
        // No agent-chosen target is set; the deterministic fallback picks the
        // first opponent-controlled creature on the controller's local view.
        // (Alice owns Necropede; Alice's battlefield contains Necropede plus a
        // creature she stole that Bob controls — fallback should pick that.)
        var necropede = NecropedeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(necropede);
        necropede.SetZone(ZoneType.Battlefield);

        var stolen = new Creature("Stolen Bear", "{1}{G}", 2, 2);
        stolen.SetOwner(_bob);
        stolen.SetController(_bob);
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        // Simulate death.
        _alice.Zones.Battlefield.RemoveCard(necropede);
        _alice.Zones.Graveyard.AddCard(necropede);
        necropede.SetZone(ZoneType.Graveyard);

        var trigger = necropede.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stolen.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "fallback picks the first opponent-controlled creature on the " +
            "controller's local battlefield");
    }
}
