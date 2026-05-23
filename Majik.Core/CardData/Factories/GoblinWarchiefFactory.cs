using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Warchief (Scourge / many reprints,
/// Creature — Goblin Warrior {1}{R}{R}).
///
/// Oracle text:
///   "Goblin spells you cast cost {1} less to cast.
///    Goblins you control have haste."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Warrior, mana cost {1}{R}{R}, owner/controller wired.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> wired via
///   <see cref="SpellCostReductionAbility"/>. Predicate matches spells
///   carrying the <see cref="CardSubtype.Goblin"/> subtype (any card type —
///   "Goblin spells" includes Goblin creature spells AND any non-creature
///   spells that print Goblin in their subtype line; v1 only has Goblin
///   creatures, so this collapses to creature spells in practice but the
///   predicate matches the printed oracle text faithfully). Reduction is a
///   flat 1 generic per cast. Scoped to the caster's battlefield by
///   <see cref="CostReduction.GetEffectiveCost"/> — only the controller of
///   this Warchief benefits ("spells you cast"). Coloured pips untouched
///   (CR 117.7c); floor-at-zero is enforced inside the cost-calc helper.
/// - <b>Static "Goblins you control have haste"</b> wired via
///   <see cref="LordStaticEffect"/>: <c>matchingSubtype: Goblin</c>,
///   <c>power: 0, toughness: 0</c>, <c>grantedKeywords: ["Haste"]</c>,
///   <c>includeSelf: true</c>. Layer 6 keyword grant scoped to every
///   Goblin the controller controls — including Warchief itself (the
///   oracle text says "Goblins you control" with no "other" rider, so
///   Warchief grants Haste to itself too). Controller-scoped (default
///   filter — not <c>opponentsOnly</c>).
///
/// Multiple copies stack on the cost-reduction side: two Warchiefs reduce
/// the cost of each Goblin spell by {2}. The Haste grant is idempotent
/// (HashSet semantics in <see cref="CreatureCharacteristics.Keywords"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Warchief isn't on the battlefield so the haste grant lifts correctly,
///   but a future Prune pass could drop the entry. Same shape as Plague
///   Engineer / Goblin Chieftain.
/// </summary>
public static class GoblinWarchiefFactory
{
    public const string CardName = "Goblin Warchief";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Warchief with the cost-reduction rider wired but
    /// no live continuous-effects service. Suitable for shape / dispatcher
    /// tests — the Haste-grant static effect is not registered. The
    /// cost reduction works without a layers service (it's static
    /// metadata consulted by
    /// <see cref="CostReduction.GetEffectiveCost"/>).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Goblin Warchief. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting Haste (no P/T bonus) to
    /// every Goblin the controller controls — including Warchief itself —
    /// is registered against the layers service. The cost-reduction rider
    /// is always wired (it's pure static metadata).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// Haste-grant static effect against. May be null — no live haste
    /// grant.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Goblin spells you cast cost {1} less to cast."
        // Predicate gates on the spell carrying the Goblin subtype.
        // CostReduction.GetEffectiveCost scans only the caster's
        // battlefield for this ability shape, so the "you cast" scope is
        // enforced by the cost-calc helper.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasSubtype(CardSubtype.Goblin),
            reduction: (_, _) => 1,
            description: "Goblin spells you cast cost {1} less to cast."));

        if (continuousEffects != null)
        {
            // CR 613.1f — granted keyword (Haste). Layer 6 in MTG layering;
            // LordStaticEffect MVP places it at Layer.PT_Modify alongside
            // P/T. The oracle text says "Goblins you control" with no
            // "other" — so includeSelf is true and Warchief gives itself
            // Haste too. Controller capture is at register time —
            // control-change re-eval is a follow-up (same caveat as
            // Plague Engineer / Goblin Chieftain).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "Haste" },
                includeSelf: true,
                opponentsOnly: false));
        }

        return card;
    }
}
