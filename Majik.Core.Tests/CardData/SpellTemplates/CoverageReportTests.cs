using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

public class CoverageReportTests
{
    [Fact]
    public void Build_CountsMatched_Unmatched_AndPerTemplateHits()
    {
        var entities = new[]
        {
            // Names are chosen so they DO NOT appear as words in the
            // oracle text — the normalizer rewrites a card's own name to
            // "~" before binding, so reusing a verb-shaped name like
            // "Counter" or a self-referential name like "Bolt" would
            // suppress the very template the test wants to assert on.
            new CardEntity { Name = "Lightning Strike", OracleText = "Lightning Strike deals 3 damage to any target.", TypeLine = "Instant" },
            new CardEntity { Name = "Cancel",           OracleText = "Counter target spell.",                          TypeLine = "Instant" },
            new CardEntity { Name = "Mystery",          OracleText = "Do something the parser cannot match.",          TypeLine = "Sorcery" },
        };

        var report = CoverageReport.Build(entities, OracleSpellBinder.Registry, new Player("X", 20));

        report.Total.Should().Be(3);
        report.Matched.Should().Be(2);
        report.UnmatchedNames.Should().Contain("Mystery");
        report.PerTemplateHits.Should().ContainKey("CounterTargetSpell");
    }

    [Fact]
    public void Build_FiltersOutNonInstantsAndSorceries()
    {
        var entities = new[]
        {
            new CardEntity { Name = "Bear",    OracleText = "",                 TypeLine = "Creature — Bear" },
            new CardEntity { Name = "Counter", OracleText = "Counter target spell.", TypeLine = "Instant" },
        };

        var report = CoverageReport.Build(entities, OracleSpellBinder.Registry, new Player("X", 20));

        report.Total.Should().Be(1);
        report.UnmatchedNames.Should().NotContain("Bear");
    }
}
