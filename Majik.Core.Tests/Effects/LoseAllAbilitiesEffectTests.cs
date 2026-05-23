using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613.6 — Layer 6 ability-removing effects (Humility-class), plus the
/// source-suppression behavior whereby continuous effects sourced from a
/// stripped creature stop applying (CR 613.8 dependency relationship between
/// Layer 6 and Layer 7).
/// </summary>
public class LoseAllAbilitiesEffectTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Humility_StripsPrintedKeywordFromAffectedCreature()
    {
        var svc = new ContinuousEffectsService();
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var air = new Creature("Air Elemental", "3UU", 4, 4)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        air.AddAbility(new KeywordAbility("Flying", air, _alice));

        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { air }));

        var chars = svc.Compute(air);

        chars.Keywords.Should().NotContain("Flying");
        chars.Power.Should().Be(4);
        chars.Toughness.Should().Be(4);
    }

    [Fact]
    public void Humility_SuppressesEffectsFromStrippedSourceLord()
    {
        var svc = new ContinuousEffectsService();
        var chieftain = new Creature("Goblin Chieftain", "1RR", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var fanatic = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        svc.Register(new LordStaticEffect(chieftain, CardSubtype.Goblin));

        // Baseline: lord pump is active, Fanatic is 2/2.
        svc.Compute(fanatic).Power.Should().Be(2);
        svc.Compute(fanatic).Toughness.Should().Be(2);

        // Drop a Humility-style strip on the chieftain (and fanatic for symmetry).
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { chieftain, fanatic }));

        // Lord effect must be suppressed because its Source (chieftain) is stripped.
        var chars = svc.Compute(fanatic);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void Humility_DoesNotSuppressItself()
    {
        var svc = new ContinuousEffectsService();
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var chieftain = new Creature("Goblin Chieftain", "1RR", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        chieftain.AddAbility(new KeywordAbility("Haste", chieftain, _alice));

        // Humility's Source is the enchantment, NOT the chieftain — so the
        // strip itself is not suppressed by its own application.
        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { chieftain }));

        var chars = svc.Compute(chieftain);

        chars.Keywords.Should().BeEmpty();
    }
}
