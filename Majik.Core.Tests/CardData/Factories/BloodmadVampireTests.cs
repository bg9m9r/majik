using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BloodmadVampireFactory"/> (Innistrad, {2}{R}).
///
/// Covers the card's UNIQUE non-madness behaviour:
/// - Identity (name, type, mana cost, P/T, Vampire + Berserker subtypes).
/// - Combat-damage-to-a-player trigger: dealing combat damage to a player
///   puts a +1/+1 counter on it (CR 603.1).
/// - The trigger does NOT fire on combat damage to a creature.
///
/// Madness {1}{R} is intrinsic (CR 702.35 — MadnessCatalog + the discard
/// funnel cover it) so it is intentionally not tested here.
/// </summary>
[Trait("Color", "R")]
public class BloodmadVampireTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BloodmadVampire_Identity()
    {
        var vamp = BloodmadVampireFactory.Create(_alice);

        vamp.Name.Should().Be("Bloodmad Vampire");
        vamp.ManaCost.Should().Be("{2}{R}");
        vamp.HasType(CardType.Creature).Should().BeTrue();
        vamp.HasSubtype(CardSubtype.Vampire).Should().BeTrue("Bloodmad Vampire is a Vampire");
        vamp.HasSubtype(CardSubtype.Berserker).Should().BeTrue("Bloodmad Vampire is a Berserker");
        vamp.BasePower.Should().Be(4);
        vamp.BaseToughness.Should().Be(1);
        vamp.Owner.Should().BeSameAs(_alice);
        vamp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BloodmadVampire_CombatDamageToPlayer_AddsPlusOnePlusOneCounter()
    {
        var vamp = BloodmadVampireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vamp);
        vamp.SetZone(ZoneType.Battlefield);

        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(vamp, _bob, 4);

        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "Bloodmad Vampire dealing combat damage to a player matches the trigger");

        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        foreach (var e in trigger.Effects) e.Execute();

        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the trigger puts a +1/+1 counter on Bloodmad Vampire");
    }

    [Fact]
    public void BloodmadVampire_CombatDamageToCreature_DoesNotFire()
    {
        // Oracle text says "deals combat damage to a player". Damage to a
        // creature must NOT fire the trigger.
        var vamp = BloodmadVampireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vamp);
        vamp.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(vamp, (ICard)blocker, 4);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "combat damage to a creature does not match — TargetPlayer is null");
    }
}
