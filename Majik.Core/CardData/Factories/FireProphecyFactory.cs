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
/// Named-card factory for Fire Prophecy (Ikoria: Lair of Behemoths, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Fire Prophecy deals 3 damage to target creature. You may put a card
///    from your hand on the bottom of your library. If you do, draw a card."
///
/// ## Implementation
///
/// Two-part resolution body (CR 608.2 — follow the instructions in order):
///
/// 1. <b>Damage</b> — single 1..1 "target creature" request (CR 115.4), the
///    same single-target 3-damage shape as <see cref="FieryImpulseFactory"/>
///    (under spell mastery). A resolution-time legality re-check (CR 608.2b)
///    no-ops the damage if the chosen token is no longer a creature on the
///    battlefield.
///
/// 2. <b>Rummage rider</b> — "You may put a card from your hand on the bottom
///    of your library. If you do, draw a card." This is an optional looter:
///    the controller may bury one hand card on the bottom of their library
///    (CR 701.20-style bottom placement = append, mirroring
///    <see cref="AetherGustFactory"/>), and only <i>if a card was actually
///    bottomed</i> do they draw a card. The "if you do" gate (CR 608.2) means
///    declining — or having an empty hand — skips the draw entirely.
///
///    The optional decision + card choice are injected as closures (same
///    posture as Aether Gust's <c>topChooser</c> and Izzet Charm's
///    deterministic loot). v1 defaults are conservative: <c>mayBottom</c> =
///    "no" (decline), so the single-arg dispatcher path performs only the
///    damage. Production callers / tests wire the controller's decision +
///    hand pick.
///
/// The base card shape (name / Instant / {1}{R}) is materialised from the
/// embedded JSON definition (<c>fire-prophecy.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve-time spell body is
/// built on-demand by <see cref="BuildSpellDefinition"/> because the JSON
/// AbilityDefinition schema does not yet express a may-bottom-then-draw rider.
/// </summary>
[CardName("Fire Prophecy")]
public static class FireProphecyFactory
{
    public const string CardName = "Fire Prophecy";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "fire-prophecy";

    /// <summary>CR — Fire Prophecy deals 3 damage to target creature.</summary>
    public const int Damage = 3;

    /// <summary>
    /// Construct Fire Prophecy as an Instant owned by <paramref name="owner"/>.
    /// Base shape (name / Instant / {1}{R}) from the embedded JSON.
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
    /// Build the <see cref="SpellDefinition"/> used when Fire Prophecy is
    /// cast: 3 damage to a single creature target, followed by the optional
    /// bottom-a-card-then-draw rummage rider.
    /// </summary>
    /// <param name="controller">Spell controller — owns the hand the rider
    /// bottoms a card from and the library it draws from.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="mayBottom">Controller's "you may" decision: return true to
    /// bottom a card and (if a card is bottomed) draw. Null = decline (v1
    /// default — only the guaranteed damage happens).</param>
    /// <param name="cardChooser">Picks which hand card to bury when
    /// <paramref name="mayBottom"/> returns true; receives the current hand.
    /// Null = first card in hand. Returning null / an empty hand means nothing
    /// is bottomed, so the "if you do" draw clause never fires.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver,
        Func<bool>? mayBottom = null,
        Func<IReadOnlyList<ICard>, ICard?>? cardChooser = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 115.4 — "target creature": every creature on every
                // battlefield (CR 301). Bot ranks opponent creatures highest
                // via the Removal intent.
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Fire Prophecy: 3 damage to target creature", () =>
                    {
                        // CR 608.2b — resolution-time legality re-check: the
                        // target must still be a creature on the battlefield.
                        if (target is not Creature creature) return;
                        if (creature.Zone != ZoneType.Battlefield) return;
                        Fx.DealDamage(creature, Damage);
                    }),
                    Fx.Inline("Fire Prophecy: may bottom a card, then draw", () =>
                        ResolveRummage(controller, mayBottom, cardChooser)),
                };
            });
    }

    /// <summary>
    /// "You may put a card from your hand on the bottom of your library. If
    /// you do, draw a card." (CR 608.2 — sequential; the draw is gated on a
    /// card actually being bottomed.)
    /// </summary>
    private static void ResolveRummage(
        Player controller,
        Func<bool>? mayBottom,
        Func<IReadOnlyList<ICard>, ICard?>? cardChooser)
    {
        // "You may" — decline by default (v1 conservative posture).
        if (mayBottom == null || !mayBottom()) return;

        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // nothing to bottom → "if you do" never fires.

        var pick = cardChooser != null ? cardChooser(hand) : hand[0];
        if (pick == null) return;    // declined to choose → no bottom, no draw.
        if (!controller.Zones.Hand.ContainsCard(pick)) return;

        // Bottom of library = append (mirrors AetherGustFactory's bottom path).
        controller.Zones.Hand.RemoveCard(pick);
        controller.Zones.Library.AddCard(pick);
        pick.SetZone(ZoneType.Library);

        // "If you do, draw a card." A single top-of-library draw; an empty
        // library flags the player for SBA loss (CR 704.5b), mirroring
        // IzzetCharmFactory's loot draw.
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
