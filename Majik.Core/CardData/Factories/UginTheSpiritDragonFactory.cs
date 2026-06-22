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
///   701.21 + CR 105)</b>: <b>pure-enumeration each-permanent effect</b>.
///   Reads every battlefield off the LIVE <see cref="ResolutionContext"/>
///   (<c>rc.Game.AllPlayers</c>) at RESOLUTION — no captured player-list
///   resolver, so it runs on the prod routed build (the
///   <c>resolver-null-loyalty-each-player-context-read</c> deferral fix;
///   same context-read pattern as <see cref="ContextOpponents"/> / #2549 /
///   #2551, now applied on the loyalty path). A permanent qualifies when
///   <see cref="Card.ManaCostValue"/>.TotalValue ≤ X AND
///   <see cref="CardColors.GetColors"/>.Count ≥ 1 (at least one coloured
///   pip in the printed mana cost). Each qualifying card is moved to its
///   owner's exile zone via raw zone manipulation — same posture as
///   <see cref="KarnLiberatedFactory"/>'s -3. X is read off the source's
///   <see cref="Card.PendingCastX"/> at resolution (stamped by the
///   activator; mirrors Chalice / Spell Queller's PendingCastX posture for
///   X abilities). When no X is stamped the effect snapshots X = 0, which
///   still legally exiles 0-cost coloured permanents (CR 202.3b + CR 105).
/// - <b>-10: gain 7 life, return up to 7 permanent cards from
///   controller's graveyard, then draw 7 (CR 606 + CR 119.3 + CR 701.20
///   + CR 121)</b>: three-step ordered resolution — gain life first via
///   <see cref="Fx.GainLife"/>; then move up to 7 permanent cards from
///   the controller's graveyard to the battlefield (filtered to
///   <c>HasType(Land) || HasType(Creature) || HasType(Artifact) ||
///   HasType(Enchantment) || HasType(Planeswalker)</c>; deterministic
///   first-in-graveyard pick — same shape Priest of Fell Rites uses);
///   finally draw seven via <see cref="Fx.DrawCards"/>. The "up to" is
///   auto-accepted at v1. The controller is read off the LIVE
///   <see cref="ResolutionContext"/> (<c>rc.Controller</c>) at resolution.
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
    /// Construct Ugin with no any-target resolver wired — the +2 damage
    /// clause no-ops while the loyalty change still applies. -X and -10 read
    /// the live game / controller off the <see cref="ResolutionContext"/>, so
    /// they run on this routed build too (they need no resolver). This is the
    /// production routed overload (<c>NamedCardFactory.Create(name, owner)</c>).
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, anyTargetResolver: null);

    /// <summary>
    /// Construct Ugin, the Spirit Dragon. When
    /// <paramref name="anyTargetResolver"/> is non-null, the +2 damage
    /// effect picks the first returned target. The -X exile + -10 ultimate
    /// read the live battlefield / controller off the resolution context and
    /// need no resolver.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="anyTargetResolver">Returns any-target candidates
    /// (players / creatures / planeswalkers) for +2 at activation time.
    /// v1 picks the first. May be null — +2 damage clause no-ops.</param>
    public static Planeswalker Create(
        Player owner,
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
        // CR 701.21 (exile). PURE-ENUMERATION each-permanent effect — reads
        // every battlefield off the LIVE ResolutionContext (rc.Game.AllPlayers)
        // at resolution, NOT from a build-time captured resolver (the prod
        // routed single-arg Create leaves any captured resolver null → the
        // clause used to be INERT in real games; the resolver-null loyalty
        // deferral fix). X is read off the source's PendingCastX (stamped by
        // the activator; same posture Chalice / Spell Queller use for X on the
        // stack). Registered at LoyaltyChange = 0 because the engine models
        // loyalty costs as flat ints; the effect pays the chosen X loyalty
        // inline via RemoveLoyalty.
        ugin.AddAbility(new LoyaltyAbility(ugin, 0 /* -X registered at 0; effect pays "X" loyalty via RemoveLoyalty + reads PendingCastX for the exile mv cap */,
            new[]
            {
                Fx.Inline("Exile each coloured permanent with mv ≤ X", rc =>
                {
                    // CR 606 — -X loyalty cost is dynamic; the engine models
                    // loyalty costs as flat ints, so the ability is registered
                    // at LoyaltyChange = 0 and the RemoveLoyalty for the chosen
                    // X is applied here, reading PendingCastX off the source
                    // (the captured planeswalker IS the loyalty ability's
                    // Source on both the prod and legacy paths).
                    var x = ugin.PendingCastX ?? 0;
                    if (x > 0) ugin.RemoveLoyalty(Math.Min(x, ugin.Loyalty));

                    // Read every battlefield off the live game context (CR 105 —
                    // colour is determined from the printed mana cost). Snapshot
                    // first to avoid mutating the iterated collection.
                    var players = rc.Game?.AllPlayers;
                    if (players == null)
                    {
                        ugin.ClearPendingCastX();
                        return default;
                    }

                    var toExile = new List<Card>();
                    foreach (var p in players)
                    {
                        if (p == null) continue;
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
                    return default;
                }),
            }));

        // -- -10: You gain 7 life, return up to seven permanent cards
        //    from your graveyard to the battlefield, then draw seven
        //    cards. -------------------------------------------------------
        // CR 606 (loyalty) + CR 119.3 (life) + CR 701.20 (graveyard →
        // battlefield) + CR 121 (draw). Three-step printed-order
        // resolution (CR 608.2c — events in printed order). "Up to seven"
        // auto-accepted at v1; deterministic first-in-graveyard pick. The
        // controller is read off the LIVE ResolutionContext (rc.Controller)
        // at resolution, falling back to the captured owner on the legacy
        // direct-activation path.
        ugin.AddAbility(new LoyaltyAbility(ugin, -10,
            new[]
            {
                Fx.Inline("Gain 7, reanimate up to 7 permanents, draw 7", rc =>
                {
                    // 1. Life — CR 119.3. The controller is the planeswalker's
                    // controller at resolution time.
                    var controller = rc.Controller ?? ugin.Controller ?? owner;
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
                    return default;
                }),
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
