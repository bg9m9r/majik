using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class ExileFromGraveyardTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"exile\s+target\s+(?<kind>creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+(?:a|your)\s+graveyard",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ExileFromGraveyard";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.ExileFromGraveyardSpell(ctx.Resolver, m.Groups["kind"].Value.Trim())
            : null;
    }
}
