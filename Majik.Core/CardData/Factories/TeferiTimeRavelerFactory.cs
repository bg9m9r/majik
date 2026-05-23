using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Teferi, Time Raveler (War of the Spark, {1}{W}{U}).
///
/// Legendary Planeswalker — Teferi, starting loyalty 4.
/// Oracle text:
///   "Each opponent can cast spells only any time they could cast a sorcery.
///    +1: Until your next turn, you may cast sorcery spells as though they
///        had flash.
///    -3: Return target artifact, creature, or enchantment to its owner's
///        hand. Draw a card."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 4, Teferi subtype, mana cost
///   {1}{W}{U}.
/// - <b>Printed static</b> (CR 117.1a): "Each opponent can cast spells only
///   any time they could cast a sorcery." Wired via a <see
///   cref="SorcerySpeedRestrictionEffect"/>: while Teferi is on the
///   battlefield, every player returned by <c>opponentResolver</c> is
///   registered into <see cref="Majik.Core.Rules.CastingRestrictions"/>,
///   and <see cref="Majik.Core.Rules.ActionValidator"/> rejects their
///   non-sorcery-speed casts. The effect detaches as Teferi leaves the
///   battlefield via <see cref="CardMovedEvent"/> on the supplied bus.
/// - <b>-3 bounce + draw</b>: returns the first
///   artifact/creature/enchantment on the supplied target-permanent
///   resolver to its owner's hand; controller then draws a card.
///   v1 auto-pick: <c>LoyaltyAbility</c> doesn't yet declare
///   <see cref="TargetRequest"/>s. The
///   <see cref="Create(Player, Func{IReadOnlyList{Player}}?, Func{IReadOnlyList{Permanent}}?, ContinuousEffectsService?, IEventBus?)"/>
///   overload accepts a resolver returning candidate permanents; the
///   single-arg path no-ops the bounce (loyalty change still applies).
///
/// ## Deferred (v1 gaps)
/// - <b>+1 cast-sorceries-as-flash</b>: the engine doesn't yet model
///   "you may cast sorcery spells as though they had flash" as a
///   per-controller cast-time speed modifier. Wiring this requires
///   plumbing a controller-keyed "treat-as-flash" predicate through
///   <see cref="Majik.Core.Rules.TimingRules"/> /
///   <see cref="Majik.Core.Rules.ActionValidator"/>. The +1 ability is
///   shipped as a no-op body so the loyalty change still applies (CR
///   606.3 — cost is paid even if the effect does nothing).
/// - <b>Targeting prompt</b>: -3 picks the first matching permanent in
///   the supplied resolver deterministically rather than via the agent.
/// </summary>
public static class TeferiTimeRavelerFactory
{
    public const string CardName = "Teferi, Time Raveler";
    public const string Cost = "{1}{W}{U}";
    public const int StartingLoyalty = 4;

    /// <summary>
    /// Construct Teferi, Time Raveler with no resolvers wired. Suitable
    /// for shape / dispatcher tests — the printed static and -3 will not
    /// fire. Loyalty changes still apply.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, opponentResolver: null, targetResolver: null,
               effects: null, eventBus: null);

    /// <summary>
    /// Construct Teferi, Time Raveler with the printed-static lifecycle
    /// wired against <paramref name="eventBus"/> and the -3 bounce
    /// resolving against <paramref name="targetResolver"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Returns the set of players treated
    /// as opponents at restriction-sync time. Called when Teferi enters
    /// the battlefield. May be null — restriction simply won't activate.</param>
    /// <param name="targetResolver">Returns candidate target permanents
    /// for -3 (artifacts/creatures/enchantments, anywhere on any
    /// battlefield the engine considers in-scope). v1 picks the first
    /// matching permanent.</param>
    /// <param name="effects">Continuous-effects service (reserved for
    /// future Teferi layer-effects; currently unused — sorcery-speed
    /// restrictions are a rules-modifier registry, not a layer effect).</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the
    /// printed static. May be null — Attach will still sync once.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        Func<IReadOnlyList<Permanent>>? targetResolver,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = effects; // reserved for future layer-effects

        var teferi = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Teferi });

        teferi.SetOwner(owner);
        teferi.SetController(owner);

        // -- Printed static (CR 117.1a) — "Each opponent can cast spells
        //    only any time they could cast a sorcery."
        if (opponentResolver != null)
        {
            var lifecycle = new SorcerySpeedRestrictionEffect(
                source: teferi,
                eventBus: eventBus,
                affectedPlayersResolver: opponentResolver);
            lifecycle.Attach();
        }

        // -- +1: cast sorceries as flash until your next turn. v1: no-op
        //    body (no controller-keyed cast-as-flash primitive yet).
        teferi.AddAbility(new LoyaltyAbility(teferi, +1, () => { /* deferred */ }));

        // -- -3: Return target artifact/creature/enchantment to its
        //    owner's hand; draw a card.
        teferi.AddAbility(new LoyaltyAbility(teferi, -3, () =>
        {
            // Auto-pick: first matching permanent from the resolver.
            // Without a resolver the bounce no-ops (loyalty change still
            // applies; controller still draws).
            if (targetResolver != null)
            {
                var candidates = targetResolver();
                if (candidates != null)
                {
                    foreach (var p in candidates)
                    {
                        if (p == null) continue;
                        if (!IsBounceTarget(p)) continue;
                        var pOwner = p.Owner ?? owner;
                        // Move from current zone (battlefield) to owner's
                        // hand. Mirror the WrennAndSix +1 zone-mechanic
                        // pattern: explicit Remove + Add + SetZone.
                        if (p.Controller != null)
                        {
                            p.Controller.Zones.Battlefield.RemoveCard(p);
                        }
                        else
                        {
                            pOwner.Zones.Battlefield.RemoveCard(p);
                        }
                        pOwner.Zones.Hand.AddCard(p);
                        p.SetZone(ZoneType.Hand);
                        break; // "target" — one permanent
                    }
                }
            }

            // Draw a card (no shuffle/loss-by-empty-library handling here;
            // the engine's draw pipeline picks that up elsewhere).
            var top = owner.Zones.Library.GetCards().FirstOrDefault();
            if (top != null)
            {
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
        }));

        return teferi;
    }

    private static bool IsBounceTarget(Permanent p)
    {
        return p.HasType(CardType.Artifact)
            || p.HasType(CardType.Creature)
            || p.HasType(CardType.Enchantment);
    }
}
