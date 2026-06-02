using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wren's Run Vanquisher (Lorwyn / The Brothers' War Commander
/// reprint, {1}{G}).
///
/// Oracle (Scryfall, verified):
///   "As an additional cost to cast this spell, reveal an Elf card from
///    your hand or pay {3}.
///    Deathtouch (Any amount of damage this deals to a creature is enough
///    to destroy it.)"
///
/// Coverage:
/// - Identity / shape (Elf Warrior, {1}{G}, 3/3, green) + NamedCardFactory
///   dispatch through the embedded JSON definition.
/// - Deathtouch KeywordAbility marker, read by CombatAbilities.HasDeathtouch.
/// - Reveal-an-Elf-or-pay-{3} additional-cast-cost marker
///   ("RevealElfOrPay3"), the structural-only sibling of Silvergill Adept's
///   "RevealMerfolkOrPay3" (CR 601.2b). Cast-time enforcement is deferred
///   in v1, same as the other reveal-cost cards.
/// </summary>
[Trait("Color", "G")]
public class WrensRunVanquisherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Create_HasElfWarriorShape()
    {
        var vanquisher = WrensRunVanquisherFactory.Create(_alice);

        vanquisher.Should().BeOfType<Creature>();
        vanquisher.Name.Should().Be("Wren's Run Vanquisher");
        vanquisher.HasType(CardType.Creature).Should().BeTrue();
        vanquisher.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        vanquisher.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        vanquisher.ManaCost.Should().Be("{1}{G}");
        vanquisher.ManaCostValue.TotalValue.Should().Be(2);
        vanquisher.Power.Should().Be(3);
        vanquisher.Toughness.Should().Be(3);
        vanquisher.Owner.Should().BeSameAs(_alice);
        vanquisher.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasDeathtouchMarker_ReadByCombatAbilities()
    {
        var vanquisher = WrensRunVanquisherFactory.Create(_alice);

        // CR 702.2 — Deathtouch marker.
        vanquisher.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Deathtouch");

        // CombatAbilities consumes the marker for lethal-damage determination.
        CombatAbilities.HasDeathtouch(vanquisher).Should().BeTrue();
    }

    [Fact]
    public void Create_HasRevealElfOrPayAdditionalCostMarker()
    {
        var vanquisher = WrensRunVanquisherFactory.Create(_alice);

        // CR 601.2b — "reveal an Elf card from your hand or pay {3}" — the
        // structural-only additional-cast-cost marker (v1 enforcement
        // deferred, sibling of Silvergill Adept's RevealMerfolkOrPay3).
        vanquisher.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "RevealElfOrPay3");
    }

    [Fact]
    public void Create_HasNoTriggeredOrActivatedAbilities()
    {
        var vanquisher = WrensRunVanquisherFactory.Create(_alice);

        vanquisher.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        vanquisher.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
