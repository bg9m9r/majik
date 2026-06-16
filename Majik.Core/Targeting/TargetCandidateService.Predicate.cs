using System;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Targeting;

public static partial class TargetCandidateService
{
    /// <summary>
    /// CR 608.2b — a coarse, category-derived legality predicate used as the
    /// resolution-recheck FALLBACK for spells that ship no per-card predicate.
    /// Returns null for <see cref="TargetCategory.None"/> (the caller keeps its
    /// own predicate / runs no recheck). Tests only the object's TYPE against
    /// the category (a creature stayed a creature, a player stayed a player);
    /// finer per-card constraints ("power 1 or less") are the card's own
    /// predicate's job. Honest enough to counter a spell whose only target
    /// changed type/zone (creature destroyed, became a non-creature), never
    /// over-strict.
    /// </summary>
    public static Func<object, bool>? BuildLegalityPredicate(string? description)
    {
        var cat = Classify(description);
        return cat switch
        {
            TargetCategory.None => null,
            TargetCategory.AnyTarget => o => o is Creature || o is Planeswalker || o is Player,
            TargetCategory.Creature => o => o is Creature,
            TargetCategory.Planeswalker => o => o is Planeswalker,
            TargetCategory.Player => o => o is Player,
            TargetCategory.Opponent => o => o is Player,
            TargetCategory.CreatureOrPlayer => o => o is Creature || o is Player,
            TargetCategory.CreatureOrPlaneswalker => o => o is Creature || o is Planeswalker,
            TargetCategory.PlayerOrPlaneswalker => o => o is Player || o is Planeswalker,
            TargetCategory.Permanent => o => o is Permanent,
            TargetCategory.NonlandPermanent => o => o is Permanent p && !p.HasType(CardType.Land),
            TargetCategory.Artifact => o => o is Permanent p && p.HasType(CardType.Artifact),
            TargetCategory.Enchantment => o => o is Permanent p && p.HasType(CardType.Enchantment),
            TargetCategory.Land => o => o is Permanent p && p.HasType(CardType.Land),
            TargetCategory.Spell => o => o is Majik.Core.Spells.ISpell,
            TargetCategory.NoncreatureSpell => o => o is Majik.Core.Spells.ISpell s && !SpellIsCreature(s),
            TargetCategory.CreatureSpell => o => o is Majik.Core.Spells.ISpell s && SpellIsCreature(s),
            TargetCategory.GraveyardCard => o => o is ICard,
            _ => null,
        };
    }
}
