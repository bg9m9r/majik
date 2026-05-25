using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "~ deals N damage to each player." — Flame Rift, the player-only half
/// of Mana Clash, etc. Sister to
/// <see cref="DealsNDamageEachOpponentTemplate"/>; the difference is
/// inclusiveness of the caster (CR 109.5: "each player" includes the
/// controller).
///
/// PR-B: consumes the folded view so a single regex catches every N.
/// Value is recovered from the unfolded
/// <see cref="SpellBindContext.Text"/>.
/// </summary>
public sealed class DamageEachPlayerTemplate : ISpellTemplate
{
    private static readonly Regex FoldedPattern = new(
        @"~\s+deals\s+n\s+damage\s+to\s+each\s+player\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex NExtract = new(
        @"~\s+deals\s+(?<n>\d+)\s+damage\s+to\s+each\s+player",
        RegexOptions.IgnoreCase);

    public int Priority => 60; // Beat the each-opponent template if both ever match the same text.
    public string Name => "DamageEachPlayer";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Reach;

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!FoldedPattern.IsMatch(ctx.TextFolded)) return null;

        var m = NExtract.Match(ctx.Text);
        if (!m.Success || !int.TryParse(m.Groups["n"].Value, out var n) || n <= 0) return null;

        return DamageSpellFactory.DamageEachPlayerSpell(n, ctx.Caster);
    }
}
