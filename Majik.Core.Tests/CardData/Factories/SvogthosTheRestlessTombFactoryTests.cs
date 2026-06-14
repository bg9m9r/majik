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
/// Tests for <see cref="SvogthosTheRestlessTombFactory"/> — the */* CDA-P/T
/// manland:
///   "{T}: Add {C}.
///    {3}{B}{G}: Until end of turn, this land becomes a black and green Plant
///    Zombie creature with \"This creature's power and toughness are each equal
///    to the number of creature cards in your graveyard.\" It's still a land."
/// </summary>
[Trait("Color", "BG")]
public class SvogthosTheRestlessTombFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Svogthos_Identity_AndDispatch()
    {
        var land = SvogthosTheRestlessTombFactory.Create(_alice);
        land.Name.Should().Be("Svogthos, the Restless Tomb");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");

        var dispatched = NamedCardFactory.Create("Svogthos, the Restless Tomb", _alice);
        dispatched.Should().NotBeNull();
        dispatched!.Name.Should().Be("Svogthos, the Restless Tomb");
    }

    [Fact]
    public void Svogthos_HasManaAndAnimateAbilities()
    {
        var land = SvogthosTheRestlessTombFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {C}");
        var animate = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle("the {3}{B}{G} animate cost");
    }

    [Fact]
    public void Svogthos_Animate_PlantZombie_CdaFromGraveyardCreatureCount()
    {
        var effects = new ContinuousEffectsService();
        var land = SvogthosTheRestlessTombFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Two creature cards in Alice's graveyard.
        foreach (var c in new ICard[]
                 {
                     new Creature("Bear", "{1}{G}", 2, 2, null, new[] { CardSubtype.Bear }),
                     new Creature("Elf", "{G}", 1, 1, null, new[] { CardSubtype.Elf }),
                 })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c); c.SetZone(ZoneType.Graveyard);
        }

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land, "It's still a land");
        chars.Subtypes.Should().Contain(CardSubtype.Plant);
        chars.Subtypes.Should().Contain(CardSubtype.Zombie);
        var cc = chars.Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(2, "*/* = creature cards in graveyard (2)");
        cc.Toughness.Should().Be(2);

        // Until end of turn → the animation expires at cleanup.
        effects.ExpireEndOfTurn();
        effects.Compute((Permanent)land).Types.Should().NotContain(CardType.Creature);
    }
}
