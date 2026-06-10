using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for <see cref="DirectDamageRecognizer"/> — the cached oracle-text scan
/// that recognizes how much DIRECT damage a card can deal to a player.
///
/// Cards are built via the prod-equivalent <see cref="ScryfallCardFactory"/> path
/// (same fixture pattern as <see cref="DeterminizationSamplerTests"/>), so the
/// recognizer is exercised against the REAL embedded oracle text — expectations
/// reflect what the regex genuinely extracts, with no name special-cases:
/// <list type="bullet">
///   <item>Lightning Bolt — "deals 3 damage to any target" → 3.</item>
///   <item>Lava Spike — "deals 3 damage to target player or planeswalker" → 3.</item>
///   <item>Boros Charm — modal; the burn mode reads "Boros Charm deals 4 damage
///     to target player or planeswalker." → 4.</item>
///   <item>Grizzly Bears — no oracle text at all → 0.</item>
///   <item>Island — "({T}: Add {U}.)" → 0.</item>
///   <item>Flame Slash — "deals 4 damage to target creature." — creature-only
///     damage can NOT reach a player → 0.</item>
/// </list>
/// </summary>
public class DirectDamageRecognizerTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name) => Factory.Create(name, new Player("Owner", 20));

    [Theory]
    [InlineData("Lightning Bolt", 3)]   // "deals 3 damage to any target"
    [InlineData("Lava Spike", 3)]       // "deals 3 damage to target player or planeswalker"
    [InlineData("Boros Charm", 4)]      // modal: "• Boros Charm deals 4 damage to target player or planeswalker."
    [InlineData("Grizzly Bears", 0)]    // vanilla creature — no oracle text
    [InlineData("Island", 0)]           // mana ability only
    public void DamageToPlayer_ExtractsBurnReach_FromRealOracleText(string name, int expected)
        => DirectDamageRecognizer.DamageToPlayer(Build(name)).Should().Be(expected);

    [Fact]
    public void DamageToPlayer_CreatureOnlyDamage_DoesNotCount()
        // Flame Slash: "Flame Slash deals 4 damage to target creature." — direct
        // damage, but it can never hit a player, so it is NOT burn reach.
        => DirectDamageRecognizer.DamageToPlayer(Build("Flame Slash")).Should().Be(0);

    [Fact]
    public void DamageToPlayer_IsCachedPerName_ConsistentAcrossCalls()
    {
        var first = DirectDamageRecognizer.DamageToPlayer(Build("Lightning Bolt"));
        var second = DirectDamageRecognizer.DamageToPlayer(Build("Lightning Bolt"));
        first.Should().Be(3);
        second.Should().Be(first);
    }
}
