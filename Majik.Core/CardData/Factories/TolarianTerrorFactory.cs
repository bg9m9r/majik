using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tolarian Terror (Dominaria United, {6}{U}{U}).
///
/// Creature — Serpent 5/5. Oracle text (Scryfall, verified):
///   "This spell costs {1} less to cast for each instant and sorcery card in
///    your graveyard.
///    Ward {3} (Whenever this creature becomes the target of a spell or
///    ability an opponent controls, counter it unless that player pays {3}.)"
///
/// ## Implemented (v1)
///
/// - 5/5 Creature — Serpent at printed cost {6}{U}{U}; owner / controller
///   wired. Single Serpent subtype (no other printed subtypes).
/// - <b>Graveyard cost reduction (CR 117.7)</b>: a
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> shape — counts
///   instant / sorcery cards in the caster's graveyard at cost-calc time
///   and reduces generic mana by that count (one-to-one,
///   <em>not</em> doubled — Demilich's reducer is the same shape but with
///   a different printed multiplier; both wrap into the same
///   <see cref="CostReduction.GetEffectiveCost"/> floor-at-zero pipeline).
///   Coloured pips are untouched (CR 117.7c) — the two {U} pips remain
///   regardless of graveyard size; the printed {6} generic collapses to
///   zero once there are six or more instants / sorceries in the
///   graveyard, giving the canonical "turn-3 / turn-4 Terror" tempo line
///   that defines the card across Modern + Pioneer.
///     - 0 in graveyard → pays {6}{U}{U}
///     - 4 in graveyard → pays {2}{U}{U}
///     - 6 in graveyard → pays {U}{U}
///     - 10 in graveyard → still pays {U}{U} (floor at the two blue pips)
///   Mirrors <see cref="DemilichFactory"/>'s graveyard-count reducer.
/// - <b>Ward {3} (CR 702.21)</b>: wired as a
///   <see cref="KeywordAbility"/> marker plus a <see cref="WardEffect"/>
///   builder (<see cref="BuildWardEffect"/>) bound to the live card. Same
///   posture as <see cref="KappaCannoneerFactory"/> — the marker keeps the
///   keyword-scan surface uniform (bot probes, dispatcher tests, oracle
///   reflection); the working <see cref="WardEffect"/> primitive is
///   exposed for the spell-resolution path to opt-in via
///   <see cref="WardEffect.ResolvesWard"/>. The
///   battlefield-attached triggered-ability surface is deferred (same
///   ship-gap as Kappa Cannoneer); cost-paying-or-countering on
///   opponent-controlled targeting is the spell-resolve hook.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward {3} trigger wiring</b>: <see cref="WardEffect"/> is a stand-
///   alone check helper, not yet plumbed onto a battlefield-attached
///   triggered ability. v1 ships the marker + <see cref="BuildWardEffect"/>
///   builder; the spell-resolution path gains the Ward consultation in a
///   separate PR (matches the deferred surface on Kappa Cannoneer +
///   Reality Smasher).
/// </summary>
[CardName("Tolarian Terror")]
public static class TolarianTerrorFactory
{
    public const string CardName = "Tolarian Terror";
    public const string PrintedManaCost = "{6}{U}{U}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>CR 702.21 — printed Ward cost: {3}.</summary>
    public const string WardCost = "{3}";

    /// <summary>
    /// CR 702.21 — Tolarian Terror's printed Ward {3} effect, bound to the
    /// supplied <paramref name="card"/>. v1 exposes this as a builder so
    /// the spell-resolution path can opt-in once the Ward trigger
    /// primitive lands (see class xmldoc for the deferred wiring gap).
    /// Mirrors <see cref="KappaCannoneerFactory.BuildWardEffect"/>.
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, ManaCost.Parse(WardCost));

    /// <summary>
    /// Construct Tolarian Terror owned and controlled by
    /// <paramref name="owner"/>. The graveyard-count cost reducer and the
    /// Ward {3} keyword marker are attached.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Serpent });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each instant
        // and sorcery card in your graveyard." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — the function counts
        // instants/sorceries in the caster's graveyard at cost-calc time.
        // CR 117.7c — coloured pips can't reduce; the floor at zero on
        // generic mana is enforced inside CostReduction.GetEffectiveCost,
        // so the two {U} pips remain regardless of graveyard size.
        // Mirrors DemilichFactory's reducer (same shape, same engine
        // wiring; differs only in the printed multiplier — Terror = 1,
        // Demilich = 1 as well at the multiplier level since both read
        // "this spell costs {1} less" / "{U} less" one-to-one).
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

        // ----------------------------------------------------------------
        // Ward {3} (CR 702.21) — marker keyword. WardEffect exists as a
        // standalone helper (BuildWardEffect bounds an instance to the
        // live card) but the battlefield-attached triggered-ability
        // surface is deferred — see class xmldoc. Same posture as Kappa
        // Cannoneer / Reality Smasher.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        return card;
    }
}
