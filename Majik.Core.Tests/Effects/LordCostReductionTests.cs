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

        lord.Zone = ZoneType.Graveyard;
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

    // ---------- Cost reduction ----------

    [Fact]
    public void CostReduction_ReducesGenericOnly()
    {
        var red = new CostReductionStaticEffect(2, (_, _) => true);
        red.Reduce(ManaCost.Parse("3RR")).Generic.Should().Be(1);
        red.Reduce(ManaCost.Parse("3RR")).Red.Should().Be(2);
    }

    [Fact]
    public void CostReduction_FloorsAtZero()
    {
        var red = new CostReductionStaticEffect(5, (_, _) => true);
        red.Reduce(ManaCost.Parse("2R")).Generic.Should().Be(0);
        red.Reduce(ManaCost.Parse("2R")).Red.Should().Be(1);
    }

    [Fact]
    public void CostReduction_AppliesToFilterMatches()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };

        var red = new CostReductionStaticEffect(1, (_, c) => c.HasType(CardType.Instant));
        red.AppliesTo(_alice, bolt).Should().BeTrue();
        red.AppliesTo(_alice, bear).Should().BeFalse();
    }
}
