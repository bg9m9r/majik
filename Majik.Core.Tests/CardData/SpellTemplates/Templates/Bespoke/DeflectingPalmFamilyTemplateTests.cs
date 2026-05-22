using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class DeflectingPalmFamilyTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null,
            Replacements: new ReplacementBus());

    private static SpellBindContext CtxWithoutBus(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void DeflectingPalm_Binds()
    {
        new DeflectingPalmFamilyTemplate().TryBind(Ctx(
            "The next time a source of your choice would deal damage to you this turn, prevent that damage. " +
            "If damage is prevented this way, Deflecting Palm deals that much damage to that source's controller."))
            .Should().NotBeNull();
    }

    [Fact]
    public void HonorablePassage_Binds()
    {
        new DeflectingPalmFamilyTemplate().TryBind(Ctx(
            "The next time a source of your choice would deal damage to any target this turn, prevent that damage. " +
            "If damage from a red source is prevented this way, Honorable Passage deals that much damage to the source's controller."))
            .Should().NotBeNull();
    }

    [Fact]
    public void InterventionPact_Binds()
    {
        new DeflectingPalmFamilyTemplate().TryBind(Ctx(
            "The next time a source of your choice would deal damage to you this turn, prevent that damage. " +
            "You gain life equal to the damage prevented this way."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ReverseDamage_Binds()
    {
        new DeflectingPalmFamilyTemplate().TryBind(Ctx(
            "The next time a source of your choice would deal damage to you this turn, prevent that damage. " +
            "You gain life equal to the damage prevented this way."))
            .Should().NotBeNull();
    }

    [Fact]
    public void FogOracle_DoesNotMatch()
    {
        new DeflectingPalmFamilyTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt this turn."))
            .Should().BeNull();
    }

    [Fact]
    public void DoesNotBind_WhenReplacementBusUnavailable()
    {
        new DeflectingPalmFamilyTemplate().TryBind(CtxWithoutBus(
            "The next time a source of your choice would deal damage to you this turn, prevent that damage. " +
            "You gain life equal to the damage prevented this way."))
            .Should().BeNull();
    }

    [Fact]
    public void Shield_PreventsDamageToBeneficiaryAndFiresRider()
    {
        var caster = new Player("A", 20);
        var bus = new ReplacementBus();
        var prevented = 0;
        var shield = new PreventNextDamageFromChosenSourceShield(
            caster,
            onPrevent: (amount, _) => prevented = amount);
        bus.Register(shield);

        var result = bus.Apply(new DamageIntent(new Player("B", 20), 3, TargetPlayer: caster));

        result.Should().BeNull();
        prevented.Should().Be(3);
    }

    [Fact]
    public void Shield_DoesNotApplyToOtherTargets()
    {
        var caster = new Player("A", 20);
        var other = new Player("B", 20);
        var bus = new ReplacementBus();
        bus.Register(new PreventNextDamageFromChosenSourceShield(caster));

        var result = bus.Apply(new DamageIntent(new Player("C", 20), 5, TargetPlayer: other));

        result.Should().NotBeNull();
        result!.Amount.Should().Be(5);
    }

    [Fact]
    public void Shield_OneShot_FiresOnlyOnce()
    {
        var caster = new Player("A", 20);
        var bus = new ReplacementBus();
        var fires = 0;
        bus.Register(new PreventNextDamageFromChosenSourceShield(
            caster,
            onPrevent: (_, _) => fires++));

        var first = bus.Apply(new DamageIntent(new Player("B", 20), 3, TargetPlayer: caster));
        var second = bus.Apply(new DamageIntent(new Player("B", 20), 4, TargetPlayer: caster));

        first.Should().BeNull();
        second.Should().NotBeNull();
        second!.Amount.Should().Be(4);
        fires.Should().Be(1);
    }
}
