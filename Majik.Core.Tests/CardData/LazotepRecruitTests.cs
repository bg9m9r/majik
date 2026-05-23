using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LazotepRecruitFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, Zombie subtype, owner/controller, P/T).
/// - Single ETB triggered ability with no mana abilities.
/// - ETB effect (no Army present): creates a 0/0 Zombie Army token with one +1/+1 counter.
/// - ETB effect (Army already present): re-uses the existing Army; no new token; counter stacks.
/// </summary>
public class LazotepRecruitTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LazotepRecruit_IsCreature()
    {
        var card = LazotepRecruitFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void LazotepRecruit_HasExpectedShape()
    {
        var creature = LazotepRecruitFactory.Create(_alice);

        creature.Name.Should().Be("Lazotep Recruit");
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
        creature.Power.Should().Be(1);
        creature.Toughness.Should().Be(1);
        creature.Subtypes.Should().Contain(CardSubtype.Zombie);
    }

    [Fact]
    public void LazotepRecruit_HasSingleEtbTrigger_NoManaAbility()
    {
        var creature = LazotepRecruitFactory.Create(_alice);

        creature.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        creature.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void LazotepRecruit_EtbAmass_NoArmyPresent_CreatesZombieArmyTokenWithCounter()
    {
        var alice = new Player("Alice", 20);
        var creature = LazotepRecruitFactory.Create(alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();

        foreach (var effect in etb.Effects) effect.Execute();

        var army = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Subtypes.Contains(CardSubtype.Army));
        army.IsToken.Should().BeTrue();
        army.Subtypes.Should().Contain(CardSubtype.Army);
        army.Subtypes.Should().Contain(CardSubtype.Zombie);
        army.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void LazotepRecruit_EtbAmass_ExistingArmy_StacksCounterOnIt_NoNewToken()
    {
        var alice = new Player("Alice", 20);
        var existing = new Creature("Standing Army", "", 0, 0,
            subtypes: new[] { CardSubtype.Army })
        {
            Owner = alice,
            Controller = alice,
        };
        existing.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(existing);

        var creature = LazotepRecruitFactory.Create(alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        existing.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        // Only the existing Army should occupy the battlefield (no new token).
        alice.Zones.Battlefield.GetCards().Should().HaveCount(1);
    }
}
