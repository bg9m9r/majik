using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hazoret the Fervent (Amonkhet, {3}{R}).
///
/// Legendary Creature — God 5/4. Oracle text (verified against Scryfall):
///   "Indestructible, haste
///    Hazoret can't attack or block unless you have one or fewer cards in
///    hand.
///    {2}{R}, Discard a card: Hazoret deals 2 damage to each opponent."
///
/// The card's base shape (name, Legendary supertype, God subtype, {3}{R},
/// 5/4) is materialised from the embedded JSON definition
/// (<c>hazoret-the-fervent.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours
/// (Indestructible / Haste keywords, the can't-attack-or-block static, the
/// discard-cost burn) are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers,
/// predicate-mode combat restrictions, or discard-cost activated abilities,
/// so they live in the factory (same posture as the other JSON-backed
/// cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
///
/// - 5/4 Legendary Creature — God at {3}{R}, owner / controller wired.
/// - <b>Indestructible (CR 702.12) + Haste (CR 702.10)</b>:
///   <see cref="KeywordAbility"/> markers. SBA 704.5g + the destroy
///   pipeline read Indestructible via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>;
///   Haste is read by the combat / summoning-sickness gate. Same wiring
///   as <see cref="AvacynAngelOfHopeFactory"/> (Indestructible) +
///   <see cref="StormbreathDragonFactory"/> (Haste).
/// - <b>"Hazoret can't attack or block unless you have one or fewer cards
///   in hand" (CR 508.1c / CR 509.1c)</b>: two predicate-mode
///   <see cref="CombatRestrictionEffect"/> instances
///   (<see cref="CombatRestriction.CannotAttack"/> +
///   <see cref="CombatRestriction.CannotBlock"/>), each scoped to Hazoret
///   itself (the predicate matches only when the queried creature IS
///   Hazoret) and tripping when the controller holds two or more cards
///   ("unless one or fewer" = "while two or more"). The hand-size read is
///   live, so the lock lifts the instant the controller discards down to
///   one card (the discard-cost burn below is the natural enabler — the
///   classic Hazoret play pattern). Gated on Hazoret being on the
///   battlefield (CR 603.6e). Same predicate-mode shape as
///   <see cref="EnsnaringBridgeFactory"/>'s "power &gt; hand size", but
///   self-scoped and dual-restriction. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
/// - <b>"{2}{R}, Discard a card: Hazoret deals 2 damage to each opponent"
///   (CR 602 / CR 117.1 / CR 701.16a)</b>: an <see cref="ActivatedAbility"/>
///   whose cost list is <see cref="ManaCostCost"/>("{2}{R}") +
///   <see cref="DiscardACardCost"/>. The resolve effect deals 2 damage to
///   every opponent supplied by the optional <paramref name="opponentsResolver"/>
///   via <see cref="Fx.DealDamageAny"/> (the controller is skipped
///   defensively). Same each-opponent-damage shape as
///   <see cref="BoltwaveFactory"/> + <see cref="StormbreathDragonFactory"/>'s
///   becomes-monstrous burn; without a resolver the burn finds no
///   opponents (defensive no-op for shape-only tests).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape + keyword markers + the
///   activated ability (which no-ops its burn without an opponents
///   resolver). The combat restriction is NOT registered (no
///   continuous-effects service). The overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — additionally
///   registers the can't-attack-or-block restriction.
/// - <see cref="Create(Player, ContinuousEffectsService?, Func{IReadOnlyList{Player}}?)"/>
///   — fully wired: restriction + an opponents resolver for the burn.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard chooser</b>: <see cref="DiscardACardCost"/> deterministically
///   discards the first card in hand when no <see cref="DiscardACardCost.Target"/>
///   is nominated (same v1 picker posture as the rest of the cost surface —
///   Liliana of the Veil etc.).
/// - <b>Opponents resolver threading</b>: the burn's opponent list is
///   supplied by the caller at resolve time rather than read off a live
///   game table — identical posture to Boltwave / Creeping Chill / Omnath
///   / Stormbreath Dragon.
/// - <b>Bot attack/block planner</b>: the heuristic bot does not yet read
///   the <see cref="CombatRestriction"/> when proposing attackers /
///   blockers; the engine rejects any illegal declaration the predicate
///   catches (same posture as Ensnaring Bridge / Leyline Binding).
///
/// CR rule references: 205.2 (Legendary), 205.3m (God subtype), 702.10
/// (Haste), 702.12 (Indestructible), 508.1c / 509.1c (combat
/// restrictions), 602 (activated abilities), 117.1 / 701.16a (discard
/// cost), 119 (damage).
/// </summary>
[CardName("Hazoret the Fervent")]
public static class HazoretTheFerventFactory
{
    public const string CardName = "Hazoret the Fervent";
    public const string Slug = "hazoret-the-fervent";
    public const int Power = 5;
    public const int Toughness = 4;
    public const string ActivatedCost = "{2}{R}";
    public const int ActivatedDamage = 2;

    /// <summary>
    /// CR 701.16a — "unless you have one or fewer cards in hand" means the
    /// restriction is active while the controller holds at least this many
    /// cards. "One or fewer" lifts the lock; two or more re-imposes it.
    /// </summary>
    public const int HandSizeLockThreshold = 2;

    /// <summary>
    /// Construct Hazoret with no continuous-effects service and no
    /// opponents resolver. Keyword markers + the activated ability are
    /// attached (the burn no-ops without a resolver); the can't-attack-or-
    /// block restriction is NOT registered. Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, opponentsResolver: null);

    /// <summary>
    /// Construct Hazoret with a continuous-effects service for the
    /// can't-attack-or-block restriction but no opponents resolver (the
    /// burn still no-ops). Convenience overload for restriction tests.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, opponentsResolver: null);

    /// <summary>
    /// Construct Hazoret with an opponents resolver for the burn but no
    /// continuous-effects service (restriction skipped). Convenience
    /// overload for activated-ability tests.
    /// </summary>
    public static Creature Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver)
        => Create(owner, continuousEffects: null, opponentsResolver);

    /// <summary>
    /// Construct a fully-wired Hazoret the Fervent.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Game-level continuous-effects
    /// service. When supplied, the two predicate-mode combat restrictions
    /// (CannotAttack + CannotBlock) are registered, gated on Hazoret being
    /// on the battlefield. Pass null to skip the restriction.</param>
    /// <param name="opponentsResolver">Live opponents list used by the
    /// activated burn at resolve time. Pass null — the burn then finds no
    /// opponents (defensive no-op).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // supertype, God subtype, {3}{R}, 5/4). The JSON carries no
        // abilities — the printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.12 / 702.10 — Indestructible + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // "Hazoret can't attack or block unless you have one or fewer cards
        // in hand." CR 508.1c (attack) + CR 509.1c (block).
        //
        // Predicate-mode CombatRestrictionEffect, self-scoped: the
        // predicate matches only when the queried creature IS Hazoret, and
        // only while the controller holds >= 2 cards ("unless one or fewer"
        // == "while two or more"). The hand size is read live every
        // validation pass, so discarding to one card (e.g. via the burn
        // below) lifts both restrictions immediately.
        //
        // "you" — Hazoret's controller (CR 109.5). Gate: only active while
        // Hazoret is on the battlefield (CR 603.6e); off-battlefield the
        // service's prune sweep drops the effect.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            bool LockedForCombat(Creature queried)
            {
                if (!ReferenceEquals(queried, card)) return false; // self-scoped
                var ctrl = card.Controller;
                if (ctrl == null) return false;
                return ctrl.Zones.Hand.GetCards().Count() >= HandSizeLockThreshold;
            }

            bool OnBattlefield() => card.Zone == ZoneType.Battlefield;

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotBlock,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // "{2}{R}, Discard a card: Hazoret deals 2 damage to each
        // opponent." CR 602 (activated ability), CR 117.1 / 701.16a
        // (discard-a-card cost), CR 119 (damage).
        //
        // Cost stack: ManaCostCost("{2}{R}") + DiscardACardCost. Resolve:
        // deal 2 to each opponent supplied by the resolver via
        // Fx.DealDamageAny (the controller is skipped defensively). Without
        // a resolver the burn finds no opponents — a no-op for shape-only
        // tests.
        // ----------------------------------------------------------------
        var burn = new Effect(
            $"{CardName}: deal {ActivatedDamage} damage to each opponent",
            () =>
            {
                var controller = card.Controller ?? owner;
                var opponents = opponentsResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, controller)) continue; // defensive
                    if (opp.HasLost) continue;
                    Fx.DealDamageAny(opp, ActivatedDamage);
                }
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedCost),
                new DiscardACardCost(),
            },
            effects: new IEffect[] { burn }));

        return card;
    }
}
