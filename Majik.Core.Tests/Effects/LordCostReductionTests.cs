using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class LordCostReductionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------- Lord ----------

    [Fact]
    public void Lord_BoostsOtherCreaturesOfMatchingSubtype()
    {
        var svc = new ContinuousEffectsService();
        var lord = new Creature("Lord of Atlantis", "UU", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var minion = new Creature("Human Soldier", "1W", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        svc.Register(new LordStaticEffect(lord, CardSubtype.Human));

        minion.Power.Should().Be(3);
        minion.Toughness.Should().Be(3);
        lord.Power.Should().Be(2); // lord excluded by default
    }

    [Fact]
    public void Lord_ExcludesOpponentCreatures()
    {
        var svc = new ContinuousEffectsService();
        var lord = new Creature("Lord of Atlantis", "UU", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var enemyHuman = new Creature("Enemy", "1W", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        svc.Register(new LordStaticEffect(lord, CardSubtype.Human));

        enemyHuman.Power.Should().Be(2);
    }

    [Fact]
    public void Lord_LeavesBattlefield_NoLongerBoosts()
    {
        var svc = new ContinuousEffectsService();
        var lord = new Creature("Lord", "UU", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var minion = new Creature("Soldier", "1W", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        svc.Register(new LordStaticEffect(lord, CardSubtype.Human));
        minion.Power.Should().Be(3);

        lord.SetZone(ZoneType.Graveyard);
        minion.Power.Should().Be(2);
    }

    [Fact]
    public void Lord_GrantsKeyword_WhileActive()
    {
        var svc = new ContinuousEffectsService();
        var lord = new Creature("Lord", "UU", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var minion = new Creature("Soldier", "1W", 2, 2,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        svc.Register(new LordStaticEffect(lord, CardSubtype.Human,
            grantedKeywords: new[] { "Flying" }));

        Majik.Core.Combat.CombatAbilities.HasFlying(minion).Should().BeTrue();
    }
}
