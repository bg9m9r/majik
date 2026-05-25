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
/// Unit tests for <see cref="ArcboundStingerFactory"/>.
///
/// Card: Arcbound Stinger — Artifact Creature — Insect {2} 1/1 (Darksteel).
///   "Flying.
///    Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
/// </summary>
public class ArcboundStingerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    [Fact]
    public void ArcboundStinger_Identity()
    {
        var c = ArcboundStingerFactory.Create(_alice);

        c.Name.Should().Be("Arcbound Stinger");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArcboundStinger_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Arcbound Stinger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Arcbound Stinger");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Modular death trigger is attached at construction");
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Flying marker is attached");
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Modular 1").Should().BeTrue(
                "Modular 1 marker is attached");
    }

    [Fact]
    public void Modular_ReplacementBus_StampsEtbCounterIntent()
    {
        var bus = new ReplacementBus();
        var stinger = ArcboundStingerFactory.Create(_alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: stinger, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var rewritten = bus.Apply(intent);

        rewritten.Should().NotBeNull();
        rewritten!.PlusOneCountersOnEnter.Should().Be(ArcboundStingerFactory.ModularValue,
            "Modular 1 — replacement bus rewrites ETB intent to carry 1 +1/+1 counter");
    }

    [Fact]
    public void ModularDeathTrigger_MovesCounterToArtifactCreature()
    {
        var stinger = ArcboundStingerFactory.Create(_alice);
        PutOnBattlefield(_alice, stinger);
        ArcboundStingerFactory.MarkEntersWithCounter(stinger);

        var bestowee = new Creature("Test Artifact Creature", "{2}", 0, 0);
        bestowee.SetOwner(_alice);
        bestowee.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, bestowee);

        _alice.Zones.Battlefield.RemoveCard(stinger);
        _alice.Zones.Graveyard.AddCard(stinger);
        stinger.SetZone(ZoneType.Graveyard);

        var modular = stinger.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the single Modular-1 counter moves to the bestowee");
    }
}
