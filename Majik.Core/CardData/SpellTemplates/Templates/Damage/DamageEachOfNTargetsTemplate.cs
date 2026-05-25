using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "~ deals N damage to each of <i>two/three</i> targets" — Furious
/// Reprisal, Spitebellows-trigger, Forked Lightning, etc.
///
/// Distinct from
/// <see cref="DamageDividedTemplate"/> in that the per-target amount is
/// fixed (every target takes the full N), not divided. The fold view
/// captures every value of N and the named count of targets.
/// </summary>
public sealed class DamageEachOfNTargetsTemplate : ISpellTemplate
{
    private static readonly Regex FoldedPattern = new(
        @"~\s+deals\s+n\s+damage\s+to\s+each\s+of\s+(?<k>two|three)\s+targets?\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex NExtract = new(
        @"~\s+deals\s+(?<n>\d+)\s+damage\s+to\s+each\s+of\s+(?:two|three)\s+targets?",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "DamageEachOfNTargets";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var fm = FoldedPattern.Match(ctx.TextFolded);
        if (!fm.Success) return null;

        var m = NExtract.Match(ctx.Text);
        if (!m.Success || !int.TryParse(m.Groups["n"].Value, out var n) || n <= 0) return null;

        var k = fm.Groups["k"].Value.Equals("three", StringComparison.OrdinalIgnoreCase) ? 3 : 2;

        // Reuse the divided factory with one tweak: each target takes N (not
        // N/k). We model this by passing n*k as the total and clamping the
        // even-split to exactly N per target (k targets * N = n*k). The
        // helper splits remainder front-loaded, so for k|n*k this is exact.
        return DamageSpellFactory.DamageDividedAmongAnyTargetsSpell(
            n * k, k, ctx.Resolver, ctx.Replacements, ctx.Caster, ctx.EventBus);
    }
}
