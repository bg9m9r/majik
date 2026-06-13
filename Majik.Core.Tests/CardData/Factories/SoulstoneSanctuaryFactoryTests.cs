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
/// Tests for <see cref="SoulstoneSanctuaryFactory"/> (Modern Horizons 3
/// "all creature types" manland). Land:
///   "{T}: Add {C}.
///    {4}: This land becomes a 3/3 creature with vigilance and all creature
///    types. It's still a land."
///
/// The distinguishing feature vs Mutavault / Faceless Haven: the animate has
/// NO "until end of turn" clause, so the animation is PERMANENT (CR 613.1c) —
/// the continuous effects do NOT expire at cleanup.
/// </summary>
[Trait("Color", "C")]
public class SoulstoneSanctuaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SoulstoneSanctuary_Identity()
    {
        var land = SoulstoneSanctuaryFactory.Create(_alice);

        land.Name.Should().Be("Soulstone Sanctuary");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SoulstoneSanctuary_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Soulstone Sanctuary", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Soulstone Sanctuary");
    }

    [Fact]
    public void SoulstoneSanctuary_HasManaAndAnimateAbilities()
    {
        var land = SoulstoneSanctuaryFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability");
        var animate = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle("the {4} animate cost");
    }

    [Fact]
    public void SoulstoneSanctuary_Animate_GrantsEveryCreatureType_Vigilance_3_3_AndIsPermanent()
    {
        var effects = new ContinuousEffectsService();
        var land = SoulstoneSanctuaryFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land, "It's still a land");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Goblin, "all creature types granted");
        chars.Keywords.Should().Contain("Vigilance");
        var cc = chars.Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(3);

        // No "until end of turn" → the animation survives the cleanup expiry.
        effects.ExpireEndOfTurn();
        effects.Compute((Permanent)land).Types.Should().Contain(CardType.Creature,
            "Soulstone Sanctuary's animate is permanent (no 'until end of turn')");
    }
}
