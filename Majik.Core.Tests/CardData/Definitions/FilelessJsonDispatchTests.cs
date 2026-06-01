using System.Reflection;
using System.Text;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Tests.Snapshots;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Golden-parity guard for PLAN 03 Slice 3 — JSON cards are now <b>fileless</b>:
/// the ~118 hand-written wrapper <c>*Factory</c> classes were deleted and the
/// source generator (<c>NamedCardFactoryGenerator</c>) emits the dispatch arm
/// per <c>CardData/Cards/*.json</c> directly.
///
/// <para>
/// Each deleted wrapper used to do exactly
/// <c>(Cast)CardDefinitionFactory.Build(FromEmbeddedResource("slug"), owner)</c>.
/// The generated arm calls the identical
/// <c>CardDefinitionFactory.Build(FromEmbeddedResource("slug"), owner)</c>, so
/// dispatching a fileless card by name through the production
/// <see cref="NamedCardFactory.Create(string, Player)"/> path must yield a
/// runtime <see cref="ICard"/> whose <see cref="SnapshotSummary"/> digest is
/// byte-identical to building the same embedded JSON definition directly. This
/// proves the wrapper deletion changed nothing about the produced card.
/// </para>
/// </summary>
public class FilelessJsonDispatchTests
{
    /// <summary>
    /// For every fileless JSON card name the generator emitted, the
    /// dispatch-by-name path and the direct JSON build path must produce a
    /// byte-identical card digest. (Basic lands additionally pass through
    /// <c>NamedCardFactory</c>'s idempotent basic-mana attach — the JSON
    /// already carries the intrinsic mana ability, so the digests still match.)
    /// </summary>
    [Fact]
    public void EveryFilelessJsonCard_DispatchByName_MatchesDirectBuild()
    {
        var mismatches = new StringBuilder();
        var checkedCount = 0;

        foreach (var name in NamedCardFactory.GeneratedJsonCardNames)
        {
            var slug = SlugForName(name);
            slug.Should().NotBeNull(
                $"every generated JSON card name (\"{name}\") must map to an embedded resource");

            // Direct JSON build — the exact body of the deleted wrapper.
            var def = CardDefinitionLoader.FromEmbeddedResource(slug!);
            var direct = CardDefinitionFactory.Build(def, new Player("Alice", 20));

            // Production dispatch — through the generated fileless arm.
            var dispatched = NamedCardFactory.Create(name, new Player("Alice", 20));

            var directDigest = SnapshotSummary.Serialize(SnapshotSummary.Build(direct));
            var dispatchedDigest = SnapshotSummary.Serialize(SnapshotSummary.Build(dispatched));

            if (directDigest != dispatchedDigest)
            {
                mismatches.AppendLine($"==== {name} ({slug}) ====");
                mismatches.AppendLine("  direct:");
                mismatches.AppendLine(directDigest);
                mismatches.AppendLine("  dispatched:");
                mismatches.AppendLine(dispatchedDigest);
            }
            checkedCount++;
        }

        mismatches.ToString().Should().BeEmpty(
            "fileless dispatch must be byte-identical to building the JSON directly");
        checkedCount.Should().BeGreaterThan(0, "the generator must emit fileless JSON arms");
    }

    /// <summary>
    /// The generator's fileless count must equal the number of names it
    /// actually emitted into <see cref="NamedCardFactory.GeneratedJsonCardNames"/>.
    /// </summary>
    [Fact]
    public void GeneratedJsonCardNames_AreNonEmpty_AndCountMatches()
    {
        NamedCardFactory.GeneratedJsonCardNames.Should().NotBeEmpty();
        NamedCardFactory.GeneratedJsonCardNames.Length
            .Should().Be(NamedCardFactory.GeneratedJsonCardCount);

        // No fileless name may collide with a [CardName] factory: if it did,
        // the generator would have skipped it (factory arm wins), so the
        // generated set and the reflected attribute set must be disjoint.
        var attributeNames = ReflectedCardNameAttributes();
        NamedCardFactory.GeneratedJsonCardNames
            .Should().OnlyContain(n => !attributeNames.Contains(n),
                "fileless arms are only emitted for names with no [CardName] wrapper");
    }

    private static string? SlugForName(string name)
    {
        foreach (var slug in Slugs())
        {
            var def = CardDefinitionLoader.FromEmbeddedResource(slug);
            if (string.Equals(def.Name, name, StringComparison.Ordinal))
            {
                return slug;
            }
        }
        return null;
    }

    private static HashSet<string> ReflectedCardNameAttributes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(CardNameAttribute).Assembly.GetTypes())
        {
            foreach (var attr in type.GetCustomAttributes<CardNameAttribute>(inherit: false))
            {
                if (!string.IsNullOrWhiteSpace(attr.Name)) set.Add(attr.Name);
            }
        }
        return set;
    }

    private const string ResourcePrefix = "Majik.Core.CardData.Cards.";
    private const string ResourceSuffix = ".json";

    private static IReadOnlyList<string> Slugs()
    {
        var asm = typeof(CardDefinitionLoader).Assembly;
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(n => n.Substring(
                ResourcePrefix.Length,
                n.Length - ResourcePrefix.Length - ResourceSuffix.Length))
            .ToArray();
    }
}
