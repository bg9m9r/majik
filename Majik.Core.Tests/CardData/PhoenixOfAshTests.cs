using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="PhoenixOfAshFactory"/> (Throne of Eldraine,
/// {2}{R}{R}).
///
/// Covers:
///   - Identity: name, type, Phoenix subtype, P/T 3/2, mana cost,
///     owner/controller.
///   - Haste keyword marker (CR 702.10).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - Absence of other keywords (no Flying, no Trample — Phoenix of Ash
///     is a ground Phoenix unlike Arclight Phoenix).
///   - Single ability surface (just the Haste marker — Escape deferred).
///
/// Escape (CR 702.143) is deferred — same gap as <see cref="UroTitanFactory"/>
/// / <see cref="PhlageFactory"/>.
/// </summary>
public class PhoenixOfAshTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PhoenixOfAsh_Identity_Phoenix_3_2_AtCost2RR_WithHaste()
    {
        var phoenix = PhoenixOfAshFactory.Create(_alice);

        phoenix.Name.Should().Be("Phoenix of Ash");
        phoenix.ManaCost.Should().Be("{2}{R}{R}");
        phoenix.HasType(CardType.Creature).Should().BeTrue();
        phoenix.HasSubtype(CardSubtype.Phoenix).Should().BeTrue();
        phoenix.BasePower.Should().Be(3);
        phoenix.BaseToughness.Should().Be(2);
        phoenix.Owner.Should().BeSameAs(_alice);
        phoenix.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasHaste(phoenix).Should().BeTrue(
            "CR 702.10 — Phoenix of Ash has Haste");
    }

    [Fact]
    public void PhoenixOfAsh_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Phoenix of Ash", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Phoenix of Ash");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Phoenix).Should().BeTrue();
        CombatAbilities.HasHaste((Creature)card).Should().BeTrue();
    }

    [Fact]
    public void PhoenixOfAsh_HasNoFlying_AndOnlyHasteKeyword()
    {
        var phoenix = PhoenixOfAshFactory.Create(_alice);

        // CR 702.9 — Phoenix of Ash is a ground Phoenix (contrast Arclight
        // Phoenix's Flying + Haste).
        CombatAbilities.HasFlying(phoenix).Should().BeFalse(
            "Phoenix of Ash does not have flying");

        // Only the Haste keyword marker is wired in v1 — Escape (CR 702.143)
        // is deferred. The implicit "attack as though it didn't have summoning
        // sickness" rider collapses to Haste in v1.
        var keywords = phoenix.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().ContainSingle(k => k.Keyword == "Haste");
        keywords.Should().HaveCount(1, "v1 wires only the Haste keyword marker");
    }

    [Fact]
    public void PhoenixOfAsh_HasNoTriggeredAbilities_EscapeDeferred()
    {
        var phoenix = PhoenixOfAshFactory.Create(_alice);

        phoenix.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Escape (CR 702.143) is deferred — no graveyard cast alt-cost yet, " +
            "same gap as Uro / Phlage / Cling to Dust");
    }
}
