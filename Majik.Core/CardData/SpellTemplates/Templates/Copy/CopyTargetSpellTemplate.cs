using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Copy;

/// <summary>
/// "Copy target instant or sorcery spell. You may choose new targets for the
/// copy." — Twincast ({U}{U}) / Reverberate ({R}{R}) and the wider spell-copy
/// family (CR 707.10).
///
/// Matches the leading "copy target instant or sorcery spell" clause and binds
/// the copy effect via <see cref="CopySpellFactory.CopyTargetInstantOrSorcery"/>
/// (a distinct copy stack object, CR 706.10a). The "you may choose new targets
/// for the copy" rider (CR 707.10a) is honoured at resolution — the copy effect
/// re-prompts the copier for new targets via
/// <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpellAsync"/> using
/// the copied spell's retained per-slot requests; declining keeps the original
/// target.
/// </summary>
public sealed class CopyTargetSpellTemplate : ISpellTemplate
{
    // Anchored on the copy clause. Tolerates the optional "you may choose new
    // targets for the copy" trailing rider (consumed-but-ignored in v1).
    private static readonly Regex Pattern = new(
        @"copy\s+target\s+instant\s+or\s+sorcery\s+spell\b",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "CopyTargetSpell";

    // The copier holds mana to chain into a real spell; Buff is the closest
    // BotIntent (the bot treats it as a value/hold-mana signal).
    public BotIntent Intent => BotIntent.Buff;

    /// <summary>
    /// Requires <see cref="SpellBindContext.Stack"/> so the bound effect can
    /// push the copy onto the live stack. Shape-only contexts (no stack)
    /// decline so a stack-less probe doesn't bind a no-op.
    /// </summary>
    public bool CanBind(SpellBindContext ctx) => ctx.Stack is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CopySpellFactory.CopyTargetInstantOrSorcery(ctx.Resolver, ctx.Stack, ctx.Caster);
}
