using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class GainControlTemplate : ISpellTemplate
{
    // Accepts an optional modifier chain (color / state / type-prefix) and
    // a broader noun set: creature, artifact, enchantment, permanent, land,
    // planeswalker, spell, Aura. Multi-noun unions also caught — "artifact
    // or creature", "artifact, creature, or enchantment" (Threads of
    // Disloyalty, Hijack, Kefnet's Last Word).
    private static readonly Regex Pattern = new(
        @"gain\s+control\s+of\s+target\s+(?:(?:[\w-]+|or|and)\s*,?\s*){0,4}(?:creature|artifact|enchantment|permanent|land|planeswalker|spell|aura)\b",
        RegexOptions.IgnoreCase);

    // CR 514.2 — "until end of turn" makes the control change TEMPORARY: the
    // Threaten / Act of Treason / Claim the Firstborn family. The bound spell
    // then installs a TemporaryControlChangeEffect (control reverts at cleanup)
    // plus the standard untap + haste-until-EOT rider, instead of a permanent
    // Mind-Control-style ControlChangeEffect.
    private static readonly Regex UntilEndOfTurnPattern = new(
        @"gain\s+control\s+of\s+target\s+(?:(?:[\w-]+|or|and)\s*,?\s*){0,4}(?:creature|artifact|enchantment|permanent|land|planeswalker)\b[^.]*?until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    // Whether the printed text grants haste alongside the temporary steal
    // (the Threaten template — Act of Treason / Threaten; Claim the Firstborn
    // omits it). Used only on the until-end-of-turn path.
    private static readonly Regex GainsHastePattern = new(
        @"gains?\s+haste", RegexOptions.IgnoreCase);

    private const string DurationKey = "duration";
    private const string HasteKey = "haste";
    private const string EndOfTurn = "end_of_turn";

    public int Priority => 50;
    public string Name => "GainControl";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    // GainControl needs a live ContinuousEffectsService to register the
    // continuous control-change effect — skip when none is available so
    // the registry moves on to whatever else might match (typically nothing,
    // leaving the card to fall back to a vanilla shell).
    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        if (oracleText is null || !Pattern.IsMatch(oracleText)) return null;

        // Permanent steal (Mind Control) → no params.
        if (!UntilEndOfTurnPattern.IsMatch(oracleText)) return EmptyParams.Instance;

        // Temporary steal (Threaten family) → record the duration + whether the
        // haste rider is printed so Rehydrate composes the right spell.
        return new Dictionary<string, string>
        {
            [DurationKey] = EndOfTurn,
            [HasteKey] = GainsHastePattern.IsMatch(oracleText) ? "true" : "false",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        // CR 514.2 — temporary "until end of turn" steal (Threaten / Act of
        // Treason): route through the declarative gain_control verb so the
        // TemporaryControlChangeEffect + untap + haste rider resolve against
        // the spell's chosen target (CR 608.2b fizzle included).
        if (@params.TryGetValue(DurationKey, out var duration)
            && string.Equals(duration, EndOfTurn, StringComparison.Ordinal))
        {
            var gainsHaste = !@params.TryGetValue(HasteKey, out var haste)
                || !string.Equals(haste, "false", StringComparison.Ordinal);
            return Definitions.CardDefRuntime.BuildSpellDefinitionFromEffects(
                ctx.Entity.Name,
                new Definitions.EffectDefinition[]
                {
                    new Definitions.GainControlEffectDef
                    {
                        TargetFilter = "creature",
                        Duration = EndOfTurn,
                        Untap = true,
                        GainsHaste = gainsHaste,
                    },
                },
                replacements: null,
                continuous: ctx.Effects);
        }

        // Permanent steal (Mind Control) — unchanged.
        return ControlSpellFactory.GainControlSpell(ctx.Resolver, ctx.Caster, ctx.Effects!);
    }
}
