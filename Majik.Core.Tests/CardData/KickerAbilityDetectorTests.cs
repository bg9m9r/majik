using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="KickerAbilityDetector"/> — the "card with a kicker
/// ability" predicate (CR 702.32 / 702.33). A card "has a kicker ability"
/// iff it carries a printed Kicker / Multikicker <see cref="KeywordAbility"/>
/// marker, independent of whether any cast of it was actually kicked.
/// </summary>
public class KickerAbilityDetectorTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Null_IsNotKickerCard()
    {
        KickerAbilityDetector.HasKickerAbility(null).Should().BeFalse();
    }

    [Fact]
    public void PlainCreature_HasNoKickerAbility()
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        KickerAbilityDetector.HasKickerAbility(c).Should().BeFalse();
    }

    [Fact]
    public void CardWithKickerKeywordMarker_HasKickerAbility()
    {
        var c = new Instant("Test Kicker Spell", "{G}");
        c.AddAbility(new KeywordAbility(KickerAbilityDetector.KickerKeyword, c, _alice));

        KickerAbilityDetector.HasKickerAbility(c).Should().BeTrue();
    }

    [Fact]
    public void CardWithMultikickerKeywordMarker_HasKickerAbility()
    {
        var c = new Instant("Test Multikicker Spell", "{G}");
        c.AddAbility(new KeywordAbility(KickerAbilityDetector.MultikickerKeyword, c, _alice));

        KickerAbilityDetector.HasKickerAbility(c).Should().BeTrue();
    }

    [Fact]
    public void KeywordMatch_IsCaseInsensitive()
    {
        var c = new Instant("Test", "{G}");
        c.AddAbility(new KeywordAbility("kicker", c, _alice));

        KickerAbilityDetector.HasKickerAbility(c).Should().BeTrue();
    }

    [Fact]
    public void RealKickerCard_VinesOfVastwood_HasKickerAbility()
    {
        // Vines of Vastwood prints Kicker {G} — the engine attaches a
        // "Kicker" KeywordAbility marker via WithKeyword. End-to-end check
        // that a real kicker card is detected.
        var vines = VinesOfVastwoodFactory.Create(_alice);

        KickerAbilityDetector.HasKickerAbility(vines).Should().BeTrue();
    }

    [Fact]
    public void NonKickerKeyword_DoesNotMatch()
    {
        // A card with some OTHER keyword (Flying) is not a kicker card.
        var c = new Creature("Flyer", "{1}{U}", 1, 1);
        c.AddAbility(new KeywordAbility("Flying", c, _alice));

        KickerAbilityDetector.HasKickerAbility(c).Should().BeFalse();
    }
}
