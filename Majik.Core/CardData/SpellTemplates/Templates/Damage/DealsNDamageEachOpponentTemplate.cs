using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "~ deals N damage to each opponent." — e.g. Boltwave, Sulfuric Vortex
/// (the rider clause), Earthquake-style sweepers in the player-only case.
/// Maps onto <see cref="DamageSpellFactory.EachOpponentLosesLifeSpell"/>,
/// which loops <c>ChosenSpellParams.AllPlayers</c> minus the caster and
/// applies the loss. Loss-of-life and damage are conflated here at v1 —
/// the only observable difference is lifelink/damage-prevention which the
/// engine doesn't enforce on these effects yet.
/// </summary>
public sealed class DealsNDamageEachOpponentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+each\s+opponent",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DealsNDamageEachOpponent";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DamageSpellFactory.EachOpponentLosesLifeSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Caster);
}
