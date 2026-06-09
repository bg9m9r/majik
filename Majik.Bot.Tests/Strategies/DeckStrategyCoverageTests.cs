using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Strategies;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

/// <summary>
/// Tripwire tests that guard strategy coverage as authored decks grow:
///
/// <list type="number">
///   <item><b>Coverage tripwire</b> — every archetype in
///   <see cref="BotDeckCatalog.Archetypes"/> must either have a registered
///   <see cref="IDeckStrategy"/> OR appear in
///   <see cref="FairDecksWithoutStrategy"/>. A new deck whose
///   <c>[DeckStrategy]</c> name is typo'd shows up as "no strategy AND not in
///   allow-list" → test fails. As strategies are authored, remove the name from
///   the allow-list. A name that is in the allow-list AND has a strategy →
///   also fails (keep the allow-list honest).</item>
///
///   <item><b>Card-name validity</b> — for every archetype that HAS a strategy,
///   every name in <see cref="IDeckStrategy.ReferencedCardNames"/> must exist in
///   that archetype's <see cref="BotDeckCatalog.Get"/> card list. Vacuously
///   passes until strategies are authored; bites the moment a strategy
///   references a misspelled or wrong-deck card name.</item>
/// </list>
///
/// <para>The registry scan uses the default <c>DeckStrategyRegistry.For(name)</c>
/// overload, which scans <c>typeof(IDeckStrategy).Assembly</c> = Majik.Bot
/// (production strategies). The test assembly is excluded so stubs defined in
/// tests (e.g. <c>TestDeckStrategy</c>) do not count as coverage.</para>
/// </summary>
public sealed class DeckStrategyCoverageTests
{
    /// <summary>
    /// Archetypes that are intentionally allowed to have no authored strategy.
    /// Remove a name from here once its <c>[DeckStrategy]</c> class is merged.
    /// Every name in this set must be a known archetype; stale entries (names
    /// no longer in <see cref="BotDeckCatalog.Archetypes"/>) also fail.
    /// </summary>
    private static readonly HashSet<string> FairDecksWithoutStrategy =
    [
        "Burn",
        "Prowess",
        "BorosEnergy",
        "Yawg",
        "Affinity",
        "RubyStorm",
        "Belcher",
        "GoryoVengeance",
        "LivingEnd",
        "EldraziTron",
        "DimirMidrange",
        "EldraziRamp",
        "Neobrand",
        "EsperBlink",
        "SultaiMidrange",
        "MonoBlackMidrange",
        "AzoriusBlink",
        "AzoriusControl",
        "BorosLandDestruction",
        "Rhinos",
        "DomainZoo",
        "GruulBroodscale",
        "EldraziBroodscale",
    ];

    // ── Coverage tripwire ──────────────────────────────────────────────────────

    [Fact]
    public void AllArchetypes_HaveStrategyOrAreInAllowList()
    {
        // Decks that have neither a strategy NOR an allow-list entry are the
        // failure mode: a new deck added to BotDeckCatalog without a strategy
        // and without being added to FairDecksWithoutStrategy → caught here.
        var uncovered = BotDeckCatalog.Archetypes
            .Where(name =>
                DeckStrategyRegistry.For(name) is null
                && !FairDecksWithoutStrategy.Contains(name))
            .ToList();

        uncovered.Should().BeEmpty(
            "every archetype must either have a [DeckStrategy] class registered " +
            "in Majik.Bot or be listed in FairDecksWithoutStrategy; " +
            "uncovered archetypes: {0}", string.Join(", ", uncovered));
    }

    [Fact]
    public void AllowList_ContainsOnlyKnownArchetypes()
    {
        // Stale allow-list entries (archetype removed from BotDeckCatalog) → fail.
        var known = BotDeckCatalog.Archetypes.ToHashSet();
        var stale = FairDecksWithoutStrategy.Where(name => !known.Contains(name)).ToList();

        stale.Should().BeEmpty(
            "FairDecksWithoutStrategy contains names that are no longer " +
            "registered archetypes in BotDeckCatalog; remove: {0}",
            string.Join(", ", stale));
    }

    [Fact]
    public void AllowList_DoesNotContainArchetypesWithStrategies()
    {
        // A deck that now has a strategy must be removed from the allow-list so
        // the list does not go stale and mask future coverage gaps.
        var staleEntries = FairDecksWithoutStrategy
            .Where(name => DeckStrategyRegistry.For(name) is not null)
            .ToList();

        staleEntries.Should().BeEmpty(
            "the following archetypes have a registered IDeckStrategy but are " +
            "still listed in FairDecksWithoutStrategy — remove them from the " +
            "allow-list so future coverage gaps are not masked: {0}",
            string.Join(", ", staleEntries));
    }

    // ── Card-name validity ─────────────────────────────────────────────────────

    [Fact]
    public void StrategiesWithReferencedCards_OnlyReferenceCardsInTheirDeck()
    {
        // For every archetype that has a strategy, every name in
        // ReferencedCardNames must exist in that archetype's BotDeckCatalog list.
        var violations = new List<string>();

        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var strategy = DeckStrategyRegistry.For(archetype);
            if (strategy is null) continue;   // no strategy yet — vacuously fine

            var deckCards = BotDeckCatalog.Get(archetype).ToHashSet(StringComparer.Ordinal);

            foreach (var referencedName in strategy.ReferencedCardNames)
            {
                if (!deckCards.Contains(referencedName))
                {
                    violations.Add(
                        $"'{archetype}': strategy references '{referencedName}' " +
                        $"which is not in the deck's BotDeckCatalog card list");
                }
            }
        }

        violations.Should().BeEmpty(
            "every card name in IDeckStrategy.ReferencedCardNames must appear " +
            "in the corresponding archetype's BotDeckCatalog card list; violations: {0}",
            string.Join("; ", violations));
    }
}
