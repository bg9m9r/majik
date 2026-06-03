using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DestinySpinnerFactory"/> (Theros Beyond Death,
/// {1}{G}).
///
/// Enchantment Creature — Human 2/3. Oracle text:
///   "Creature and enchantment spells you control can't be countered.
///    {3}{G}: Target land you control becomes an X/X Elemental creature with
///    trample and haste until end of turn, where X is the number of
///    enchantments you control. It's still a land."
/// </summary>
[Trait("Color", "G")]
public class DestinySpinnerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity()
    {
        var spinner = DestinySpinnerFactory.Create(_alice);

        spinner.Name.Should().Be("Destiny Spinner");
        spinner.ManaCost.Should().Be("{1}{G}");
        spinner.HasType(CardType.Creature).Should().BeTrue();
        spinner.HasType(CardType.Enchantment).Should().BeTrue("Destiny Spinner is an Enchantment Creature");
        spinner.HasSubtype(CardSubtype.Human).Should().BeTrue();
        spinner.BasePower.Should().Be(2);
        spinner.BaseToughness.Should().Be(3);
        spinner.Owner.Should().BeSameAs(_alice);
        spinner.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasControllerScopedUncounterableStatic_CoveringCreatureAndEnchantment()
    {
        var spinner = DestinySpinnerFactory.Create(_alice);

        var marker = spinner.Abilities.OfType<UncounterableControllerStatic>().Single();
        marker.Controller.Should().BeSameAs(_alice);
        marker.CardTypes.Should().BeEquivalentTo(new[] { CardType.Creature, CardType.Enchantment });

        marker.Covers(new[] { CardType.Creature }).Should().BeTrue();
        marker.Covers(new[] { CardType.Enchantment }).Should().BeTrue();
        marker.Covers(new[] { CardType.Instant }).Should().BeFalse(
            "the static only covers creature + enchantment spells");
    }

    [Fact]
    public void HasActivatedAbility_With3GCost()
    {
        var spinner = DestinySpinnerFactory.Create(_alice);

        var ability = spinner.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle();
    }

    [Fact]
    public void ActivatedAbility_AnimatesTargetLand_ToElementalWithTrampleAndHaste_StillALand()
    {
        var effects = new ContinuousEffectsService();
        var land = new Land("Forest") { Owner = _alice };
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        var spinner = DestinySpinnerFactory.Create(
            _alice,
            continuousEffects: effects,
            targetLandResolver: () => new List<Land> { land });
        _alice.Zones.Battlefield.AddCard(spinner);

        var ability = spinner.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land, "CR 613.1c — it's still a land");
        chars.Types.Should().Contain(CardType.Creature, "animated into a creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental, "becomes an Elemental");
        chars.Keywords.Should().Contain("Trample");
        chars.Keywords.Should().Contain("Haste");
    }

    [Fact]
    public void ActivatedAbility_SetsXX_WhereXIsEnchantmentsControlled()
    {
        var effects = new ContinuousEffectsService();
        var land = new Land("Forest") { Owner = _alice };
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        var spinner = DestinySpinnerFactory.Create(
            _alice,
            continuousEffects: effects,
            targetLandResolver: () => new List<Land> { land });
        _alice.Zones.Battlefield.AddCard(spinner); // enchantment #1 (Destiny Spinner itself)

        var aura = new Enchantment("Some Aura", "{G}");
        aura.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(aura); // enchantment #2

        var ability = spinner.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        var pt = RegisteredEffects(effects).OfType<ManlandCycleBecomesPTEffect>().Single();
        pt.NewPower.Should().Be(2, "X = enchantments you control (Destiny Spinner + aura)");
        pt.NewToughness.Should().Be(2);
        pt.ExpiresAtEndOfTurn.Should().BeTrue("the animation lasts until end of turn (CR 514.2)");
    }

    [Fact]
    public void ActivatedAbility_NoResolver_NoOps()
    {
        var effects = new ContinuousEffectsService();
        var spinner = DestinySpinnerFactory.Create(_alice, effects, targetLandResolver: null);

        var ability = spinner.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        RegisteredEffects(effects).Should().BeEmpty("no target land supplied — clean no-op");
    }

    private static IEnumerable<ContinuousEffect> RegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IEnumerable<ContinuousEffect>)field!.GetValue(svc)!;
    }
}
