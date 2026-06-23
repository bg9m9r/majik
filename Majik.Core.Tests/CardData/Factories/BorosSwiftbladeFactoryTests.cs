using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BorosSwiftbladeFactory"/>.
///
/// Boros Swiftblade (Ravnica: City of Guilds, {R}{W}). Creature — Human
/// Soldier 1/2. Oracle text (verified against Scryfall):
///   "Double strike"
///
/// Coverage:
/// - Identity (name, type, Human + Soldier subtypes, cost, colours, P/T,
///   owner/controller).
/// - Double strike keyword marker (CR 702.4) surfaced via CombatAbilities.
///
/// (NamedCardFactory dispatch + well-formedness are covered for every
/// implemented card by CardFactoryContractTests — not re-asserted here.)
/// </summary>
[Trait("Color", "M")]
public class BorosSwiftbladeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ────────────────────────────────────────────────────────

    [Fact]
    public void BorosSwiftblade_Identity()
    {
        var c = BorosSwiftbladeFactory.Create(_alice);

        c.Name.Should().Be("Boros Swiftblade");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.ManaCost.Should().Be("{R}{W}");
        c.ManaCostValue.TotalValue.Should().Be(2);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Red);
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Double strike ───────────────────────────────────────────────────

    [Fact]
    public void BorosSwiftblade_HasDoubleStrike()
    {
        var c = BorosSwiftbladeFactory.Create(_alice);

        CombatAbilities.HasDoubleStrike(c).Should().BeTrue(
            "Boros Swiftblade prints Double strike (CR 702.4).");
    }
}
