using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "Return all &lt;kind&gt; cards from your graveyard to the battlefield."
/// — single-clause mass reanimation (Brilliant Restoration, Redress Fate,
/// Replenish, Splendid Reclamation, Triumphant Reckoning, etc).
///
/// Lossy v1 stub: returns every card in the caster's graveyard to the
/// battlefield under the caster's control. The kind rider is captured
/// for the target-request label but the runtime predicate accepts all
/// cards — type/subtype/supertype filters are ignored. "tapped" is also
/// ignored. Multi-clause variants ("Return all … . Then destroy all …")
/// require composer support and are intentionally skipped here.
/// </summary>
public sealed class ReturnAllFromGraveyardTemplate : ISpellTemplate
{
    // Period anchor at the tail keeps this to single-sentence shapes —
    // multi-clause variants (Wake the Past, Storm of Souls, Zombie Apocalypse)
    // need bespoke handling and shouldn't trigger this stub.
    private static readonly Regex Pattern = new(
        @"return\s+all\s+(?<kind>[^.]+?)\s+cards?\s+from\s+your\s+graveyard\s+to\s+the\s+battlefield(?:\s+tapped)?\s*\.",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ReturnAllFromGraveyard";
    public BotIntent Intent => BotIntent.Reanimate;

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
        LibrarySpellFactory.ReturnAllFromGraveyardSpell(
            ctx.Caster, @params.TryGetValue("kind", out var k) ? k : "");
}
