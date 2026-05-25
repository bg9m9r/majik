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

    // -- New behaviour: mid-text paren reminders are stripped ---------------

    [Fact]
    public void Normalize_StripsMidTextParenReminder()
    {
        var input = "Trample (This creature can deal excess combat damage to defending player.)";

        OracleTextNormalizer.Normalize(input).Should().Be("Trample");
    }

    [Fact]
    public void Normalize_StripsMultipleMidTextParenReminders()
    {
        var input =
            "When ~ enters, draw a card. Cycling {2} (You may pay this and discard this card to draw a card.)";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("When ~ enters, draw a card. Cycling {2}");
    }

    // -- New behaviour: en-dash / double-hyphen / horizontal-bar → em-dash --

    [Theory]
    [InlineData("Strive – This spell costs {1} more.\nDraw a card.", "Draw a card.")]
    [InlineData("Strive -- This spell costs {1} more.\nDraw a card.", "Draw a card.")]
    [InlineData("Strive ― This spell costs {1} more.\nDraw a card.", "Draw a card.")]
    public void Normalize_RewritesDashVariantsToEmDashAndStripsStrive(string input, string expected)
    {
        OracleTextNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_RewritesEnDashInBody()
    {
        var input = "Choose one – Destroy target creature.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Choose one — Destroy target creature.");
    }

    // -- New behaviour: curly quotes → ASCII ---------------------------------

    [Fact]
    public void Normalize_RewritesCurlyQuotesToAscii()
    {
        var input = "Cast “Golem” and ‘activate’ it.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Cast \"Golem\" and 'activate' it.");
    }

    // -- New behaviour: whitespace collapse ----------------------------------

    [Fact]
    public void Normalize_CollapsesWhitespaceAndTrims()
    {
        var input = "   Destroy   target\ncreature.\n  ";

        OracleTextNormalizer.Normalize(input).Should().Be("Destroy target creature.");
    }

    // -- New leading-prefix coverage -----------------------------------------

    [Fact]
    public void Normalize_StripsCasualty()
    {
        var input =
            "Casualty 2 (As you cast this spell, you may sacrifice a creature with power 2 or greater. When you do, copy this spell.)\n" +
            "Counter target spell.";

        OracleTextNormalizer.Normalize(input).Should().Be("Counter target spell.");
    }

    [Fact]
    public void Normalize_StripsDemonstrate()
    {
        var input =
            "Demonstrate (When you cast this spell, you may copy it. If you do, choose an opponent to also copy it.)\n" +
            "Target player draws two cards.";

        OracleTextNormalizer.Normalize(input).Should().Be("Target player draws two cards.");
    }

    [Fact]
    public void Normalize_StripsDisturbCost()
    {
        var input =
            "Disturb {1}{W} (You may cast this card from your graveyard transformed for its disturb cost.)\n" +
            "Target creature gets +2/+2 until end of turn.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("Target creature gets +2/+2 until end of turn.");
    }

    [Fact]
    public void Normalize_StripsForetell()
    {
        var input =
            "Foretell {1}{U} (During your turn, you may pay {2} and exile this card from your hand face down. Cast it on a later turn for its foretell cost.)\n" +
            "Draw two cards.";

        OracleTextNormalizer.Normalize(input).Should().Be("Draw two cards.");
    }

    [Fact]
    public void Normalize_StripsReplicate()
    {
        var input =
            "Replicate {1}{R} (When you cast this spell, copy it for each time you paid its replicate cost. You may choose new targets for the copies.)\n" +
            "~ deals 2 damage to any target.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("~ deals 2 damage to any target.");
    }

    [Fact]
    public void Normalize_StripsBuyback()
    {
        var input =
            "Buyback {3} (You may pay an additional {3} as you cast this spell. If you do, put it into its owner's hand as it resolves.)\n" +
            "Counter target spell.";

        OracleTextNormalizer.Normalize(input).Should().Be("Counter target spell.");
    }

    [Fact]
    public void Normalize_StripsStorm()
    {
        var input =
            "Storm (When you cast this spell, copy it for each spell cast before it this turn. You may choose new targets for the copies.)\n" +
            "~ deals 3 damage to any target.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("~ deals 3 damage to any target.");
    }

    [Fact]
    public void Normalize_StripsOverload()
    {
        var input =
            "Overload {4}{R}{R} (You may cast this spell for its overload cost. If you do, change its text by replacing all instances of \"target\" with \"each.\")\n" +
            "~ deals 3 damage to target creature.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("~ deals 3 damage to target creature.");
    }

    [Fact]
    public void Normalize_StripsSpree()
    {
        var input =
            "Spree (Choose one or more additional costs.)\n" +
            "+ {1} — Target creature gets +1/+1.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("+ {1} — Target creature gets +1/+1.");
    }

    [Fact]
    public void Normalize_StripsSurge()
    {
        var input =
            "Surge {R} (You may cast this spell for its surge cost if you or a teammate has cast another spell this turn.)\n" +
            "~ deals 4 damage to target creature.";

        OracleTextNormalizer.Normalize(input)
            .Should().Be("~ deals 4 damage to target creature.");
    }

    // -- New behaviour: NormalizeForCard substitutes card name with "~" ------

    [Fact]
    public void NormalizeForCard_ReplacesPrintedNameWithTilde()
    {
        var input = "Whenever Goblin Guide attacks, defending player reveals the top card of their library.";

        OracleTextNormalizer.NormalizeForCard(input, "Goblin Guide")
            .Should().Be("Whenever ~ attacks, defending player reveals the top card of their library.");
    }

    [Fact]
    public void NormalizeForCard_ReplacesShortNameBeforeComma()
    {
        // Adventure-style "Front, Back" name — the printed body uses the
        // short (pre-comma) form. Both should rewrite to ~.
        var input = "Bonecrusher Giant deals 2 damage to any target. Bonecrusher Giant gets +1/+0.";

        OracleTextNormalizer.NormalizeForCard(input, "Bonecrusher Giant, Stomp")
            .Should().Be("~ deals 2 damage to any target. ~ gets +1/+0.");
    }

    [Fact]
    public void NormalizeForCard_IsCaseInsensitive()
    {
        var input = "lightning bolt deals 3 damage. LIGHTNING BOLT is fast.";

        OracleTextNormalizer.NormalizeForCard(input, "Lightning Bolt")
            .Should().Be("~ deals 3 damage. ~ is fast.");
    }

    [Fact]
    public void NormalizeForCard_HandlesNullName()
    {
        var input = "Destroy target creature.";

        OracleTextNormalizer.NormalizeForCard(input, null)
            .Should().Be("Destroy target creature.");
    }

    [Fact]
    public void NormalizeForCard_LeavesUnrelatedTextAlone()
    {
        var input = "Counter target spell.";

        OracleTextNormalizer.NormalizeForCard(input, "Cancel")
            .Should().Be("Counter target spell.");
    }

    // -- PR-B: mana-cost token fold -----------------------------------------

    [Theory]
    [InlineData("{X}{R}",              "{cost}")]
    [InlineData("{2}{W}{W}",           "{cost}")]
    [InlineData("{R}",                 "{cost}")]
    [InlineData("{G/U}{2/W}",          "{cost}")]
    [InlineData("{X}{G/P}{B}",         "{cost}")]
    public void FoldTokens_CollapsesAdjacentManaPipsTo_cost(string input, string expected)
    {
        OracleTextNormalizer.FoldTokens(input).Should().Be(expected);
    }

    [Fact]
    public void FoldTokens_LeavesTapAlone()
    {
        // Tap "{T}" is not a mana cost. Single-pip mana payments do fold
        // ({2} below), but {T} must stay verbatim so activated-ability
        // patterns like "{T}: Add {G}." keep their shape.
        OracleTextNormalizer.FoldTokens("{T}, Pay {2}: Draw a card.")
            .Should().Be("{T}, Pay {cost}: Draw a card.");
    }

    [Fact]
    public void FoldTokens_LeavesUntapQAlone()
    {
        // "{Q}" is the untap symbol — not mana. Must not fold to {cost}.
        OracleTextNormalizer.FoldTokens("{Q}: Add {W}.")
            .Should().Be("{Q}: Add {cost}.");
    }

    [Fact]
    public void FoldTokens_FoldsHybridAndPhyrexianRuns()
    {
        OracleTextNormalizer.FoldTokens("Cast for {2}{W/U}{B/P}.")
            .Should().Be("Cast for {cost}.");
    }

    [Fact]
    public void FoldTokens_LeavesNonManaBraceGroupsAlone()
    {
        // "{E}" is energy (not mana — it's an energy counter spend in some
        // cards' costs; the pip-body grammar excludes E by design so it
        // doesn't get folded as mana).
        OracleTextNormalizer.FoldTokens("Pay {E}{E}: Draw a card.")
            .Should().Be("Pay {E}{E}: Draw a card.");
    }

    // -- PR-B: numeric "n" token fold ---------------------------------------

    [Theory]
    [InlineData("Target creature gets +1/+1 until end of turn.",
                "Target creature gets +n/+n until end of turn.")]
    [InlineData("Target creature gets -2/-1 until end of turn.",
                "Target creature gets -n/-n until end of turn.")]
    [InlineData("Target creature gets +0/+3 until end of turn.",
                "Target creature gets +n/+n until end of turn.")]
    public void FoldTokens_FoldsPtDeltaToPlusN(string input, string expected)
    {
        OracleTextNormalizer.FoldTokens(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("~ deals 3 damage to any target.",  "~ deals n damage to any target.")]
    [InlineData("~ deals 5 damage to target creature.",  "~ deals n damage to target creature.")]
    [InlineData("~ deals 10 damage to each opponent.",  "~ deals n damage to each opponent.")]
    public void FoldTokens_FoldsNDamage(string input, string expected)
    {
        OracleTextNormalizer.FoldTokens(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Draw 2 cards.",                 "Draw n cards.")]
    [InlineData("Draw 4 cards.",                 "Draw n cards.")]
    [InlineData("Target player discards 1 card.",  "Target player discards n card.")]
    public void FoldTokens_FoldsNCards(string input, string expected)
    {
        OracleTextNormalizer.FoldTokens(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Target opponent loses 4 life.",  "Target opponent loses n life.")]
    [InlineData("You gain 5 life.",               "You gain n life.")]
    public void FoldTokens_FoldsNLife(string input, string expected)
    {
        OracleTextNormalizer.FoldTokens(input).Should().Be(expected);
    }

    [Fact]
    public void FoldTokens_LeavesNumbersInsideManaCostsAlone()
    {
        // The "2" inside {2}{R} is part of the mana cost — it should be
        // consumed by the {cost} fold, not the numeric fold, and the
        // resulting "{cost}" must NOT then be re-folded.
        OracleTextNormalizer.FoldTokens("Cast {2}{R}: ~ deals 3 damage.")
            .Should().Be("Cast {cost}: ~ deals n damage.");
    }

    [Fact]
    public void NormalizeFolded_RunsFullPipelineWithCardNameSubstitution()
    {
        // End-to-end: card-name → ~, parens stripped, dashes folded, mana
        // → {cost}, and "3 damage" → "n damage". This is the production
        // entry-point templates consume via SpellBindContext.TextFolded.
        var input = "Lightning Bolt deals 3 damage to any target.";
        OracleTextNormalizer.NormalizeFolded(input, "Lightning Bolt")
            .Should().Be("~ deals n damage to any target.");
    }

    [Fact]
    public void NormalizeFolded_LeavesNonMatchingTextAlone()
    {
        // Sanity: prose with no numeric or mana payload is untouched.
        OracleTextNormalizer.NormalizeFolded("Counter target spell.", "Cancel")
            .Should().Be("Counter target spell.");
    }

    [Fact]
    public void NormalizeFolded_HandlesNullAndEmpty()
    {
        OracleTextNormalizer.NormalizeFolded("", "Whatever").Should().Be("");
        OracleTextNormalizer.NormalizeFolded(null!, "Whatever").Should().BeNull();
    }

    [Fact]
    public void FoldTokens_LeavesNumbersThatLookLikeDigitsInRandomContext()
    {
        // "2 or more" — the numeric fold is anchored on known nouns only,
        // so prose numbers without a recognised noun stay verbatim.
        OracleTextNormalizer.FoldTokens("Choose a number 2 or greater.")
            .Should().Be("Choose a number 2 or greater.");
    }
}
