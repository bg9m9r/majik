using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class GainControlTemplate : ISpellTemplate
{
    // Accepts an optional modifier chain (color / state / type-prefix) and
    // a broader noun set: creature, artifact, enchantment, permanent, land,
    // planeswalker, spell, Aura. Multi-noun unions also caught — "artifact
    // or creature", "artifact, creature, or enchantment" (Threads of
    // Disloyalty, Hijack, Kefnet's Last Word).
    //
    // Runtime stub installs a continuous control-change on the chosen
    // target — extra wording like "until end of turn" or "untap it. It
    // gains haste until end of turn" is lossy at v1, but the bound spell
    // resolves with the load-bearing effect.
    private static readonly Regex Pattern = new(
        @"gain\s+control\s+of\s+target\s+(?:(?:[\w-]+|or|and)\s*,?\s*){0,4}(?:creature|artifact|enchantment|permanent|land|planeswalker|spell|aura)\b",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "GainControl";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    // GainControl needs a live ContinuousEffectsService to register the
    // continuous control-change effect — skip when none is available so
    // the registry moves on to whatever else might match (typically nothing,
    // leaving the card to fall back to a vanilla shell).
    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ControlSpellFactory.GainControlSpell(ctx.Resolver, ctx.Caster, ctx.Effects!);
}
