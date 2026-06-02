using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArcaneSanctumFactory"/> — Arcane Sanctum
/// (Shards of Alara "tapped tri-land", a.k.a. the Vivid-less Esper tap-land).
///
/// W/U/B tapped tri-land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {W}, {U}, or {B}."
///
/// Same oracle shape as the Triome cycle (<see cref="SavaiTriomeFactory"/>)
/// minus cycling and minus the basic-land subtypes — a plain nonbasic land
/// with an unconditional enters-tapped restriction and a three-colour mana
/// ability. Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>,
/// mirroring <see cref="BlossomingSandsFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - Three single-colour mana abilities — {W}, {U}, {B} (CR 605.1a).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the Refuge / Triome cycle. The
/// optional two-arg path also registers an <see cref="EntersTappedReplacement"/>
/// on a supplied <see cref="ReplacementBus"/>.
/// </summary>
[Trait("Color", "C")]
public class ArcaneSanctumTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ArcaneSanctum_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Arcane Sanctum", _alice);

        land.Name.Should().Be("Arcane Sanctum");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Arcane Sanctum is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArcaneSanctum_HasThreeManaAbilities_ProducingWhiteBlueBlack()
    {
        var land = (Land)NamedCardFactory.Create("Arcane Sanctum", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(3, "{T}: Add {W}, {U}, or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void ArcaneSanctum_HasNoTriggeredAbilities()
    {
        // Arcane Sanctum has no ETB life-gain or cycling — only the tapped
        // entry (handled by the binder) and the three mana abilities.
        var land = (Land)NamedCardFactory.Create("Arcane Sanctum", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ArcaneSanctum_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        // CR 614.1c — unconditional "This land enters tapped." The two-arg
        // path registers the replacement on the supplied bus; build succeeds.
        var replacements = new ReplacementBus();
        var land = ArcaneSanctumFactory.Create(_alice, replacements);

        land.Should().NotBeNull();
        land.Name.Should().Be("Arcane Sanctum");
    }
}
