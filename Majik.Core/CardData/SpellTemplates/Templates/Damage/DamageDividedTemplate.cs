using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "~ deals N damage divided as you choose among one, two, or three
/// targets." family — Arc Lightning, Chandra's Pyrohelix, Boulderfall,
/// Aerial Volley, Deft Dismissal, Furious Reprisal, etc.
///
/// <para>
/// PR-B token-fold consumer: the template anchors on the folded view
/// ("~ deals n damage divided as you choose among …") so a single
/// regex catches every value of N. The unfolded
/// <see cref="SpellBindContext.Text"/> is then scanned to recover the
/// actual N and the maximum-target count (one / two / three / any
/// number). Two-pass detection so the family fold lifts an entire
/// cluster of cards through a single template.
/// </para>
///
/// <para>
/// Lossy v1: distribution is even-split (remainder front-loaded) —
/// see <see cref="DamageSpellFactory.DamageDividedAmongAnyTargetsSpell"/>.
/// Restricting clauses ("…among one, two, or three target attacking or
/// blocking creatures" — Deft Dismissal) are accepted by the family
/// fold; the v1 target slot uses "any target" rather than the narrowed
/// predicate. Targeting fidelity can be tightened later.
/// </para>
/// </summary>
public sealed class DamageDividedTemplate : ISpellTemplate
{
    // Folded-view family pattern. The "~ deals n damage divided as you
    // choose among" stem is identical across every member of the family.
    // The cap word is captured loosely so the various Scryfall printings
    // (Arc Lightning's "one, two, or three", Chandra's Pyrohelix's "one or
    // two", Boulderfall's "any number of") all match a single regex.
    private static readonly Regex FoldedPattern = new(
        @"~\s+deals\s+n\s+damage\s+divided\s+as\s+you\s+choose\s+among\s+(?<cap>(?:one|two|three|any\s+number)(?:[ ,]+(?:or\s+)?(?:one|two|three))*)\s+(target|targets)\b",
        RegexOptions.IgnoreCase);

    // Unfolded N-extraction. Re-anchors on the same stem and pulls the
    // integer that the fold replaced with "n".
    private static readonly Regex NExtract = new(
        @"~\s+deals\s+(?<n>\d+)\s+damage\s+divided",
        RegexOptions.IgnoreCase);

    public int Priority => 55;
    public string Name => "DamageDivided";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var folded = ctx.TextFolded;
        var fm = FoldedPattern.Match(folded);
        if (!fm.Success) return null;

        var nm = NExtract.Match(ctx.Text);
        if (!nm.Success || !int.TryParse(nm.Groups["n"].Value, out var n) || n <= 0) return null;

        var max = CapToMax(fm.Groups["cap"].Value, n);
        return DamageSpellFactory.DamageDividedAmongAnyTargetsSpell(
            n, max, ctx.Resolver, ctx.Replacements, ctx.Caster, ctx.EventBus);
    }

    private static int CapToMax(string capWord, int n)
    {
        // "any number of" caps at N (you can't divide N damage among more
        // than N targets and still deal 1 to each). Word caps are literal.
        var w = capWord.ToLowerInvariant();
        if (w.StartsWith("any number")) return Math.Max(1, n);
        if (w.Contains("three")) return 3;
        if (w.Contains("two")) return 2;
        return 1;
    }
}
