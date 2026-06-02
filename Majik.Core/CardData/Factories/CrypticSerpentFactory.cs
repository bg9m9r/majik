using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cryptic Serpent (Hour of Devastation, {5}{U}{U}).
///
/// Creature — Serpent 6/5. Oracle text (verified against Scryfall):
///   "This spell costs {1} less to cast for each instant and sorcery card in
///    your graveyard."
///
/// The base shape (name, Creature, Serpent subtype, {5}{U}{U}, 6/5) is
/// materialised from the embedded JSON definition
/// (<c>cryptic-serpent.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The graveyard-count cost
/// reducer is layered on here — the JSON <c>AbilityDefinition</c> schema
/// carries no parameterised spell-cost reducer (same posture as
/// <see cref="DanithaCapashenParagonFactory"/> for the cost reducer).
///
/// ## Implemented (v1)
///
/// - 6/5 Creature — Serpent at printed cost {5}{U}{U}; owner / controller
///   wired. Single Serpent subtype.
/// - <b>Graveyard cost reduction (CR 117.7)</b>: a
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> shape — counts
///   instant / sorcery cards in the caster's graveyard at cost-calc time
///   and reduces generic mana by that count (one-to-one). Coloured pips
///   are untouched (CR 117.7c) — the two {U} pips remain regardless of
///   graveyard size; the printed {5} generic collapses to zero once there
///   are five or more instants / sorceries in the graveyard. The reduction
///   floors at zero inside <see cref="CostReduction.GetEffectiveCost"/>.
///   Exactly the same reducer shape as <see cref="TolarianTerrorFactory"/>
///   — Cryptic Serpent is the Ward-less, {5}{U}{U} 6/5 sibling of the same
///   "costs {1} less per instant/sorcery in your graveyard" Serpent line.
///     - 0 in graveyard → pays {5}{U}{U}
///     - 4 in graveyard → pays {1}{U}{U}
///     - 5 in graveyard → pays {U}{U}
///     - 8 in graveyard → still pays {U}{U} (floor at the two blue pips)
/// </summary>
[CardName("Cryptic Serpent")]
public static class CrypticSerpentFactory
{
    public const string CardName = "Cryptic Serpent";
    public const string Slug = "cryptic-serpent";

    /// <summary>
    /// Construct Cryptic Serpent owned and controlled by
    /// <paramref name="owner"/>. The graveyard-count cost reducer is
    /// attached — there is no live runtime service needed (the reducer is a
    /// passive rider read by <see cref="Majik.Core.Costs.CostReduction"/>).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Serpent, {5}{U}{U}, 6/5). No abilities in the JSON — the cost
        // reducer is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each instant
        // and sorcery card in your graveyard." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — the function counts
        // instants/sorceries in the caster's graveyard at cost-calc time
        // and reduces generic mana one-to-one.
        // CR 117.7c — coloured pips can't reduce; the floor at zero on
        // generic mana is enforced inside CostReduction.GetEffectiveCost,
        // so the two {U} pips remain regardless of graveyard size.
        // Same reducer shape as Tolarian Terror's.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
            {
                if (caster?.Zones?.Graveyard == null) return 0;
                var n = 0;
                foreach (var g in caster.Zones.Graveyard.GetCards())
                {
                    if (g.HasType(CardType.Instant) || g.HasType(CardType.Sorcery)) n++;
                }
                return n;
            },
            description:
                "This spell costs {1} less to cast for each instant and " +
                "sorcery card in your graveyard."));

        return card;
    }
}
