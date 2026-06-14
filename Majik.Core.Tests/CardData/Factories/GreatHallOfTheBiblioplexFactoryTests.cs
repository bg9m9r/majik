using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GreatHallOfTheBiblioplexFactory"/> — the conditional
/// animate manland with a granted cast-pump trigger:
///   "{5}: If this land isn't a creature, it becomes a 2/4 Wizard creature with
///    \"Whenever you cast an instant or sorcery spell, this creature gets +1/+0
///    until end of turn.\" It's still a land."
/// </summary>
[Trait("Color", "C")]
public class GreatHallOfTheBiblioplexFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GreatHall_Identity_AndDispatch()
    {
        var land = GreatHallOfTheBiblioplexFactory.Create(_alice);
        land.Name.Should().Be("Great Hall of the Biblioplex");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");

        var dispatched = NamedCardFactory.Create("Great Hall of the Biblioplex", _alice);
        dispatched.Should().NotBeNull();
        dispatched!.Name.Should().Be("Great Hall of the Biblioplex");
    }

    [Fact]
    public void GreatHall_HasManaAndAnimateAbilities()
    {
        var land = GreatHallOfTheBiblioplexFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {C}");
        var animate = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle("the {5} animate cost");
    }

    [Fact]
    public void GreatHall_Animate_2_4_Wizard_Permanent_GrantsCastPump()
    {
        var effects = new ContinuousEffectsService();
        var land = GreatHallOfTheBiblioplexFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // No granted trigger before animating.
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var cc = effects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Types.Should().Contain(CardType.Land, "It's still a land");
        cc.Power.Should().Be(2);
        cc.Toughness.Should().Be(4);
        cc.Subtypes.Should().Contain(CardSubtype.Wizard);

        // Granted cast-pump trigger now exists; casting an instant pumps +1/+0.
        var trigger = land.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        var bolt = new Instant("Bolt", "{R}");
        bolt.SetOwner(_alice); bolt.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(bolt, _alice);
        var castEvent = new Majik.Core.Domain.DomainEvents.SpellCastEvent(spell);
        trigger.Condition.Matches(castEvent, trigger).Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();
        effects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject.Power.Should().Be(3,
            "+1/+0 from the granted cast-pump");

        // No "until end of turn" on the animate → body is permanent (the pump
        // is until EOT, the body is not). Re-activation is a no-op once a
        // creature.
        foreach (var e in animate.Effects) e.Execute();
        effects.Compute((Permanent)land).Types.Should().Contain(CardType.Creature);
    }
}
