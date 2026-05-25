using System.Text.Json;
using FluentAssertions;
using Majik.Console.Commands;
using Xunit;

namespace Majik.Core.Tests.Console;

/// <summary>
/// Tests for the <c>dashboard</c> subcommand. The IO-bearing
/// <see cref="DashboardCommand.RunAsync"/> shim is exercised by the
/// dashboard PR smoke run; these tests pin down the pure-render layer
/// (<see cref="DashboardRenderer"/>) on representative fixtures so the
/// markdown shape is stable across refactors.
/// </summary>
public class DashboardCommandTests
{
    // ----------------------- Render: full happy path -----------------------

    [Fact]
    public void Render_FullHappyPath_EmitsAllSectionsAndMetrics()
    {
        var coverage = JsonDocument.Parse("""
        {
          "total_cards": 21917,
          "covered_cards": 6027,
          "covered_percent": 27.5,
          "frequency_weighted_covered_percent": 97.5,
          "top_meta_covered": 20,
          "top_meta_total": 20,
          "counts_by_tier": {
            "NamedFactory": 310,
            "SpellBound": 121,
            "KeywordOnly": 1411,
            "Vanilla": 629,
            "Unimplemented": 15890
          },
          "top_meta": [
            { "name": "Consign to Memory",  "weight": 530, "tier": "NamedFactory" },
            { "name": "Sink into Stupor",   "weight": 340, "tier": "Unimplemented" },
            { "name": "Lightning Bolt",     "weight": 300, "tier": "SpellBound" }
          ]
        }
        """).RootElement;

        var gaps = JsonDocument.Parse("""
        {
          "clusters": [
            { "first_sentence_signature": "{cost}: add {cost}", "member_count": 331 },
            { "first_sentence_signature": "~ enters tapped",     "member_count": 303 },
            { "first_sentence_signature": "equipped creature gets +n/+n", "member_count": 98 }
          ]
        }
        """).RootElement;

        var deps = JsonDocument.Parse("""
        {
          "clusters": [
            {
              "primitive_id": "agent-prompt-targeting",
              "display_name": "Agent-prompt targeting MVP",
              "factory_count": 22,
              "mention_count": 28
            },
            {
              "primitive_id": "library-search",
              "display_name": "Library-search restriction slot",
              "factory_count": 4,
              "mention_count": 5
            }
          ]
        }
        """).RootElement;

        var meta = JsonDocument.Parse("""
        {
          "format": "modern",
          "cards": [
            { "name": "Consign to Memory", "decks": 530 },
            { "name": "Mystical Dispute",  "decks": 530 }
          ]
        }
        """).RootElement;

        var velocity = new List<VelocityRow>
        {
            new(new DateOnly(2026, 5, 25), Prs: 18, Cards: 11, Primitives: 7),
            new(new DateOnly(2026, 5, 24), Prs: 24, Cards: 12, Primitives: 12),
        };

        var archetypes = new List<ArchetypeRow>
        {
            new("Burn", "~100%"),
            new("Merfolk", "~85%"),
            new("Boros Energy", "~40%"),
        };

        var md = DashboardRenderer.Render(new DashboardInput
        {
            Mode = "Modern",
            GeneratedUtc = new DateTime(2026, 5, 25, 14, 32, 0, DateTimeKind.Utc),
            Coverage = coverage,
            Gaps = gaps,
            MechanicDeps = deps,
            MetaSnapshot = meta,
            ShippingVelocity = velocity,
            ArchetypeRollups = archetypes,
        });

        md.Should().Contain("# Majik Coverage Dashboard");
        md.Should().Contain("**Last generated:** 2026-05-25 14:32 UTC");
        md.Should().Contain("**Mode:** Modern");

        // Headline numbers
        md.Should().Contain("Raw coverage");
        md.Should().Contain("27.5% (6027 / 21917)");
        md.Should().Contain("97.5%");
        md.Should().Contain("Top-20 most-played covered");
        md.Should().Contain("20/20");
        md.Should().Contain("| Named factories              | 310 |");
        md.Should().Contain("| Spell templates              | 121 |");

        // Mechanic-deps
        md.Should().Contain("Top open mechanic-deps clusters");
        md.Should().Contain("| 1 | Agent-prompt targeting MVP | 22 | 28 |");
        md.Should().Contain("| 2 | Library-search restriction slot | 4 | 5 |");

        // Gap clusters
        md.Should().Contain("Top unimplemented mechanic patterns");
        md.Should().Contain("`{cost}: add {cost}`");
        md.Should().Contain("| 331 |");

        // Top unimplemented weighted
        md.Should().Contain("Top unimplemented tournament-weighted cards");
        md.Should().Contain("Sink into Stupor");
        md.Should().Contain("| 340 |");
        md.Should().NotContain("Consign to Memory | NamedFactory"); // shouldn't appear in unimplemented table

        // Velocity
        md.Should().Contain("Recent shipping velocity");
        md.Should().Contain("| 2026-05-25 | 18 | 11 | 7 |");
        md.Should().Contain("| 2026-05-24 | 24 | 12 | 12 |");

        // Archetypes
        md.Should().Contain("Archetype rollups");
        md.Should().Contain("| Burn | ~100% |");
        md.Should().Contain("| Merfolk | ~85% |");
    }

    // ----------------------- Render: empty / missing inputs ----------------

    [Fact]
    public void Render_AllInputsEmpty_ProducesGracefulEmptySections()
    {
        var md = DashboardRenderer.Render(new DashboardInput
        {
            Mode = "Modern",
            GeneratedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        md.Should().Contain("# Majik Coverage Dashboard");
        md.Should().Contain("_No coverage data");
        md.Should().Contain("_No mechanic-deps data._");
        md.Should().Contain("_No gap-cluster data._");
        md.Should().Contain("_No feat() commits in the last 7 days._");
        md.Should().Contain("_No archetype rollups parsed._");
    }

    [Fact]
    public void Render_EmptyClustersArrays_RendersThresholdNote()
    {
        var coverage = JsonDocument.Parse("""
        { "total_cards": 100, "covered_cards": 10, "covered_percent": 10.0,
          "frequency_weighted_covered_percent": 0.0, "top_meta": [],
          "counts_by_tier": {} }
        """).RootElement;
        var deps = JsonDocument.Parse("""{ "clusters": [] }""").RootElement;
        var gaps = JsonDocument.Parse("""{ "clusters": [] }""").RootElement;

        var md = DashboardRenderer.Render(new DashboardInput
        {
            Coverage = coverage,
            MechanicDeps = deps,
            Gaps = gaps,
        });

        md.Should().Contain("_No clusters above threshold._");
    }

    // ----------------------- Velocity parser -------------------------------

    [Fact]
    public void ParseVelocity_RollsUpByDateAndBuckets()
    {
        // %h|%ci|%s, sample dump
        const string gitOutput =
            "abc123|2026-05-25 12:00:00 -0400|feat(card): Lightning Bolt\n"
          + "def456|2026-05-25 11:00:00 -0400|feat(card): Counterspell\n"
          + "ghi789|2026-05-25 10:00:00 -0400|feat(infra): new primitive\n"
          + "jkl012|2026-05-24 09:00:00 -0400|feat(rules): SBA bugfix\n"
          + "mno345|2026-05-24 08:00:00 -0400|feat(card): Brainstorm\n"
          + "pqr678|2026-05-24 07:00:00 -0400|feat(infra): another primitive\n";

        var rows = DashboardRenderer.ParseVelocity(gitOutput);

        rows.Should().HaveCount(2);
        // Newest first
        rows[0].Date.Should().Be(new DateOnly(2026, 5, 25));
        rows[0].Prs.Should().Be(3);
        rows[0].Cards.Should().Be(2);
        rows[0].Primitives.Should().Be(1);

        rows[1].Date.Should().Be(new DateOnly(2026, 5, 24));
        rows[1].Prs.Should().Be(3);
        rows[1].Cards.Should().Be(1);
        rows[1].Primitives.Should().Be(2); // feat(infra) + feat(rules)
    }

    [Fact]
    public void ParseVelocity_EmptyInput_ReturnsEmptyList()
    {
        DashboardRenderer.ParseVelocity("").Should().BeEmpty();
        DashboardRenderer.ParseVelocity("   \n\n").Should().BeEmpty();
    }

    [Fact]
    public void ParseVelocity_GarbageLines_AreSkippedSilently()
    {
        var rows = DashboardRenderer.ParseVelocity(
            "not-a-commit-line\n"
          + "abc|bad-date|feat(card): foo\n"
          + "def|2026-05-25 10:00:00 -0400|feat(card): bar\n");
        rows.Should().HaveCount(1);
        rows[0].Cards.Should().Be(1);
    }

    // ----------------------- Archetype rollup parser -----------------------

    [Fact]
    public void ParseArchetypeRollups_ExtractsNameAndFinalPercent()
    {
        const string md = """
        # Modern Coverage

        ## Headline numbers

        Some headline content.

        ## Coverage by archetype

        - **Burn** — Saturated. Lightning Bolt, Lava Spike. ~100%.
        - **Merfolk** — Mid-high. Aether Vial done (~50%), Lord of Atlantis. ~85%.
        - **Boros Energy / Boros Convoke** — Mid. Several pieces. ~40%.
        - **Unknown** — no coverage info.

        ## Top 20 staples NOT yet implemented

        - **Should not be parsed** — different section.
        """;

        var rows = DashboardRenderer.ParseArchetypeRollups(md);

        rows.Should().HaveCount(4);
        rows[0].Name.Should().Be("Burn");
        rows[0].Coverage.Should().Be("~100%");
        rows[1].Name.Should().Be("Merfolk");
        rows[1].Coverage.Should().Be("~85%"); // last percent wins, not the ~50% one
        rows[2].Name.Should().Be("Boros Energy / Boros Convoke");
        rows[2].Coverage.Should().Be("~40%");
        rows[3].Name.Should().Be("Unknown");
        rows[3].Coverage.Should().Be("—");
    }

    [Fact]
    public void ParseArchetypeRollups_EmptyOrMissingSection_ReturnsEmpty()
    {
        DashboardRenderer.ParseArchetypeRollups("").Should().BeEmpty();
        DashboardRenderer.ParseArchetypeRollups("# Just a title\n\nNo archetypes here.").Should().BeEmpty();
    }
}
