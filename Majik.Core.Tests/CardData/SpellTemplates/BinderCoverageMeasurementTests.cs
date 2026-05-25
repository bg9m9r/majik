using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Measurement harness: walks every unimplemented instant/sorcery in the
/// embedded seed and counts how many <see cref="OracleSpellBinder"/> binds
/// successfully. Used to gauge the lift from normalizer / template changes
/// (PR-A baseline = 3658, PR-B target = significantly higher post-token-fold).
///
/// Not a regression assertion — just emits the numbers via
/// <see cref="ITestOutputHelper"/> for the PR body. The test is always
/// green so it doesn't gate CI; CI just doesn't care about the number.
/// </summary>
public class BinderCoverageMeasurementTests
{
    private readonly ITestOutputHelper _out;
    public BinderCoverageMeasurementTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void Measure_BinderMatchCount_OnUnimplementedInstantsAndSorceries()
    {
        var repo = new EmbeddedCardRepository();
        var all = repo.Search(q: null, implementedOnly: false, limit: int.MaxValue);
        var caster = new Player("Measure", 20);

        int total = 0, matched = 0;
        var matchedNames = new List<string>();
        foreach (var e in all)
        {
            if (!IsInstantOrSorcery(e)) continue;
            if (e.IsImplemented) continue;
            total++;
            var def = OracleSpellBinder.Bind(e, caster, x => x, null, null);
            if (def is not null)
            {
                matched++;
                matchedNames.Add(e.Name);
            }
        }

        _out.WriteLine($"Unimplemented instants/sorceries: {total}");
        _out.WriteLine($"Bound by OracleSpellBinder:        {matched}");

        // Emit a small sanity-check sample of newly-matched cards so a human
        // can eyeball a handful of representative matches.
        var sample = matchedNames.OrderBy(n => n, StringComparer.Ordinal).Take(20).ToList();
        _out.WriteLine("Sample (first 20 by name):");
        foreach (var n in sample) _out.WriteLine($"  - {n}");

        total.Should().BeGreaterThan(0);
    }

    private static bool IsInstantOrSorcery(CardEntity e) =>
        (e.TypeLine?.Contains("Instant", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (e.TypeLine?.Contains("Sorcery", StringComparison.OrdinalIgnoreCase) ?? false);
}
