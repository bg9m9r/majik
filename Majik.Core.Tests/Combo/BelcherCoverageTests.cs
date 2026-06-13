using FluentAssertions;
using Majik.Core.CardData;
using Xunit;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// Phase B5 (plan 2026-06-13) — the per-deck COVERAGE note as an executable
/// audit. Maps every one of the 60 Azorius Lotus Belcher cards to the combo
/// line that exercises it, or justifies why it is not combo-relevant (a card
/// touched by no line is a flagged gap). The map is asserted complete against
/// the real deck list, and a count guard catches deck drift (the deck type is
/// internal to Majik.Bot, so <see cref="BelcherLines.DeckCards"/> is a mirror).
///
/// <para>COVERAGE MAP (line → cards):</para>
/// <list type="bullet">
///   <item><b>Goblin Charbelcher</b> — the win-con. B3 (core kill),
///   B4a (hard-cast), B4b (Whir tutor tail), B4c (Lotus Bloom engine),
///   B4d (MDFC back-land).</item>
///   <item><b>Lotus Bloom</b> — the {3}/{4} mana engine. B4c (as the {3}
///   engine), B4a/B4b-tail/B4d (mana source).</item>
///   <item><b>Whir of Invention</b> — artifact tutor to battlefield. B4b
///   (cast gap + tutor tail).</item>
///   <item><b>Hydroelectric Specimen / Jwari Disruption / Sea Gate Restoration
///   / Sink into Stupor / Razorgrass Ambush / Waterlogged Teachings</b> — the
///   MDFC manabase: nonland by front face in the library (the combo's core
///   premise). MdfcLibraryProbeTests proves each is nonland in library; they
///   are the lethal reveal fodder in B3/B4; Sink into Stupor's back land
///   (Soporific Springs) is played + tapped in B4d.</item>
///   <item><b>Preordain / Thundertrap Trainer / Tamiyo, Inquisitive Student</b>
///   — card selection / dig. NOT part of the kill line itself (they find the
///   pieces; the line tests assume the pieces are found). Justified
///   not-combo-relevant for the engine-correctness floor.</item>
///   <item><b>Disrupting Shoal / Force of Negation / Stern Scolding / Orim's
///   Chant / Suppression Ray / Tameshi, Reality Architect</b> — protection /
///   disruption / grind. They protect the combo turn or provide a backup plan;
///   they are not steps in the kill line. Justified not-combo-relevant.</item>
/// </list>
/// </summary>
public sealed class BelcherCoverageTests
{
    private enum Role { WinCon, ManaEngine, Tutor, MdfcManabase, CardSelection, Protection }

    // Each distinct deck card → (role, line(s) that exercise it or the
    // justification for not-combo-relevant).
    private static readonly IReadOnlyDictionary<string, (Role role, string line)> Coverage =
        new Dictionary<string, (Role, string)>
        {
            ["Goblin Charbelcher"]        = (Role.WinCon,       "B3 + B4a/b/c/d — the kill activation"),
            ["Lotus Bloom"]               = (Role.ManaEngine,   "B4c (as {3} engine) + B4a/b-tail/d (mana)"),
            ["Whir of Invention"]         = (Role.Tutor,        "B4b — cast gap (deferred) + tutor tail"),

            ["Hydroelectric Specimen"]    = (Role.MdfcManabase, "MdfcLibraryProbe + B3/B4 reveal fodder"),
            ["Jwari Disruption"]          = (Role.MdfcManabase, "MdfcLibraryProbe + B3/B4 reveal fodder"),
            ["Sea Gate Restoration"]      = (Role.MdfcManabase, "MdfcLibraryProbe + B3/B4 reveal fodder"),
            ["Sink into Stupor"]          = (Role.MdfcManabase, "MdfcLibraryProbe + B4d back-land played"),
            ["Razorgrass Ambush"]         = (Role.MdfcManabase, "MdfcLibraryProbe + B3/B4 reveal fodder"),
            ["Waterlogged Teachings"]     = (Role.MdfcManabase, "MdfcLibraryProbe + B3/B4 reveal fodder"),

            ["Preordain"]                 = (Role.CardSelection, "not-combo-relevant: digs for pieces, not a kill step"),
            ["Thundertrap Trainer"]       = (Role.CardSelection, "not-combo-relevant: digs for pieces, not a kill step"),
            ["Tamiyo, Inquisitive Student"] = (Role.CardSelection, "not-combo-relevant: value flip-planeswalker"),

            ["Disrupting Shoal"]          = (Role.Protection,    "not-combo-relevant: free counter protecting the combo turn"),
            ["Force of Negation"]         = (Role.Protection,    "not-combo-relevant: free counter protecting the combo turn"),
            ["Stern Scolding"]            = (Role.Protection,    "not-combo-relevant: cheap counter"),
            ["Orim's Chant"]              = (Role.Protection,    "not-combo-relevant: tax/lock the opponent turn"),
            ["Suppression Ray"]           = (Role.Protection,    "not-combo-relevant: tax/lock"),
            ["Tameshi, Reality Architect"] = (Role.Protection,   "not-combo-relevant: grind/backup (registered partial)"),
        };

    [Fact]
    public void EveryDeckCard_IsMappedToALineOrJustified()
    {
        var distinct = BelcherLines.DeckCards.Distinct().ToList();

        // Every distinct deck card must appear in the coverage map (mapped to a
        // line OR explicitly justified as not-combo-relevant). A card missing
        // here is a flagged gap (the spec's "card touched by no line").
        foreach (var name in distinct)
        {
            Coverage.Should().ContainKey(name,
                $"every deck card must be mapped to a combo line or justified — '{name}' is not");
        }

        // No stale map entries (a name in the map that left the deck).
        foreach (var name in Coverage.Keys)
        {
            distinct.Should().Contain(name,
                $"coverage map entry '{name}' is not in the deck — remove or re-sync");
        }
    }

    [Fact]
    public void DeckList_Matches_RealDeck_Count_AndIsKnownToRepo()
    {
        // Drift guard: the real AzoriusLotusBelcher deck has exactly 60 cards.
        // If this fails, re-sync BelcherLines.DeckCards from
        // Majik.Bot/Decks/AzoriusLotusBelcherDeck.cs.
        BelcherLines.DeckCards.Should().HaveCount(60,
            "the real AzoriusLotusBelcher mainboard is 60 cards (re-sync BelcherLines if this fails)");

        // Every card name resolves in the embedded repo (front-face names for
        // MDFCs resolve to the composite entity).
        var repo = new EmbeddedCardRepository();
        foreach (var name in BelcherLines.DeckCards.Distinct())
        {
            repo.GetByName(name).Should().NotBeNull(
                $"'{name}' must resolve in the embedded card pool");
        }
    }

    [Fact]
    public void EveryCombatRole_HasAtLeastOneCard()
    {
        // Sanity: the map covers all five combo roles + protection, so the
        // coverage is structurally complete (no role silently empty).
        foreach (Role role in System.Enum.GetValues<Role>())
        {
            Coverage.Values.Should().Contain(v => v.role == role,
                $"the coverage map should classify at least one card under {role}");
        }
    }
}
