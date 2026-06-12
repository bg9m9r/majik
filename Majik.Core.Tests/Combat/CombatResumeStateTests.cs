using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

public class CombatResumeStateTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature AddBear(Player owner)
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", owner);
        bear.SetOwner(owner);
        bear.SetController(owner);
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        bear.HasSummoningSickness = false;
        return bear;
    }

    [Fact]
    public void Rebind_ResolvesAttackersAndPlayerTarget_ByIds()
    {
        var atk1 = AddBear(_alice);
        var atk2 = AddBear(_alice);
        AddBear(_bob); // bystander on the defending side

        var resume = CombatResumeState.FromAttackers(new[] { atk1, atk2 }, _bob);

        var cloned = GameStateCloner.Clone(new[] { _alice, _bob });
        var plan = resume.Rebind(cloned.Players);

        plan.Should().NotBeNull();
        plan!.Attackers.Select(a => a.Attacker.InstanceId)
            .Should().BeEquivalentTo(new[] { atk1.InstanceId, atk2.InstanceId });

        // Attackers resolve to the CLONED creature instances, not the live ones.
        plan.Attackers.Select(a => a.Attacker)
            .Should().NotContain(c => ReferenceEquals(c, atk1) || ReferenceEquals(c, atk2));

        // Defending target resolves to the CLONED Bob instance.
        var clonedBob = cloned.Players.Single(p => p.Id == _bob.Id);
        clonedBob.Should().NotBeSameAs(_bob);
        plan.Attackers.Should().OnlyContain(a =>
            ReferenceEquals(a.DefendingPlayerOrPlaneswalker, clonedBob));
    }

    [Fact]
    public void Rebind_DropsAttackersMissingFromClone()
    {
        var atk1 = AddBear(_alice);
        var atk2 = AddBear(_alice);

        var resume = CombatResumeState.FromAttackers(new[] { atk1, atk2 }, _bob);

        var cloned = GameStateCloner.Clone(new[] { _alice, _bob });

        // Remove atk2's clone from the cloned battlefield (simulates an
        // attacker that does not survive the clone — defensive guard).
        var clonedAlice = cloned.Players.Single(p => p.Id == _alice.Id);
        var atk2Clone = clonedAlice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.InstanceId == atk2.InstanceId);
        clonedAlice.Zones.Battlefield.RemoveCard(atk2Clone);

        var plan = resume.Rebind(cloned.Players);

        plan.Should().NotBeNull();
        plan!.Attackers.Select(a => a.Attacker.InstanceId)
            .Should().Equal(atk1.InstanceId);
    }

    [Fact]
    public void Rebind_ReturnsNull_WhenNoAttackerSurvives()
    {
        var atk = AddBear(_alice);
        var resume = CombatResumeState.FromAttackers(new[] { atk }, _bob);

        var cloned = GameStateCloner.Clone(new[] { _alice, _bob });
        var clonedAlice = cloned.Players.Single(p => p.Id == _alice.Id);
        var atkClone = clonedAlice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.InstanceId == atk.InstanceId);
        clonedAlice.Zones.Battlefield.RemoveCard(atkClone);

        resume.Rebind(cloned.Players).Should().BeNull();
    }
}
