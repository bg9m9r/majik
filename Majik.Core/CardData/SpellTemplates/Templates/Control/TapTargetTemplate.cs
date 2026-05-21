using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class TapTargetTemplate : ISpellTemplate
{
    // Accepts:
    //   "tap target <noun>" (single)
    //   "tap up to N target <noun>s" (Frost Breath, Send to Sleep)
    //   "tap N target <noun>s" (Blinding Beam's "two creatures")
    //   "tap X target <noun>s" (Icy Blast, Winter Blast)
    //   "tap one or two target <noun>s" (Broken Dam, Succumb to the Cold)
    // Optional modifier word before the noun (untapped/attacking/etc).
    //
    // v1 stub taps ONE chosen target regardless of "up to N" wording —
    // multi-target lossy. The bound spell still resolves with the
    // load-bearing tap effect.
    private static readonly Regex Pattern = new(
        @"\btap\s+(?:(?:up\s+to\s+)?(?:one|two|three|four|five|six|seven|eight|nine|ten|x)\s+|one\s+or\s+two\s+)?target\s+(?:[\w-]+\s+)?(?<kind>permanent|creature|artifact|land|enchantment|planeswalker)s?\b",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "TapTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ControlSpellFactory.TapTargetSpell(ctx.Resolver, $"target {@params["kind"]}");
}
