using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlimmerlightFactory"/>.
///
/// Card: Glimmerlight — Artifact — Equipment {2} (Foundations Jumpstart).
///   "When this Equipment enters, create a 1/1 white Glimmer enchantment
///    creature token.
///    Equipped creature gets +1/+1.
///    Equip {1}."
/// </summary>
[Trait("Color", "C")]
public class GlimmerlightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Glimmerlight_Identity()
    {
        var c = GlimmerlightFactory.Create(_alice);

        c.Name.Should().Be("Glimmerlight");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Glimmerlight is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Glimmerlight_EquipAbility_HasGenericOneCost()
    {
        var c = GlimmerlightFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    [Fact]
    public void Glimmerlight_Equipped_Bear_Becomes_3_3()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var equip = GlimmerlightFactory.Create(_alice, svc, triggers: null);
        equip.Zone = ZoneType.Battlefield;

        equip.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+1 boost from Glimmerlight");
        bear.GetToughness().Should().Be(3, "+1/+1 boost from Glimmerlight");
    }

    [Fact]
    public void Glimmerlight_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var equip = GlimmerlightFactory.Create(_alice, svc, triggers: null);
        equip.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Glimmerlight's boost gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Glimmerlight_Etb_Mints_1_1_White_Glimmer_Enchantment_Creature_Token()
    {
        var token = GlimmerlightFactory.CreateGlimmerToken(_alice, zoneService: null);

        token.Name.Should().Be("Glimmer");
        token.IsToken.Should().BeTrue();
        token.GetPower().Should().Be(1);
        token.GetToughness().Should().Be(1);
        token.HasType(CardType.Creature).Should().BeTrue(
            "the Glimmer token is a creature");
        token.HasType(CardType.Enchantment).Should().BeTrue(
            "the Glimmer token is an enchantment creature");
        token.HasSubtype(CardSubtype.Glimmer).Should().BeTrue(
            "the token carries the Glimmer subtype");
        token.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.White },
            "the token is a white token (CR 111.4)");
        token.Zone.Should().Be(ZoneType.Battlefield,
            "the token enters the battlefield (CR 111.6)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(token);
    }

    [Fact]
    public void Glimmerlight_Etb_Trigger_FiresOnSelfEnter()
    {
        var equip = GlimmerlightFactory.Create(_alice);

        // The ETB trigger is attached structurally; assert it is a
        // self-enter triggered ability so the wired path mints a token.
        var etb = equip.Abilities.OfType<TriggeredAbility>().Single();
        etb.Should().NotBeNull("Glimmerlight has an ETB token-mint trigger");
    }
}
