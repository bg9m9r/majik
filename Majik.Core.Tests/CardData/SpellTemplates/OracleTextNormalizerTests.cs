using FluentAssertions;
using Majik.Core.CardData.SpellTemplates;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Covers <see cref="OracleTextNormalizer"/> leading-prefix strips. Each test
/// uses a real Scryfall-style oracle text snippet for the keyword and asserts
/// the post-strip text starts at the actual spell effect (so a regex template
/// can anchor at it). v1 strips are intentionally lossy — the keyword's cost
/// or timing semantic is not enforced; only the reminder/cost prefix is
/// removed so binding succeeds.
/// </summary>
public class OracleTextNormalizerTests
{
    [Fact]
    public void Normalize_StripsConvokeReminder()
    {
        var input =
            "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\n" +
            "Destroy target creature.";

        OracleTextNormalizer.Normalize(input).Should().Be("Destroy target creature.");
    }

    [Fact]
    public void Normalize_StripsSplitSecondReminder()
    {
        var input =
            "Split second (As long as this spell is on the stack, players can't cast spells or activate abilities that aren't mana abilities.)\n" +
            "Counter target spell.";

        OracleTextNormalizer.Normalize(input).Should().Be("Counter target spell.");
    }

    [Fact]
    public void Normalize_StripsStriveCostSentence()
    {
        var input =
            "Strive — This spell costs {1}{W} more to cast for each target beyond the first.\n" +
            "Any number of target creatures each get +1/+1 until end of turn.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Any number of target creatures each get +1/+1 until end of turn.");
    }

    [Fact]
    public void Normalize_StripsCipherReminder()
    {
        var input =
            "Cipher (Then you may exile this spell card encoded on a creature you control.)\n" +
            "Target player discards a card.";

        OracleTextNormalizer.Normalize(input).Should().Be("Target player discards a card.");
    }

    [Fact]
    public void Normalize_StripsAdditionalCostSentence()
    {
        var input =
            "As an additional cost to cast this spell, sacrifice a creature.\n" +
            "Destroy target land.";

        OracleTextNormalizer.Normalize(input).Should().Be("Destroy target land.");
    }

    [Fact]
    public void Normalize_StripsLeadingParenthesizedReminder()
    {
        var input =
            "(You may cast a legendary sorcery only if you control a legendary creature or planeswalker.)\n" +
            "Destroy all nonland permanents.";

        OracleTextNormalizer.Normalize(input).Should().Be("Destroy all nonland permanents.");
    }

    [Fact]
    public void Normalize_StripsDelveReminder()
    {
        // Real: Tasigur's Cruelty
        var input =
            "Delve (Each card you exile from your graveyard while casting this spell pays for {1}.)\n" +
            "Each opponent discards two cards.";

        OracleTextNormalizer.Normalize(input).Should().Be("Each opponent discards two cards.");
    }

    [Fact]
    public void Normalize_StripsBargainReminder()
    {
        // Real: Beseech the Mirror (truncated body)
        var input =
            "Bargain (You may sacrifice an artifact, enchantment, or token as you cast this spell.)\n" +
            "Search your library for a card, exile it face down, then shuffle.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Search your library for a card, exile it face down, then shuffle.");
    }

    [Fact]
    public void Normalize_StripsAffinityForReminder()
    {
        // Real: Blinkmoth Infusion
        var input =
            "Affinity for artifacts (This spell costs {1} less to cast for each artifact you control.)\n" +
            "Untap all artifacts.";

        OracleTextNormalizer.Normalize(input).Should().Be("Untap all artifacts.");
    }

    [Fact]
    public void Normalize_StripsAffinityForCustomNoun()
    {
        // Real: Polliwallop (Affinity for Frogs)
        var input =
            "Affinity for Frogs (This spell costs {1} less to cast for each Frog you control.)\n" +
            "Target creature you control deals damage equal to twice its power to target creature you don't control.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Target creature you control deals damage equal to twice its power to target creature you don't control.");
    }

    [Fact]
    public void Normalize_StripsSuspendCostWithoutReminder()
    {
        // Real: Living End
        var input =
            "Suspend 3—{2}{B}{B}\n" +
            "Each player exiles all creature cards from their graveyard.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Each player exiles all creature cards from their graveyard.");
    }

    [Fact]
    public void Normalize_StripsSuspendCostWithReminder()
    {
        // Real: Wheel of Fate (truncated reminder)
        var input =
            "Suspend 4—{1}{R} (Rather than cast this card from your hand, pay {1}{R} and exile it with four time counters on it.)\n" +
            "Each player discards their hand, then draws seven cards.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Each player discards their hand, then draws seven cards.");
    }

    [Fact]
    public void Normalize_LeavesPlainTextAlone()
    {
        const string plain = "Destroy target creature. It can't be regenerated.";

        OracleTextNormalizer.Normalize(plain).Should().Be(plain);
    }

    [Fact]
    public void Normalize_HandlesNullOrEmptyInput()
    {
        OracleTextNormalizer.Normalize("").Should().Be("");
        OracleTextNormalizer.Normalize(null!).Should().BeNull();
    }
}
