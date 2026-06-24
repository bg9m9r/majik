using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Take Out the Trash (Bloomburrow, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Take Out the Trash deals 3 damage to target creature or planeswalker.
///    If you control a Raccoon, you may discard a card. If you do, draw a
///    card."
///
/// ## Implementation
///
/// Two-part resolution body (CR 608.2 — follow the instructions in order):
///
/// 1. <b>Damage</b> — single 1..1 "target creature or planeswalker" request
///    (CR 115.4), the same target shape as <see cref="RipApartFactory"/>'s
///    damage mode. On resolution deals <see cref="Damage"/> (3) damage via
///    <see cref="Fx.DealDamageAny(object, int)"/> (CR 119 / CR 306.7 — damage
///    to a planeswalker removes that much loyalty). A resolution-time legality
///    re-check (CR 608.2b) no-ops the damage if the chosen object is no longer
///    a creature or planeswalker on the battlefield.
///
/// 2. <b>Raccoon looter rider</b> — "If you control a Raccoon, you may discard
///    a card. If you do, draw a card." A conditional optional looter
///    (CR 608.2): the rider only offers the discard when the controller
///    actually controls a creature with the Raccoon subtype at resolution
///    (CR 205.3m — Bloomburrow Raccoon lineage); the "you may" gate lets the
///    controller decline; and the "if you do" gate (CR 608.2) means the draw
///    fires only when a card is actually discarded. Same posture as
///    <see cref="FireProphecyFactory"/>'s rummage rider, but discard (to
///    graveyard) rather than bottom-of-library, and Raccoon-gated.
///
///    The optional decision + card choice are injected as closures (same
///    posture as Fire Prophecy). v1 defaults are conservative:
///    <c>mayDiscard</c> = "no" (decline), so the single-arg dispatcher path
///    performs only the damage. Production callers / tests wire the
///    controller's decision + hand pick.
///
/// The base card shape (name / Instant / {1}{R}) is materialised from the
/// embedded JSON definition (<c>take-out-the-trash.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve-time spell body is
/// built on-demand by <see cref="BuildSpellDefinition"/> because the JSON
/// AbilityDefinition schema does not yet express a Raccoon-gated may-discard
/// -then-draw rider.
/// </summary>
[CardName("Take Out the Trash")]
public static class TakeOutTheTrashFactory
{
    public const string CardName = "Take Out the Trash";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "take-out-the-trash";

    /// <summary>CR 119 — 3 damage to target creature or planeswalker.</summary>
    public const int Damage = 3;

    /// <summary>
    /// Construct Take Out the Trash as an Instant owned by
    /// <paramref name="owner"/>. Base shape (name / Instant / {1}{R}) from the
    /// embedded JSON.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// True iff <paramref name="controller"/> controls at least one creature
    /// with the Raccoon subtype on the battlefield (CR 205.3m). Gates the
    /// looter rider — "If you control a Raccoon, …".
    /// </summary>
    public static bool ControlsRaccoon(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .Any(c => c.HasType(CardType.Creature)
                   && c.HasSubtype(CardSubtype.Raccoon));
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Take Out the Trash is
    /// cast: 3 damage to a single creature/planeswalker target, followed by the
    /// Raccoon-gated optional discard-then-draw looter rider.
    /// </summary>
    /// <param name="controller">Spell controller — owns the battlefield checked
    /// for a Raccoon and the hand/library the rider loots.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="mayDiscard">Controller's "you may" decision: return true to
    /// discard a card and (if a card is discarded) draw. Null = decline (v1
    /// default — only the guaranteed damage happens). Only consulted when the
    /// controller controls a Raccoon.</param>
    /// <param name="cardChooser">Picks which hand card to discard when
    /// <paramref name="mayDiscard"/> returns true; receives the current hand.
    /// Null = first card in hand. Returning null / an empty hand means nothing
    /// is discarded, so the "if you do" draw clause never fires.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver,
        Func<bool>? mayDiscard = null,
        Func<IReadOnlyList<ICard>, ICard?>? cardChooser = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 115.4 — "target creature or planeswalker": every creature +
                // planeswalker on every battlefield (CR 302 / CR 306). Bot ranks
                // opponent permanents highest via the Removal intent.
                new TargetRequest(
                    "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                                 || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Take Out the Trash: 3 damage to target creature or planeswalker", () =>
                    {
                        // CR 608.2b — resolution-time legality re-check: only
                        // creatures and planeswalkers are legal targets.
                        if (target is not (Creature or Planeswalker)) return;
                        // CR 119 / CR 306.7 — 3 damage; a planeswalker target
                        // loses that much loyalty via Fx.DealDamageAny.
                        Fx.DealDamageAny(target, Damage);
                    }),
                    Fx.Inline("Take Out the Trash: if you control a Raccoon, may discard then draw", () =>
                        ResolveRaccoonLoot(controller, mayDiscard, cardChooser)),
                };
            });
    }

    /// <summary>
    /// "If you control a Raccoon, you may discard a card. If you do, draw a
    /// card." (CR 608.2 — sequential; the discard is gated on controlling a
    /// Raccoon, and the draw is gated on a card actually being discarded.)
    /// </summary>
    private static void ResolveRaccoonLoot(
        Player controller,
        Func<bool>? mayDiscard,
        Func<IReadOnlyList<ICard>, ICard?>? cardChooser)
    {
        // "If you control a Raccoon" — the whole rider is skipped otherwise.
        if (!ControlsRaccoon(controller)) return;

        // "You may discard a card" — decline by default (v1 conservative).
        if (mayDiscard == null || !mayDiscard()) return;

        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // nothing to discard → "if you do" never fires.

        var pick = cardChooser != null ? cardChooser(hand) : hand[0];
        if (pick == null) return;    // declined to choose → no discard, no draw.
        if (!controller.Zones.Hand.ContainsCard(pick)) return;

        // CR 701.8 — discard the chosen card (effect discard, not a cost).
        Fx.DiscardCard(controller, pick, wasCost: false);

        // "If you do, draw a card." A single top-of-library draw; an empty
        // library flags the player for SBA loss (CR 704.5b). Routed through
        // Fx.DrawCards so replacement effects + draw intents fire.
        Fx.DrawCards(controller, 1);
    }
}
