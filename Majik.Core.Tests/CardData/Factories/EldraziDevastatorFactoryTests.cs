using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EldraziDevastatorFactory"/>
/// (Battle for Zendikar, {8}).
///
/// Creature — Eldrazi 8/9. Oracle text (verified against Scryfall 2026-06-02):
///   "Trample"
///
/// A vanilla-with-Trample colorless Eldrazi body — the Annihilator-stripped
/// sibling of <see cref="UlamogsCrusherFactory"/>.
///
/// Covers:
///   - Identity (Creature — Eldrazi, {8} colorless, 8/9, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample <see cref="KeywordAbility"/> marker present + read by
///     <see cref="CombatAbilities.HasTrample"/>.
/// </summary>
public class EldraziDevastatorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EldraziDevastator_Identity_CreatureEldrazi_8_9_Colorless8()
    {
        var devastator = EldraziDevastatorFactory.Create(_alice);

        devastator.Name.Should().Be("Eldrazi Devastator");
        devastator.HasType(CardType.Creature).Should().BeTrue();
        devastator.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        devastator.ManaCost.Should().Be("{8}");
        devastator.ManaCostValue.TotalValue.Should().Be(8);
        // CR 105.2c — {8} is generic mana only, so the card is colorless.
        CardColors.GetColors(devastator).Should().BeEmpty();
        devastator.BasePower.Should().Be(8);
        devastator.BaseToughness.Should().Be(9);
        devastator.Owner.Should().BeSameAs(_alice);
        devastator.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EldraziDevastator_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Eldrazi Devastator", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Eldrazi Devastator");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(8);
        ((Creature)card).BaseToughness.Should().Be(9);
    }

    [Fact]
    public void EldraziDevastator_HasTrample_KeywordMarker()
    {
        var devastator = EldraziDevastatorFactory.Create(_alice);

        // CR 702.19 — Trample is present as a KeywordAbility marker and read
        // by the combat-keyword lookup.
        devastator.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
        CombatAbilities.HasTrample(devastator).Should().BeTrue();
    }
}
