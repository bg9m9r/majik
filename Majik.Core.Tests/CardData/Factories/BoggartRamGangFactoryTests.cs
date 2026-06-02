using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Boggart Ram-Gang (Shadowmoor, {R/G}{R/G}{R/G}, Creature — Goblin
/// Warrior 3/3, Haste + Wither). CR 702.10 / 702.90.
/// </summary>
[Trait("Color", "RG")]
public class BoggartRamGangFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BoggartRamGang_HasCreatureShape_GoblinWarrior_3_3_AtThreeHybrid()
    {
        var card = BoggartRamGangFactory.Create(_alice);

        card.Name.Should().Be("Boggart Ram-Gang");
        card.ManaCost.Should().Be("{R/G}{R/G}{R/G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{R/G}{R/G}{R/G} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BoggartRamGang_HasHasteAndWither()
    {
        var card = BoggartRamGangFactory.Create(_alice);

        CombatAbilities.HasHaste(card).Should().BeTrue();
        CombatAbilities.HasWither(card).Should().BeTrue();
        CombatAbilities.DealsCreatureDamageAsMinusCounters(card).Should().BeTrue();
    }

    [Fact]
    public void BoggartRamGang_FightDamageToCreature_LandsAsMinusOneMinusOneCounters()
    {
        var svc = new ContinuousEffectsService();
        var ramGang = BoggartRamGangFactory.Create(_alice);
        ramGang.ActiveEffects = svc;
        ramGang.SetZone(ZoneType.Battlefield);

        var foe = new Creature("Bear", "1G", 4, 4)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        Fx.Fight(ramGang, foe);

        foe.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(3);
        foe.Damage.Should().Be(0, because: "wither deals -1/-1 counters, not marked damage");
        foe.Toughness.Should().Be(1);
    }
}
