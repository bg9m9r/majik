using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// "Prevent all damage that would be dealt to you and {permanents|creatures}
/// you control this turn." family — Endure ("permanents you control"),
/// Safe Passage ("creatures you control").
///
/// Registers <see cref="PreventAllDamageToYouAndYourPermanentsShield"/>
/// with the caster as beneficiary. v1 collapses the two variants — the
/// shield's filter covers any controlled permanent (creature, planeswalker,
/// or otherwise). Today's engine only ever pushes player- /
/// creature- / planeswalker-bound damage intents through the bus, so
/// the broader filter degrades to the printed scope.
///
/// Requires <see cref="SpellBindContext.Replacements"/>. CR 615.
/// </summary>
public sealed class PreventAllDamageToYouAndPermanentsTemplate : ISpellTemplate
{
    // Accepts both "permanents you control" and "creatures you control" —
    // see class remarks for the v1 collapse.
    private static readonly Regex Pattern = new(
        @"^\s*prevent\s+all\s+damage\s+that\s+would\s+be\s+dealt\s+to\s+you\s+and\s+(?:permanents|creatures)\s+you\s+control\s+this\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "PreventAllDamageToYouAndPermanents";
    public BotIntent Intent => BotIntent.Protection;

    public bool CanBind(SpellBindContext ctx) => ctx.Replacements is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var bus = ctx.Replacements!;
        var caster = ctx.Caster;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("prevent-all-damage-to-you-and-permanents", () =>
                {
                    bus.Register(new PreventAllDamageToYouAndYourPermanentsShield(caster));
                }),
            });
    }
}
