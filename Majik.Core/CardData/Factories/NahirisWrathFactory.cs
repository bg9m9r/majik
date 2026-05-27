using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nahiri's Wrath (Eldritch Moon, {4}{R}{R}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, discard X cards.
///    Nahiri's Wrath deals damage to each of up to X target creatures,
///    planeswalkers, and/or players equal to the total mana value of the
///    discarded cards."
///
/// ## Why it gets its own factory
/// Nahiri's Wrath is a high-variance red finisher that bridges
/// "discard-as-resource" and "X-target burn". The additional cost is
/// caster-chosen X (CR 601.2f) and the resolution shape is "deal N
/// damage to each of up to X targets" where N = total mana value of the
/// discarded cards. Two engine primitives compose: the new
/// <see cref="DiscardXCardsAdditionalCost"/> picks/discards the caster's
/// chosen subset and remembers it; resolution sums
/// <c>ManaCostValue.TotalValue</c> across the discarded list and routes
/// damage through <see cref="Fx.DealDamageAny"/> for each target.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {4}{R}{R}.
/// - <b>Additional cost: discard X cards</b> (CR 601.2f) — wired via
///   <see cref="DiscardXCardsAdditionalCost"/>. The cost remembers the
///   discarded set; the resolve closure reads it.
/// - <b>0..X targets</b> — single <see cref="TargetRequest"/> with
///   <c>MinTargets = 0, MaxTargets = int.MaxValue</c> gathering creatures,
///   planeswalkers, and players across every battlefield + player roster
///   (CR 117.7 / CR 119.3 — "up to X target creatures, planeswalkers,
///   and/or players"). v1 simplification mirrors
///   <see cref="IndomitableCreativityFactory"/>: the
///   <see cref="TargetRequest"/> can't yet bind <c>MaxTargets = X</c>
///   dynamically. Callers pre-supply at most X targets via
///   <see cref="ChosenSpellParams.Targets"/>; the resolve closure trusts
///   the chosen-target cardinality.
/// - <b>Resolve</b>: sums the total mana value of the
///   <see cref="DiscardXCardsAdditionalCost.Discarded"/> list (per
///   <c>ManaCostValue.TotalValue</c>; CR 202.3 — mana value is computed
///   from the printed mana cost). For each chosen target still in a
///   legal zone (CR 608.2b), deals N damage via
///   <see cref="Fx.DealDamageAny"/>, which routes Player →
///   <see cref="Player.LoseLife"/>, Creature →
///   <see cref="Creature.TakeDamage"/>, Planeswalker →
///   <see cref="Planeswalker.RemoveLoyalty"/> (CR 306.7).
///
/// ## v1 gaps
/// - <b>X-keyed target count</b>: the engine's
///   <see cref="TargetRequest"/> can't bind <c>MaxTargets = X</c>
///   dynamically; callers pre-supply ≤ X targets. Same gap as
///   <see cref="IndomitableCreativityFactory"/>.
/// - <b>"As an additional cost, discard X" prompt</b>: the agent doesn't
///   choose <c>X</c> at announcement separately from the discard set.
///   Today the cost's <see cref="DiscardXCardsAdditionalCost.Targets"/>
///   list is the source of truth; default (null) discards the entire
///   hand. Tests and bots that want a specific X pre-set
///   <see cref="DiscardXCardsAdditionalCost.Targets"/> on a constructed
///   cost instance and pass it into the cast flow.
/// - <b>Mana-value of cards with {X}</b>: CR 202.3b — for a card with
///   <c>{X}</c> in its mana cost in any zone other than the stack,
///   <c>X</c> is treated as 0. <see cref="Card.ManaCostValue"/> evaluates
///   <c>{X}</c> at parse time as 0 generic, so a discarded
///   Fireball-shaped card contributes only its non-X pips — correct under
///   CR 202.3b.
/// </summary>
[CardName("Nahiri's Wrath")]
public static class NahirisWrathFactory
{
    public const string CardName = "Nahiri's Wrath";
    public const string PrintedManaCost = "{4}{R}{R}";

    /// <summary>
    /// Construct a Nahiri's Wrath sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// <see cref="SpellDefinition"/> (with the discard-X additional cost +
    /// 0..X target request + damage closure) is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Nahiri's Wrath uses at
    /// cast time. The <see cref="DiscardXCardsAdditionalCost"/> instance
    /// is created here and embedded into
    /// <see cref="SpellDefinition.AdditionalCosts"/> so the cast flow
    /// pays it before resolution; the resolve closure reads its
    /// <see cref="DiscardXCardsAdditionalCost.Discarded"/> field to
    /// compute the per-target damage amount.
    /// </summary>
    /// <param name="resolver">Resolves each raw target token to a live
    /// engine object (Player / Creature / Planeswalker).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var discardCost = new DiscardXCardsAdditionalCost();

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to X target creatures, planeswalkers, and/or players",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    CandidateGatherer: ctx =>
                    {
                        var candidates = new List<object>();
                        foreach (var p in ctx.AllPlayers)
                        {
                            candidates.Add(p);
                            foreach (var c in p.Zones.Battlefield.GetCards())
                            {
                                if (c.HasType(CardType.Creature)
                                    || c.HasType(CardType.Planeswalker))
                                {
                                    candidates.Add(c);
                                }
                            }
                        }
                        return candidates;
                    }),
            },
            EffectFactory: chosen => new IEffect[]
            {
                new Effect(
                    $"{CardName}: deal total-mv damage to each chosen target",
                    () =>
                    {
                        // CR 202.3 — mana value is computed from the printed
                        // mana cost. Sum across the discarded set; X in any
                        // zone other than the stack is 0 (CR 202.3b), which
                        // ManaCost.Parse already honours.
                        var total = 0;
                        foreach (var disc in discardCost.Discarded)
                        {
                            total += Majik.Core.ValueObjects.ManaCost
                                .Parse(disc.ManaCost).TotalValue;
                        }

                        if (total <= 0) return; // nothing to deal — clean stop.
                        if (chosen.Targets.Count == 0) return;

                        foreach (var raw in chosen.Targets[0])
                        {
                            var live = resolver(raw);
                            // CR 608.2b — resolution-time legality check
                            // for each target. DealDamageAny no-ops on
                            // shapes it doesn't recognise.
                            switch (live)
                            {
                                case Permanent perm when perm.Zone != ZoneType.Battlefield:
                                    continue;
                                case Player:
                                case Creature:
                                case Planeswalker:
                                    Fx.DealDamageAny(live, total);
                                    break;
                            }
                        }
                    }),
            },
            AdditionalCosts: new IAdditionalCost[] { discardCost });
    }
}
