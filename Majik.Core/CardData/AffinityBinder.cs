using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;

namespace Majik.Core.CardData;

/// <summary>
/// CR 702.40 (modern wording) — Affinity. The canonical phrasing is
/// "This spell costs {N} less to cast for each X you control." Attaches
/// a <see cref="CostReductionAbility"/> matching the predicate; the
/// cost-reducer scans the caster's battlefield at cast time.
///
/// Supported X values: artifact, creature, land, enchantment.
/// </summary>
public static class AffinityBinder
{
    private static readonly Regex PerControlled = new(
        @"this spell costs \{?(?<n>\d+)\}?\s+less to cast for each (?<kind>artifact|creature|land|enchantment|swamp|island|mountain|plains|forest)\s+you control",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var text = entity.OracleText ?? string.Empty;
        var m = PerControlled.Match(text);
        if (!m.Success) return false;

        var amount = int.Parse(m.Groups["n"].Value);
        var kind = m.Groups["kind"].Value.ToLowerInvariant();
        Func<ICard, bool> predicate = kind switch
        {
            "artifact" => c => c.HasType(CardType.Artifact),
            "creature" => c => c.HasType(CardType.Creature),
            "land" => c => c.HasType(CardType.Land),
            "enchantment" => c => c.HasType(CardType.Enchantment),
            "swamp" => c => c.HasSubtype(CardSubtype.Swamp),
            "island" => c => c.HasSubtype(CardSubtype.Island),
            "mountain" => c => c.HasSubtype(CardSubtype.Mountain),
            "plains" => c => c.HasSubtype(CardSubtype.Plains),
            "forest" => c => c.HasSubtype(CardSubtype.Forest),
            _ => _ => false,
        };
        card.AddAbility(new CostReductionAbility(amount, predicate, $"costs {amount} less per {kind}"));
        return true;
    }
}
