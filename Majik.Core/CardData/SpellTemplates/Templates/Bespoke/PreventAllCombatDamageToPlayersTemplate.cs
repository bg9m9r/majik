using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// "Prevent all combat damage that would be dealt to players this turn."
/// — narrower than Fog. Backs Commencement of Festivities / Defend the
/// Hearth.
///
/// Registers <see cref="PreventAllCombatDamageToPlayersShield"/>; only
/// player-bound combat damage is cancelled, creature- / planeswalker-bound
/// combat damage still resolves.
///
/// Requires <see cref="SpellBindContext.Replacements"/>. CR 615.
/// </summary>
public sealed class PreventAllCombatDamageToPlayersTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*prevent\s+all\s+combat\s+damage\s+that\s+would\s+be\s+dealt\s+to\s+players\s+this\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "PreventAllCombatDamageToPlayers";
    public BotIntent Intent => BotIntent.Protection;

    public bool CanBind(SpellBindContext ctx) => ctx.Replacements is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var bus = ctx.Replacements!;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("prevent-all-combat-damage-to-players", () =>
                {
                    bus.Register(new PreventAllCombatDamageToPlayersShield());
                }),
            });
    }
}
