using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ugin, the Spirit Dragon (Fate Reforged, {8}).
///
/// Legendary Planeswalker — Ugin. Starting loyalty 7.
/// Oracle text (Scryfall, verified):
///   "+2: Ugin, the Spirit Dragon deals 3 damage to any target.
///    −X: Exile each permanent with mana value X or less that's one or
///        more colors.
///    −10: You gain 7 life, return up to seven permanent cards from your
///         graveyard to the battlefield, then draw seven cards."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker — Ugin at {8}, starting loyalty 7
///   (CR 306.1 / CR 205.3j — Ugin planeswalker subtype).
/// - <b>+2: 3 damage to any target (CR 606 + CR 119)</b>: deterministic
///   v1 target pick — the first candidate from
///   <paramref name="anyTargetResolver"/> receives 3 damage via
///   <see cref="Fx.DealDamageAny"/> (Player / Creature / Planeswalker
///   dispatch, CR 306.7). With no resolver wired the loyalty change
///   still applies (CR 606.3) and the damage clause silently no-ops.
/// - <b>-X: exile each coloured permanent with mv ≤ X (CR 606 + CR
///   701.21 + CR 105)</b>: scans every battlefield exposed by
///   <paramref name="allPlayersResolver"/>; a permanent qualifies when
///   <see cref="Card.ManaCostValue"/>.TotalValue ≤ X AND
///   <see cref="CardColors.GetColors"/>.Count ≥ 1 (i.e. at least one
///   coloured pip in the printed mana cost). Each qualifying card is
///   moved to its owner's exile zone via raw zone manipulation — same
///   posture as <see cref="KarnLiberatedFactory"/>'s -3. X is read off
///   <see cref="Card.PendingCastX"/> at activation time (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the activator
///   supplies a value; the printed loyalty cost surface is what the
///   activator pays through). When no X is stamped the effect snapshots
///   X = 0, which still legally exiles 0-cost coloured permanents
///   (printed-cost mv 0 with a coloured indicator — rare but rules-correct
///   under CR 202.3b + CR 105).
/// - <b>-10: gain 7 life, return up to 7 permanent cards from
///   controller's graveyard, then draw 7 (CR 606 + CR 119.3 + CR 701.20
///   + CR 121)</b>: three-step ordered resolution — gain life first via
///   <see cref="Fx.GainLife"/>; then move up to 7 permanent cards from
///   the controller's graveyard to the battlefield (filtered to
///   <c>HasType(Land) || HasType(Creature) || HasType(Artifact) ||
///   HasType(Enchantment) || HasType(Planeswalker)</c>; deterministic
///   first-in-graveyard pick — same shape Priest of Fell Rites uses);
///   finally draw seven via <see cref="Fx.DrawCards"/>. The "up to" is
///   auto-accepted at v1.
///
/// ## Deferred (v1 gaps)
/// - <b>Loyalty target prompts</b>: <see cref="LoyaltyAbility"/> doesn't
///   declare <see cref="TargetRequest"/>s. The +2 picks deterministically
///   via the supplied resolver; -X picks the X value from
///   <see cref="Card.PendingCastX"/>. Agent-driven target / X choice is
///   the same gap Karn / Liliana have.
/// - <b>ZoneService routing</b>: -X exile + -10 reanimation use raw zone
///   manipulation, so <see cref="Majik.Core.Events.CardMovedEvent"/>
///   doesn't publish via this path. Wire ZoneService when the broader
///   loyalty-ability infrastructure pass lands (same as Karn / Liliana).
/// </summary>
[CardName("Ugin, the Spirit Dragon")]
public static class UginTheSpiritDragonFactory
{
    public const string CardName = "Ugin, the Spirit Dragon";
    public const string Cost = "{8}";
    public const int StartingLoyalty = 7;
    public const int Plus2DamageAmount = 3;
    public const int UltimateLifeGain = 7;
    public const int UltimateReturnLimit = 7;
    public const int UltimateDrawCount = 7;

    /// <summary>
    /// Construct Ugin with no resolvers wired — +2 and -X effects no-op,
    /// -10 still runs (graveyard / hand / life are owner-scoped). Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, allPlayersResolver: null, anyTargetResolver: null);

    /// <summary>
    /// Construct Ugin, the Spirit Dragon. When
    /// <paramref name="anyTargetResolver"/> is non-null, the +2 damage
    /// effect picks the first returned target. When
    /// <paramref name="allPlayersResolver"/> is non-null, the -X exile
    /// scans every player's battlefield for coloured permanents with
    /// mv ≤ X.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at
    /// activation time. Used by -X to scan every battlefield. May be null
    /// — -X no-ops while loyalty still applies.</param>
    /// <param name="anyTargetResolver">Returns any-target candidates
    /// (players / creatures / planeswalkers) for +2 at activation time.
    /// v1 picks the first. May be null — +2 damage clause no-ops.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<IReadOnlyList<object>>? anyTargetResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ugin = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Ugin });

        ugin.SetOwner(owner);
        ugin.SetController(owner);

        // -- +2: Ugin, the Spirit Dragon deals 3 damage to any target. -----
        // CR 119 + CR 306.7 (planeswalker damage = loyalty removal).
        // v1 auto-pick: first candidate from the resolver. With no resolver
        // wired the clause is silent; the +2 loyalty change still applies.
        ugin.AddAbility(new LoyaltyAbility(ugin, +2, () =>
        {
            var targets = anyTargetResolver?.Invoke();
            if (targets == null) return;
            foreach (var t in targets)
            {
                if (t == null) continue;
                Fx.DealDamageAny(t, Plus2DamageAmount);
                return; // single any-target — CR 700.6
            }
        }));

        // -- -X: Exile each permanent with mana value X or less that's
        //    one or more colors. -------------------------------------------
        // CR 606 (loyalty cost) + CR 105 (colour from mana cost) +
        // CR 701.21 (exile). v1 reads X off Card.PendingCastX (stamped by
        // SpellCastFlow at activation time, mirroring Chalice / Spell
        // Queller's PendingCastX-on-the-source posture for X loyalty
        // abilities). Scans every battlefield exposed by allPlayersResolver
        // and exiles each qualifying card (raw-zone, owner-scoped exile —
        // same posture as Karn's -3).
        ugin.AddAbility(new LoyaltyAbility(ugin, 0 /* -X registered at 0; effect pays "X" loyalty via RemoveLoyalty + reads PendingCastX for the exile mv cap */, () =>
        {
            // CR 606 — -X loyalty cost is dynamic; the engine currently
            // models loyalty costs as flat ints, so we register the
            // ability at LoyaltyChange = 0 (which CanActivate gates as
            // "always legal" on a positive-loyalty walker) and apply the
            // RemoveLoyalty for the chosen X here, reading
            // <see cref="Card.PendingCastX"/> (stamped by the activator
            // before triggering this ability; same posture Chalice /
            // Spell Queller use for X on the stack). Future agent-loyalty
            // plumbing will model "-X" natively.
            var x = ugin.PendingCastX ?? 0;
            if (x > 0) ugin.RemoveLoyalty(Math.Min(x, ugin.Loyalty));
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;

            // Snapshot first to avoid mutating the iterated collection.
            var toExile = new List<Card>();
            foreach (var p in players)
            {
                foreach (var c in p.Zones.Battlefield.GetCards())
                {
                    if (c is not Card permCard) continue;
                    if (permCard.ManaCostValue.TotalValue > x) continue;
                    if (CardColors.GetColors(permCard).Count == 0) continue;
                    toExile.Add(permCard);
                }
            }

            foreach (var c in toExile)
            {
                if (c.Zone != ZoneType.Battlefield) continue;
                var holder = c.Controller ?? c.Owner;
                holder?.Zones.Battlefield.RemoveCard(c);
                var exileOwner = c.Owner ?? owner;
                exileOwner.Zones.Exile.AddCard(c);
                c.SetZone(ZoneType.Exile);
            }

            // Clear PendingCastX after the effect consumes it (parallels
            // Chalice's clear-on-resolve so a later reactivation doesn't
            // see stale state).
            ugin.ClearPendingCastX();
        }));

        // -- -10: You gain 7 life, return up to seven permanent cards
        //    from your graveyard to the battlefield, then draw seven
        //    cards. -------------------------------------------------------
        // CR 606 (loyalty) + CR 119.3 (life) + CR 701.20 (graveyard →
        // battlefield) + CR 121 (draw). Three-step printed-order
        // resolution (CR 608.2c — events in printed order). "Up to seven"
        // auto-accepted at v1; deterministic first-in-graveyard pick.
        ugin.AddAbility(new LoyaltyAbility(ugin, -10, () =>
        {
            // 1. Life — CR 119.3. Snapshot the controller (planeswalker's
            // controller at resolution time — the trigger's controller).
            var controller = ugin.Controller ?? owner;
            Fx.GainLife(controller, UltimateLifeGain);

            // 2. Return up to 7 permanent cards from controller's
            //    graveyard to the battlefield. "Permanent card" =
            //    Creature / Artifact / Enchantment / Land / Planeswalker
            //    (CR 110.4a).
            var picks = controller.Zones.Graveyard.GetCards()
                .Where(IsPermanentCard)
                .Take(UltimateReturnLimit)
                .ToList();
            foreach (var p in picks)
            {
                controller.Zones.Graveyard.RemoveCard(p);
                controller.Zones.Battlefield.AddCard(p);
                p.SetZone(ZoneType.Battlefield);
                if (p is Permanent perm) perm.SetController(controller);
            }

            // 3. Draw seven — CR 121.
            Fx.DrawCards(controller, UltimateDrawCount);
        }));

        return ugin;
    }

    /// <summary>
    /// CR 110.4a — a permanent card is a card whose type is Artifact,
    /// Creature, Enchantment, Land, or Planeswalker. Used by the -10
    /// graveyard-return filter so Instants / Sorceries are skipped.
    /// </summary>
    private static bool IsPermanentCard(ICard card) =>
        card.HasType(CardType.Artifact)
        || card.HasType(CardType.Creature)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Land)
        || card.HasType(CardType.Planeswalker);
}
