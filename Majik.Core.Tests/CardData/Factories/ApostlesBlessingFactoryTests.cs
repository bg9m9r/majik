using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Apostle's Blessing (New Phyrexia, {1}{W/P}, Instant).
///
/// Covers:
/// - Identity (name, type, cost, colour) + Phyrexian keyword marker.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="ApostlesBlessingFactory.PhyrexianAlternativeCost"/>:
///   mana-only path = <c>{1}</c>, life cost = 2.
/// - <see cref="SpellDefinition"/> shape: 1..1 target.
/// - <see cref="ApostlesBlessingFactory.Resolve"/> on a creature target:
///   <see cref="ProtectionAbility"/> attached via the
///   <see cref="ContinuousEffectsService"/>;
///   <see cref="Protection.HasProtectionFromColor"/> matches.
/// - Resolve on an artifact target: <see cref="ProtectionAbility"/>
///   attached directly to the card.
/// - Resolve on an illegal target: clean no-op.
/// - Quality picker default = "artifacts".
/// </summary>
[Trait("Color", "W")]
public class ApostlesBlessingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCost_Instant_PhyrexianMarker()
    {
        var card = ApostlesBlessingFactory.Create(_alice);

        card.Name.Should().Be("Apostle's Blessing");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Should().BeOfType<Instant>();

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword.Equals("Phyrexian", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }
    [Fact]
    public void PhyrexianAlternativeCost_OneLifePip_TwoLife()
    {
        var alt = ApostlesBlessingFactory.PhyrexianAlternativeCost();
        alt.LifeCost.Should().Be(2);
    }

    [Fact]
    public void SpellDefinition_OneTargetRequest_NoX()
    {
        var def = ApostlesBlessingFactory.BuildSpellDefinition(_alice);
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Resolve_CreatureTarget_RegistersProtection_OnActiveEffects()
    {
        var effects = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bear);

        var result = ApostlesBlessingFactory.Resolve(
            _alice, bear,
            ApostlesBlessingFactory.QualityFromColor(ManaColor.Red));

        result.Target.Should().BeSameAs(bear);
        result.Quality.Should().Be(ApostlesBlessingFactory.QualityRed);

        // Protection should be observable via the rules helper.
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue();
        Protection.HasProtectionFromColor(bear, ManaColor.Blue).Should().BeFalse();
    }

    [Fact]
    public void Resolve_ArtifactTarget_AttachesProtectionDirectly()
    {
        var mox = new Artifact("Mox", "{0}");
        mox.SetOwner(_alice);
        mox.SetController(_alice);
        mox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mox);

        var result = ApostlesBlessingFactory.Resolve(_alice, mox);

        result.Target.Should().BeSameAs(mox);
        result.Quality.Should().Be(ApostlesBlessingFactory.QualityArtifacts);

        Protection.HasProtectionFromCardType(mox, CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Resolve_IllegalTarget_NotPermanent_NoOp()
    {
        var result = ApostlesBlessingFactory.Resolve(_alice, "not a card");
        result.Target.Should().BeNull();
        result.Quality.Should().BeNull();
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoOp()
    {
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Graveyard); // already died.

        var result = ApostlesBlessingFactory.Resolve(_alice, bear);
        result.Target.Should().BeNull();
    }

    [Fact]
    public void Resolve_LandTarget_NoOp()
    {
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        var result = ApostlesBlessingFactory.Resolve(_alice, mountain);
        result.Target.Should().BeNull();
    }

    [Fact]
    public void Resolve_CreatureTarget_EOTExpiry_ClearsProtection()
    {
        var effects = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bear);

        ApostlesBlessingFactory.Resolve(
            _alice, bear,
            ApostlesBlessingFactory.QualityFromColor(ManaColor.Black));

        Protection.HasProtectionFromColor(bear, ManaColor.Black).Should().BeTrue();

        // EOT cleanup drops the grant.
        effects.ExpireEndOfTurn();
        Protection.HasProtectionFromColor(bear, ManaColor.Black).Should().BeFalse();
    }
}
