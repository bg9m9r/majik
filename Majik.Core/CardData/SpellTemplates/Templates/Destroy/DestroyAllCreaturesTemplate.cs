using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

/// <summary>
/// "Destroy all creatures" — wrath template (Wrath of God, Day of
/// Judgment, Damnation, Damning Verdict, Citywide Bust, Fell the
/// Mighty, Plague Wind, In Garruk's Wake, Retribution of the Meek,
/// etc).
///
/// Accepts a trailing qualifier clause ("with power 4 or greater",
/// "you don't control", "that aren't enchanted", "with no counters on
/// them", "and planeswalkers except for commanders") which is purely
/// informational at v1 — the stub destroys every creature in sight.
///
/// "They can't be regenerated" and indestructible bypass are also v1
/// lossy. Destruction goes through MoveToGraveyard which doesn't fire
/// destroy-replacement effects yet.
///
/// Priority 100 so it wins against the targeted DestroyCreature
/// template — both can match "destroy" but only this template handles
/// the all-creatures clause.
/// </summary>
public sealed class DestroyAllCreaturesTemplate : ISpellTemplate
{
    // Modifier chain between "all" and "creatures": color ("green
    // creatures" — Perish, "nonblack creatures" — Hellfire), tribe
    // ("non-Vehicle creatures" — Turbocharged Escape, "nonenchantment
    // creatures" — Extinguish All Hope), combat-state ("tapped
    // creatures" — Guan Yu / Don't Move, "blocking creatures and
    // blocked creatures" — Fight to the Death). v1 stub destroys
    // every creature on caster's view of battlefield.
    private static readonly Regex Pattern = new(
        @"destroy\s+all\s+(?:(?:[\w-]+|or|and)\s*,?\s*){0,3}creatures\b",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DestroyAllCreatures";
    public BotIntent Intent => BotIntent.Wrath;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DestroySpellFactory.DestroyAllCreaturesSpell(ctx.Caster);
}
