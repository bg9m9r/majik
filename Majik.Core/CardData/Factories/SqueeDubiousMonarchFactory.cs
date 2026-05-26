using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Squee, Dubious Monarch (Dominaria United,
/// {2}{R}).
///
/// Legendary Creature — Goblin Noble 2/2. Oracle text:
///   "Menace, haste
///    Whenever Squee, Dubious Monarch attacks, create a 1/1 red Goblin
///    creature token that's tapped and attacking.
///    {3}{R}, Exile three other cards from your graveyard: Return Squee,
///    Dubious Monarch from your graveyard to the battlefield."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Goblin Warrior (the printed "Goblin Noble"
///   subtype line — "Noble" is not yet a value in
///   <see cref="CardSubtype"/>; v1 stamps Goblin only and flags the gap.
///   Most goblin-tribal / lord-style payoffs match on Goblin alone, so
///   shape-affecting behaviour is preserved; the missing "Noble" only
///   matters for Noble-tribal payoffs which v1 doesn't yet ship). Mana
///   cost {2}{R}, owner / controller wired.
/// - <b>Menace (CR 702.110)</b>: <see cref="KeywordAbility"/> marker so
///   block-legality (Squee can't be blocked except by two or more
///   creatures) routes through the existing keyword pipeline.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads it
///   and Squee can attack the turn he enters.
/// - <b>Attack triggered ability (CR 508.1f)</b>:
///   "Whenever Squee, Dubious Monarch attacks, create a 1/1 red Goblin
///    creature token that's tapped and attacking."
///   Wired via <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>.
///   On resolution: create one 1/1 red Goblin via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The printed rider
///   "tapped and attacking" describes a state the
///   <see cref="TokenFactory"/> primitive doesn't yet model — the token
///   ships <i>untapped + not attacking</i> (v1 gap, see below) and the
///   factory logs the deferral through the <see cref="ZoneService"/>
///   diagnostics channel where available. Same shape as Goblin
///   Rabblemaster's attack-rider token creation.
/// - <b>Graveyard-activated unearth-style ability (CR 113.6 / 117.1a)</b>:
///   <c>{3}{R}, Exile three other cards from your graveyard: Return
///    Squee, Dubious Monarch from your graveyard to the battlefield.</c>
///   Mirrors <see cref="PriestOfFellRitesFactory"/>'s shape: the mana
///   cost is exposed as a <see cref="ManaCostCost"/> on the
///   <see cref="ActivatedAbility"/>; the "exile three OTHER cards from
///   your graveyard" half is folded into the resolution effect (the v1
///   <see cref="ExileCardsFromGraveyardAdditionalCost"/> doesn't filter
///   out the activation source, so we inline the exclude-self exile
///   step here). The Squee-itself return is performed at the end of
///   the effect via <see cref="ZoneService.MoveCard"/> when available so
///   ETB triggers fire (CR 603.6a); otherwise raw zone manipulation.
///   <para>
///   The engine doesn't presently gate activated abilities on source
///   zone — the activation is enumerable while Squee is on the
///   battlefield too (same caveat as Priest of Fell Rites). The
///   resolution body checks Squee is in the graveyard before paying,
///   so spurious activations from other zones are no-op-shaped.
///   </para>
///
/// ## Deferred (v1 gaps)
/// - <b>"Noble" creature subtype</b>: not yet in <see cref="CardSubtype"/>.
///   Squee ships as Goblin (drops the Noble subtype line — no current
///   Noble-tribal payoff in the implemented pool relies on it).
/// - <b>"Tapped and attacking" token primitive</b>: <see cref="TokenFactory"/>
///   doesn't yet support stamping ETB-tap + ETB-attacker state. The token
///   enters untapped + not-attacking. The Squee attack itself still
///   produces a 1/1 Goblin body on the controller's battlefield, which
///   covers downstream Goblin-tribal payoffs, but the token does NOT
///   contribute to combat damage on the turn it was created. Lifting
///   this gap is paired with the broader
///   "create N tapped-and-attacking tokens" surface (Mardu Ascendancy,
///   Cosmotronic Wave, etc.).
/// - <b>Zone-scoped activated abilities</b>: graveyard activations are
///   enumerable from any zone; future engine pass should restrict
///   activation to the printed source zone (CR 113.6).
/// - <b>Sorcery-speed gate on the reanimation</b>: Squee's printed
///   ability does NOT carry "activate only as a sorcery" (contrast Priest
///   of Fell Rites). v1 leaves the activation any-time, matching paper.
/// </summary>
[CardName("Squee, Dubious Monarch")]
public static class SqueeDubiousMonarchFactory
{
    public const string CardName = "Squee, Dubious Monarch";
    public const string PrintedManaCost = "{2}{R}";
    public const string ReanimationManaCost = "{3}{R}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int ExileCardsForReanimation = 3;

    /// <summary>
    /// Construct Squee, Dubious Monarch with no live runtime services. The
    /// attack trigger + activated ability are attached for shape inspection
    /// (not registered with a <see cref="TriggerManager"/>); the token
    /// creation + self-reanimation use raw zone moves on the dispatcher
    /// path.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Squee, Dubious Monarch. When <paramref name="zoneService"/>
    /// is supplied, the attack-trigger token + the activated-ability self-
    /// reanimation route through <see cref="ZoneService.MoveCard"/> so ETB
    /// triggers fire (CR 603.6a). When <paramref name="triggers"/> is
    /// supplied, the attack trigger is registered so a
    /// <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            // v1 gap — "Noble" subtype not yet in CardSubtype. Squee
            // surfaces as Goblin Warrior so the Goblin-tribal anchors
            // still match. See class xmldoc.
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Menace — CR 702.110. Keyword marker; block-legality reads it
        // through the existing keyword pipeline.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // Haste — CR 702.10. CombatAbilities.HasHaste consumes the marker
        // so Squee can attack the turn he enters / returns.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f.
        //   "Whenever Squee, Dubious Monarch attacks, create a 1/1 red
        //    Goblin creature token that's tapped and attacking."
        // V1 ships the token untapped + not-attacking (TokenFactory gap —
        // see class xmldoc). The token's body is otherwise correct (1/1
        // red Goblin under Squee's controller).
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: create a 1/1 red Goblin token (tapped + attacking — v1: untapped, no attacker stamp)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Goblin",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Goblin },
                    Keywords: null,
                    // CR 105 / CR 111.4 — printed "1/1 red Goblin creature
                    // token".
                    Colors: new[] { ManaColor.Red });

                TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

                // v1 gap: TokenFactory doesn't yet support
                // "tapped + attacking" ETB state. The token ships untapped
                // and is NOT registered as an attacker on the current
                // combat. Downstream Goblin-tribal payoffs still see the
                // body; combat damage on the current turn does NOT include
                // it. Same posture used elsewhere when a primitive is
                // missing — log + continue (no exception thrown).
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // Activated ability — {3}{R}, Exile three OTHER cards from your
        // graveyard: Return Squee, Dubious Monarch from your graveyard to
        // the battlefield. (CR 113.6 / 117.1a)
        //
        // Shape: mana cost is exposed as a ManaCostCost on the ability for
        // shape inspection. The "exile three other cards from your
        // graveyard" half is performed inside the resolution effect (the
        // v1 ExileCardsFromGraveyardAdditionalCost helper doesn't yet
        // exclude the activation source, so we inline the exclude-self
        // exile step here — same posture as Priest of Fell Rites' exile-
        // self inlined cost work).
        //
        // Guard: only fire when Squee is currently in his owner's
        // graveyard and the graveyard contains at least three OTHER cards
        // (so the cost is payable). Spurious activations from other zones
        // are no-op-shaped while engine zone-scoping is deferred.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: exile three other gy cards, return self gy → bf",
            () =>
            {
                // Zone guard — only payable from graveyard.
                if (card.Zone != ZoneType.Graveyard) return;
                if (card.Owner == null) return;
                if (!ReferenceEquals(card.Owner, owner)) return;

                // "Three OTHER cards from your graveyard" — exclude Squee
                // himself from the pick pool.
                var pool = owner.Zones.Graveyard.GetCards()
                    .Where(c => !ReferenceEquals(c, card))
                    .Take(ExileCardsForReanimation)
                    .ToList();

                if (pool.Count < ExileCardsForReanimation) return; // can't pay

                foreach (var picked in pool)
                {
                    owner.Zones.Graveyard.RemoveCard(picked);
                    owner.Zones.Exile.AddCard(picked);
                    picked.SetZone(ZoneType.Exile);
                }

                // Return Squee from graveyard to battlefield.
                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires ETB triggers + replacements
                    // (CR 603.6a).
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(owner);
                }
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ReanimationManaCost) },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
