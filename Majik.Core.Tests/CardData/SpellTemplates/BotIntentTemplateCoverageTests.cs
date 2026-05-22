using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Every registered <see cref="ISpellTemplate"/> must declare a non-None
/// <see cref="ISpellTemplate.Intent"/>. Allow-list is for composers /
/// stubs with no single intrinsic intent.
/// </summary>
public class BotIntentTemplateCoverageTests
{
    private static readonly HashSet<string> AllowedNone = new(StringComparer.Ordinal)
    {
        // Composers synthesize intent from sub-templates at bind time.
        "ModalChooseOne",
        "ClauseComposition",
        "Strive",

        // Bot's own life loss — bot won't cast this. Intentionally None.
        "YouLoseLife",

        // Self-mill — no graveyard archetype yet. Revisit when one ships.
        "MillSelf",

        // Misc utility templates with no clean intent classification.
        // Bot strategy falls back to legacy label-sniffing for these.
        "Fog",
        "TakeExtraTurn",
        "ShuffleGraveyardIntoLibrary",
    };

    [Fact]
    public void EveryTemplate_DeclaresIntent()
    {
        var missing = new List<string>();
        foreach (var t in OracleSpellBinder.Registry.OrderedTemplates)
        {
            if (AllowedNone.Contains(t.Name)) continue;
            if (t.Intent == BotIntent.None)
            {
                missing.Add(t.Name);
            }
        }

        missing.Should().BeEmpty(
            "every spell template must declare a BotIntent (or be allow-listed); missing: {0}",
            string.Join(", ", missing));
    }
}
