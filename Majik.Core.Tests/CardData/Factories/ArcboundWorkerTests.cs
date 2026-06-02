using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArcboundWorkerFactory"/>.
///
/// Card: Arcbound Worker — Artifact Creature — Construct {1} 0/0 (Darksteel).
///   "Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
/// </summary>
[Trait("Color", "C")]
public class ArcboundWorkerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    [Fact]
    public void ArcboundWorker_Identity()
    {
        var c = ArcboundWorkerFactory.Create(_alice);

        c.Name.Should().Be("Arcbound Worker");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(0);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Modular_ReplacementBus_StampsEtbCounterIntent()
    {
        var bus = new ReplacementBus();
        var worker = ArcboundWorkerFactory.Create(_alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: worker, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var rewritten = bus.Apply(intent);

        rewritten.Should().NotBeNull();
        rewritten!.PlusOneCountersOnEnter.Should().Be(ArcboundWorkerFactory.ModularValue,
            "Modular 1 — replacement bus rewrites ETB intent to carry 1 +1/+1 counter");
    }

    [Fact]
    public void ModularDeathTrigger_MovesCounterToArtifactCreature()
    {
        var worker = ArcboundWorkerFactory.Create(_alice);
        PutOnBattlefield(_alice, worker);
        ArcboundWorkerFactory.MarkEntersWithCounter(worker);

        var bestowee = new Creature("Test Artifact Creature", "{2}", 0, 0);
        bestowee.SetOwner(_alice);
        bestowee.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, bestowee);

        _alice.Zones.Battlefield.RemoveCard(worker);
        _alice.Zones.Graveyard.AddCard(worker);
        worker.SetZone(ZoneType.Graveyard);

        var modular = worker.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the single Modular-1 counter moves to the bestowee");
    }
}
