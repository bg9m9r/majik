using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "[Source] deals X damage to each creature" — variable-X creature
/// sweep (Earthquake, Chain Reaction, Savage Twister, Magmaquake,
/// Windstorm/Hurricane/Corrosive Gale's flying-only variant, Gates
/// Ablaze, Cataclysmic Prospecting, etc).
///
/// Mirrors <see cref="DealsDamageEachCreatureTemplate"/> for the
/// variable-X case. Priority 100 so the fixed-numeric template
/// (priority 50) doesn't shadow it — neither could anyway because X
/// isn't in that template's alternation, but the explicit priority
/// makes the intent declarative.
///
/// Flying / non-flying / opponent-only restrictions are lossy at v1
/// (the stub damages every creature on the caster-side battlefield);
/// the spell still binds and resolves with a meaningful effect.
/// </summary>
public sealed class DealsXDamageEachCreatureTemplate : ISpellTemplate
{
    // Same modifier-chain broadening as the fixed-numeric template.
    private static readonly Regex Pattern = new(
        @"deals?\s+x\s+damage\s+to\s+each\s+(?:(?:[\w-]+|or|and|and/or)\s*,?\s*){0,4}creature(?:\s+(?:you\s+control|your\s+opponents\s+control|an\s+opponent\s+controls))?",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DealsXDamageEachCreature";
    public BotIntent Intent => BotIntent.Wrath;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DamageSpellFactory.DealsXDamageEachCreatureSpell(ctx.Caster);
}
