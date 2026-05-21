using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

/// <summary>
/// "Destroy all <kind>" sweep template — Armageddon (lands), Back to
/// Nature (enchantments), Creeping Corrosion (artifacts), Purify
/// (artifacts and enchantments), Planar Cleansing (nonland
/// permanents), Jokulhaups (artifacts, creatures, and lands), etc.
///
/// Distinct from <see cref="DestroyAllCreaturesTemplate"/> which has
/// higher priority and handles the bare "creatures" sweep first. This
/// template only fires when the noun isn't a plain "creatures".
///
/// The kind chain is collapsed to a CardType-set predicate:
/// - "artifacts"           → Artifact
/// - "enchantments"        → Enchantment
/// - "lands"               → Land
/// - "nonland permanents"  → !Land
/// - "permanents"          → any
/// - multi-noun unions     → union of the above (Jokulhaups, Purify)
///
/// Modifier clauses (color, control, etc) and "can't be regenerated"
/// riders are lossy at v1.
/// </summary>
public sealed class DestroyAllPermanentsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+all\s+(?<kind>(?:artifacts|enchantments|lands|permanents|nonland\s+permanents)(?:(?:\s*,\s*|\s+and\s+|\s+or\s+)(?:artifacts|enchantments|lands|creatures|nonland\s+permanents))*)\b",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "DestroyAllPermanents";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value.ToLowerInvariant() }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var kind = @params["kind"];
        return DestroySpellFactory.DestroyAllPermanentsSpell(
            ctx.Caster, BuildPredicate(kind), kind);
    }

    private static Func<ICard, bool> BuildPredicate(string kind)
    {
        // "nonland permanents" is the inverse of Land — pick that off first.
        if (kind.Contains("nonland permanent"))
            return card => !card.HasType(CardType.Land);
        if (kind == "permanents")
            return _ => true;

        var wantArtifact = kind.Contains("artifact");
        var wantEnchantment = kind.Contains("enchantment");
        var wantLand = kind.Contains("land");
        var wantCreature = kind.Contains("creature");
        return card =>
            (wantArtifact && card.HasType(CardType.Artifact)) ||
            (wantEnchantment && card.HasType(CardType.Enchantment)) ||
            (wantLand && card.HasType(CardType.Land)) ||
            (wantCreature && card.HasType(CardType.Creature));
    }
}
