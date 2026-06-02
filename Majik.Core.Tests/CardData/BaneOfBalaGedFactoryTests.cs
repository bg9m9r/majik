using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BaneOfBalaGedFactory"/>
/// (Battle for Zendikar, {7}).
///
/// Creature — Eldrazi 7/5. Oracle text (verified against Scryfall):
///   "Whenever this creature attacks, defending player exiles two
///    permanents they control."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {7}, 7/5, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Attack <see cref="TriggeredAbility"/> is attached and fires on
///     self-attack.
///   - On resolution the defending player exiles two permanents they
///     control (deterministic first-two fallback).
/// </summary>
public class BaneOfBalaGedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BaneOfBalaGed_Identity()
    {
        var bane = BaneOfBalaGedFactory.Create(_alice);

        bane.Name.Should().Be("Bane of Bala Ged");
        bane.ManaCost.Should().Be("{7}");
        bane.HasType(CardType.Creature).Should().BeTrue();
        bane.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        bane.BasePower.Should().Be(7);
        bane.BaseToughness.Should().Be(5);
        bane.Owner.Should().BeSameAs(_alice);
        bane.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BaneOfBalaGed_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Bane of Bala Ged", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bane of Bala Ged");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(7);
        ((Creature)card).BaseToughness.Should().Be(5);
    }

    [Fact]
    public void BaneOfBalaGed_HasAttackTrigger()
    {
        var bane = BaneOfBalaGedFactory.Create(_alice);

        var triggers = bane.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the factory attaches a single attack trigger");

        // Trigger fires on self-attack.
        var trig = triggers[0];
        trig.Condition.Matches(new CreatureAttacksEvent(bane, _bob), trig)
            .Should().BeTrue();
    }

    [Fact]
    public void BaneOfBalaGed_AttackTrigger_DoesNotFireForOtherAttacker()
    {
        var bane = BaneOfBalaGedFactory.Create(_alice);
        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);

        var trig = bane.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(other, _bob), trig)
            .Should().BeFalse("the trigger is self-attack only");
    }

    [Fact]
    public void BaneOfBalaGed_AttackTrigger_ExilesTwoOnAttack()
    {
        var bane = BaneOfBalaGedFactory.Create(_alice);
        // Park Bane on Alice's battlefield so the trigger's active-zone
        // check succeeds (defaults to Battlefield only).
        bane.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bane);

        // Bob has 3 bears; deterministic fallback exiles the first two.
        var seeded = new List<Creature>();
        for (var i = 0; i < 3; i++)
        {
            var b = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            b.SetOwner(_bob);
            b.SetController(_bob);
            b.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(b);
            seeded.Add(b);
        }

        var trig = bane.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(bane, _bob), trig)
            .Should().BeTrue();
        foreach (var e in trig.Effects) e.Execute();

        seeded[0].Zone.Should().Be(ZoneType.Exile);
        seeded[1].Zone.Should().Be(ZoneType.Exile);
        seeded[2].Zone.Should().Be(ZoneType.Battlefield);
    }
}
