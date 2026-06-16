using System.Text.RegularExpressions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Welcome to the Fold (Eldritch Moon). Sorcery — {2}{U}{U}, Madness {X}{U}{U}.
///
///   "Gain control of target creature if its toughness is 2 or less. If this
///    spell's madness cost was paid, instead gain control of that creature if
///    its toughness is X or less."
///
/// Bespoke template dispatching to <see cref="WelcomeToTheFoldFactory"/> — the
/// conditional-madness-X toughness gate is not expressible by the declarative
/// <c>gain_control</c> verb (which has no "if its toughness is N or less" gate,
/// let alone a madness-X-widened one), so this matches the exact oracle text
/// fragment and hands resolution to the factory's
/// <see cref="WelcomeToTheFoldFactory.BuildSpellDefinition"/>. The factory's
/// resolve closure reads the madness-paid flag + madness X off
/// <see cref="Majik.Core.Abilities.ResolutionContext.SourceCard"/> (the seam
/// this deferral pays down).
///
/// <para>Needs a live <see cref="Majik.Core.Effects.ContinuousEffectsService"/>
/// to register the permanent control change — <see cref="CanBind"/> skips when
/// none is available, exactly as <c>GainControlTemplate</c> does.</para>
/// </summary>
public sealed class WelcomeToTheFoldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"gain\s+control\s+of\s+target\s+creature\s+if\s+its\s+toughness\s+is\s+2\s+or\s+less\.\s*"
        + @"if\s+this\s+spell'?s\s+madness\s+cost\s+was\s+paid",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 100;
    public string Name => "WelcomeToTheFold";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    // Mirrors GainControlTemplate — the control change needs a live
    // ContinuousEffectsService.
    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        oracleText is not null && Pattern.IsMatch(oracleText)
            ? EmptyParams.Instance
            : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        WelcomeToTheFoldFactory.BuildSpellDefinition(ctx.Caster, ctx.Resolver, ctx.Effects!);
}
