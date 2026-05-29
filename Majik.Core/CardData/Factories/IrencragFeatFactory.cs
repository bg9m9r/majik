using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Irencrag Feat (Throne of Eldraine, {1}{R}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Add {R}{R}{R}{R}{R}{R}{R}. You can cast only one more spell this turn."
///
/// ## Implementation
///
/// Card shape only here; the resolve-time effect is built on-demand via
/// <see cref="BuildResolveEffect"/> so tests / integrations can plug it
/// into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
/// directly to a <see cref="Majik.Core.Spells.Spell"/>.
///
/// Mana goes into <see cref="Player.ManaPool"/> via
/// <see cref="Player.AddManaToPool(ManaCost)"/>. The pool follows
/// CR 106.4 / CR 500.4 — produced mana lives until the end of the
/// current step/phase.
///
/// The "only one more spell" clause is a turn-scoped casting restriction
/// (CR 601.3). It is registered via
/// <see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/> with
/// a cap of 1. <see cref="Majik.Core.Game.SpellCastFlow"/> decrements the
/// counter on each subsequent successful cast via
/// <see cref="CastingRestrictions.ConsumeAdditionalSpellAllowance"/>, and
/// <see cref="ActionValidator.ValidateCastSpell"/> rejects the next cast
/// attempt once the counter reaches zero (CR 601.3).
///
/// CR 514.2 — the restriction expires at the end of the turn. Callers
/// should invoke <see cref="CastingRestrictions.ClearMaxAdditionalSpellsThisTurn"/>
/// in their end-of-turn cleanup (or <see cref="CastingRestrictions.Clear"/>
/// in tests).
///
/// Net-mana posture: Irencrag Feat costs {1}{R}{R}{R} (MV 4) and produces
/// seven red — a net of +{R}{R}{R} from the mana investment alone,
/// making it the premier ritual for Belcher (Empty the Warrens /
/// Goblin Charbelcher) combo lines.
///
/// Sibling of <see cref="DarkRitualFactory"/> (straight mana production)
/// and <see cref="RiteOfFlameFactory"/> (graveyard-scaling mana).
/// </summary>
[CardName("Irencrag Feat")]
public static class IrencragFeatFactory
{
    public const string CardName = "Irencrag Feat";
    public const string PrintedManaCost = "{1}{R}{R}{R}";

    /// <summary>
    /// Seven red mana produced on resolution.
    /// </summary>
    public const string ManaProduced = "RRRRRRR";

    /// <summary>
    /// The number of additional spells the controller may cast after
    /// Irencrag Feat resolves this turn (oracle: "only one more spell").
    /// </summary>
    public const int MaxAdditionalSpells = 1;

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time seven-red production and the one-more-spell
    /// casting restriction.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Irencrag Feat's resolve effect. On resolution:
    /// 1. Add seven red mana to <paramref name="controller"/>'s mana pool
    ///    (CR 106.4).
    /// 2. Register a turn-scoped "you can cast only one more spell this turn"
    ///    restriction on <paramref name="controller"/> via
    ///    <see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/>
    ///    (CR 601.3).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new IEffect[]
        {
            new Effect("Irencrag Feat: add {R}{R}{R}{R}{R}{R}{R}, then set one-more-spell cap.", () =>
            {
                // CR 106.4 — seven red mana into the pool.
                controller.AddManaToPool(ManaCost.Parse(ManaProduced));

                // CR 601.3 — "You can cast only one more spell this turn."
                // The cap is 1 (one MORE spell after Irencrag Feat itself;
                // Irencrag Feat is already on the stack when this resolves,
                // so this restriction governs future casts only).
                CastingRestrictions.SetMaxAdditionalSpellsThisTurn(controller, MaxAdditionalSpells);
            }),
        };
    }
}
