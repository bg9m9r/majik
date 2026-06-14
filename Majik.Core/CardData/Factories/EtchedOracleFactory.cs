using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Etched Oracle (Fifth Dawn, {4}).
///
/// Artifact Creature — Wizard 0/0. Oracle text (errata):
///   "Sunburst (This creature enters with a +1/+1 counter on it for each
///    color of mana spent to cast it.)"
///   "{1}, Remove four +1/+1 counters from this creature: Target player
///    draws three cards."
///
/// ## Implemented
/// - Artifact Creature {4} 0/0 with owner/controller wired + Wizard
///   subtypes.
/// - <b>Sunburst (CR 702.44)</b> wired via the shared
///   <see cref="SunburstFactory.Build"/> primitive. As an artifact
///   creature, Etched Oracle's Sunburst lands +1/+1 counters at ETB time
///   (CR 702.44a). The keyword reads <see cref="Card.PendingCastColors"/>
///   stamped by <see cref="Majik.Core.Game.TurnDriver"/> after the mana
///   resolver computes "colors of mana spent" from the cross-spend pool
///   diff. When wired against the supplied <paramref name="replacements"/>
///   bus, Hardened Scales / Doubling Season bumps apply.
/// - <b>Activated ability (CR 602.1)</b>: {1}, remove four +1/+1
///   counters: target player draws three cards.
///   - Mana cost wired as <see cref="ManaCostCost"/>.
///   - <b>Counter-removal cost (CR 118.3)</b> declared via
///     <see cref="AdditionalCost.RemoveCounters"/> — hoisted out of the
///     resolve closure into the declared cost list so cost-validation
///     (CR 602.2) and activation-legality scans see it. The cost is
///     re-source-safe: <see cref="ActivatedAbility.RebindTo"/> re-homes it
///     onto the new bearer via <see cref="AdditionalCost.RebindSource"/>
///     (CR 707.2 / 613.1f — Agatha's Soul Cauldron grant).
///   - One 1..1 "target player" <see cref="TargetRequest"/>; on resolve
///     the chosen player draws three (CR 608.2b — no legal target =
///     the ability does nothing). With no live target chosen (single-arg
///     dispatcher / shape tests) the controller draws as the fallback.
///
/// ## Deferred (v1 gaps)
/// - <b>Live agent target prompt</b>: production callers wire the chosen
///   target through <see cref="ActivatedAbility.SetChosenTargets"/> before
///   resolution; the no-target fallback (controller draws) keeps the
///   shape-test surface drivable.
/// </summary>
[CardName("Etched Oracle")]
public static class EtchedOracleFactory
{
    public const string CardName = "Etched Oracle";
    public const string PrintedManaCost = "{4}";
    public const string AbilityManaCost = "{1}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int CountersToRemove = 4;
    public const int CardsDrawn = 3;

    /// <summary>
    /// Construct Etched Oracle with no live runtime wiring. Sunburst's
    /// ETB counters arrive via the +1/+1 branch when
    /// <see cref="Card.PendingCastColors"/> is set; the activated
    /// ability draws for the controller when no target is chosen
    /// (single-arg dispatcher path).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Etched Oracle. When <paramref name="replacements"/> is
    /// supplied, Sunburst's counter placement routes through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season bumps apply. The activated ability's draw reads the chosen
    /// "target player" (or the controller when no target was chosen — the
    /// shape-test / dispatcher path).
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wizard });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sunburst (CR 702.44) — shared primitive. Reads PendingCastColors
        // at ETB resolve time and stamps +1/+1 counters (CR 702.44a —
        // Etched Oracle is a Creature). Routes through CountersService.Add
        // so Hardened Scales bumps the count.
        // ----------------------------------------------------------------
        SunburstFactory.Build(card, replacements);

        // ----------------------------------------------------------------
        // Activated ability (CR 602.1): {1}, Remove four +1/+1 counters
        // from this creature: Target player draws three cards.
        // - Mana cost: {1} → ManaCostCost.
        // - Counter-removal cost: DECLARED via AdditionalCost.RemoveCounters
        //   (CR 118.3) — hoisted out of the resolve closure into the cost
        //   list so CostValidator / activation-legality scans see it. The
        //   cost is re-source-safe (rebinds onto the new bearer).
        // - Effect: the chosen target player draws three (CR 608.2b — no
        //   legal target = does nothing; controller draws as the no-target
        //   shape-test fallback).
        // ----------------------------------------------------------------
        // RE-SOURCE-SAFE (rebindSafe: true): the draw reads its target/controller
        // off the live ResolutionContext (ctx.ChosenTargets / ctx.Controller) —
        // NOT a captured ability instance — so ActivatedAbility.RebindTo (CR 707.2)
        // re-homes it correctly when Agatha's Soul Cauldron grants the ability.
        // The counter-removal half is a DECLARED cost that rebinds via
        // AdditionalCost.RebindSource, so the whole ability is sound under rebind.
        var drawEffect = new Effect(
            $"{CardName}: target player draws {CardsDrawn} cards",
            ctx =>
            {
                // Resolve the chosen target. CR 608.2b — if a target was
                // declared but is no longer legal (empty inner list) the
                // ability does nothing. With NO target chosen at all (the
                // single-arg dispatcher / shape-test path), fall back to the
                // controller so the surface stays drivable.
                var chosen = ctx.ChosenTargets;
                Player? target;
                if (chosen is { Count: > 0 })
                {
                    if (chosen[0].Count == 0) return ValueTask.CompletedTask; // CR 608.2b.
                    target = chosen[0][0] as Player;
                }
                else
                {
                    target = ctx.Controller ?? owner;
                }

                if (target != null)
                {
                    Fx.DrawCards(target, CardsDrawn);
                }

                return ValueTask.CompletedTask;
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(AbilityManaCost),
                // CR 118.3 — declared counter-removal cost (re-source-safe).
                AdditionalCost.RemoveCounters(card, CounterType.PlusOnePlusOne, CountersToRemove),
            },
            effects: new IEffect[] { drawEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Draw,
                    CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),
            },
            rebindSafe: true);

        card.AddAbility(ability);

        return card;
    }
}
