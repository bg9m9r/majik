using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Graveyard Trespasser — DFC front face (Innistrad:
/// Midnight Hunt, {2}{B}). Back face: Graveyard Glutton.
///
/// Creature — Human Werewolf 3/3. Oracle text (Scryfall verified, front):
///   "Ward—Discard a card.
///    Whenever this creature enters or attacks, exile up to one target card
///    from a graveyard. If a creature card was exiled this way, each opponent
///    loses 1 life and you gain 1 life.
///    Daybound (If a player casts no spells during their own turn, it becomes
///    night next turn.)"
///
/// Back face (Graveyard Glutton): Creature — Werewolf 4/4. "Ward—Discard a
/// card. Whenever this creature enters or attacks, exile up to two target
/// cards from graveyards. For each creature card exiled this way, each
/// opponent loses 1 life and you gain 1 life. Nightbound (…)."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Human Werewolf at {2}{B} (front face), owner / controller
///   set. <see cref="CardSubtype.Werewolf"/> subtype.
/// - <b>Daybound + Nightbound</b> (CR 702.145): the DFC carries a Daybound
///   marker (front) and a Nightbound marker (back), consumed by
///   <see cref="DayboundNightbound"/>. The game's untap-step day/night check
///   (CR 502.2 / 730.2, wired in <see cref="Majik.Core.Game.TurnDriver"/>)
///   flips the attached <see cref="MdfcState"/> between
///   "Graveyard Trespasser" (front, daybound) and "Graveyard Glutton" (back,
///   nightbound) as it becomes day/night.
/// - <b>"Enters or attacks" triggered ability</b> (CR 603.1 / 508.1f): two
///   <see cref="TriggeredAbility"/> instances (ETB-self + attack-self) sharing
///   one effect body — the standard pattern (see Archon of Cruelty). On
///   resolution: exile up to one target card from a graveyard; if a creature
///   card was exiled, each opponent loses 1 life and the controller gains 1
///   (CR 119.3). The "up to one target" is agent-driven via the target
///   request; absent a chosen target it deterministically prefers a creature
///   card in any graveyard (so the life rider is meaningful), else the first
///   available graveyard card, else no-op (CR 608.2b — "up to one" may pick
///   zero).
///
/// ## Implemented (Ward)
/// - <b>Ward—Discard a card (CR 702.21c non-mana ward).</b> Shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker plus a bound
///   <see cref="WardEffect"/> (<see cref="BuildWardEffect"/>) whose payment is
///   a real <see cref="DiscardACardCost"/>. <see cref="WardEffect.Resolve"/>
///   counters an opponent's targeting spell/ability unless they discard a
///   card — the discard tax is now functional.
///
/// ## Deferred (v1 gaps)
/// - <b>Back-face hot-swap (Graveyard Glutton 4/4 + exile-up-to-two).</b> As
///   with every v1 DFC (Delver, Ajani), the transform flips the MdfcState
///   only; the live Creature object stays a 3/3 Human Werewolf with the
///   exile-up-to-one rider. Full Layer-0 per-face characteristic replacement
///   (4/4 body, exile-up-to-two, "for each creature card" drain) is deferred.
/// </summary>
[CardName("Graveyard Trespasser")]
public static class GraveyardTrespasserFactory
{
    public const string FrontName = "Graveyard Trespasser";
    public const string BackName = "Graveyard Glutton";
    public const string FrontCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>Printed Ward cost — non-mana (discard a card), CR 702.21c.</summary>
    public const string WardDiscardCost = "Discard a card";

    /// <summary>
    /// CR 702.21 — Graveyard Trespasser's printed "Ward—Discard a card"
    /// effect, bound to the supplied <paramref name="card"/>. The ward cost is
    /// a real <see cref="DiscardACardCost"/> (non-mana ward); the mana portion
    /// is <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/>.
    /// <see cref="WardEffect.Resolve"/> counters an opponent's targeting
    /// spell/ability unless they discard a card (same posture as
    /// <see cref="RealitySmasherFactory.BuildWardEffect"/>).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new DiscardACardCost());

    /// <summary>
    /// Construct Graveyard Trespasser with no live TriggerManager wiring
    /// (shape / dispatcher path). The enters/attacks triggers and the
    /// daybound/nightbound markers are attached so structural assertions see
    /// them; the triggers are not registered with a manager.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, players: null);

    /// <summary>
    /// Construct Graveyard Trespasser with optional <see cref="TriggerManager"/>
    /// wiring and the live player list (needed for "each opponent" + scanning
    /// every graveyard for the exile target). When <paramref name="triggers"/>
    /// is supplied, both triggers are registered so the ETB / attack events
    /// land them on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, IReadOnlyList<Player>? players)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Werewolf });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — DFC face tracker. Front = Graveyard Trespasser (daybound),
        // back = Graveyard Glutton (nightbound). Starts front-face up. The
        // back-face characteristics carrier (Graveyard Glutton — 4/4 black
        // Werewolf) drives the Layer-0 per-face replacement: while back-face
        // up, ContinuousEffectsService.Compute seeds the 4/4 black Werewolf
        // body (the daybound/nightbound transform yields the correct body).
        card.MdfcState = new MdfcState(FrontName, BackName,
            BackFaceCharacteristics.Creature(
                name: BackName,
                power: 4,
                toughness: 4,
                subtypes: new[] { CardSubtype.Werewolf },
                colors: new[] { Majik.Core.ValueObjects.ManaColor.Black }));

        // CR 702.21 — Ward—Discard a card. Marker keyword for discovery; the
        // functional non-mana ward rider is exposed via BuildWardEffect /
        // WardEffect.Resolve (charges DiscardACardCost).
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // CR 702.145 — Daybound (front) + Nightbound (back) markers consumed
        // by DayboundNightbound. The Werewolf carries both; the transform
        // logic gates on the current face.
        card.AddAbility(new KeywordAbility(DayboundNightbound.DayboundKeyword, card, owner));
        card.AddAbility(new KeywordAbility(DayboundNightbound.NightboundKeyword, card, owner));

        // CR 603.1 / 508.1f — "Whenever this creature enters or attacks, …"
        // Both FACES' riders attach; an ActiveWhen face gate (CR 711.3) lets
        // only the active face's pair fire — no register/unregister churn on
        // the day/night flip. The two triggers per face share one effect body.
        //   FRONT (Graveyard Trespasser): exile up to ONE; drain ONCE if any
        //     creature card was exiled.
        //   BACK  (Graveyard Glutton):    exile up to TWO; drain PER creature
        //     card exiled.
        bool OnFrontFace() => card.MdfcState is { IsBackFace: false };
        bool OnBackFace() => card.MdfcState is { IsBackFace: true };

        IEffect BuildEffect(string face, string label, int maxCards, bool perCreatureDrain) =>
            new Effect(
                $"{face}: {label} — exile up to {(maxCards == 1 ? "one" : "two")} target card(s) from a graveyard; "
                + (perCreatureDrain
                    ? "for each creature card exiled, each opponent loses 1 / you gain 1"
                    : "if a creature card was exiled, each opponent loses 1 / you gain 1"),
                () => ResolveExileAndDrain(card, owner, players, maxCards, perCreatureDrain));

        void AttachPair(string face, int maxCards, bool perCreatureDrain, Func<bool> activeWhen)
        {
            var etbTrigger = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: Triggers.OnEnterBattlefieldSelf(card),
                effects: new IEffect[] { BuildEffect(face, "ETB", maxCards, perCreatureDrain) },
                activeZones: new[] { ZoneType.Battlefield },
                activeWhen: activeWhen);
            card.AddAbility(etbTrigger);
            triggers?.RegisterTriggeredAbility(etbTrigger);

            var attackTrigger = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: Triggers.OnAttackSelf(card),
                effects: new IEffect[] { BuildEffect(face, "attack", maxCards, perCreatureDrain) },
                activeZones: new[] { ZoneType.Battlefield },
                activeWhen: activeWhen);
            card.AddAbility(attackTrigger);
            triggers?.RegisterTriggeredAbility(attackTrigger);
        }

        AttachPair(FrontName, maxCards: 1, perCreatureDrain: false, OnFrontFace);
        AttachPair(BackName, maxCards: 2, perCreatureDrain: true, OnBackFace);

        return card;
    }

    /// <summary>
    /// Resolve the enters/attacks effect: exile up to <paramref name="maxCards"/>
    /// target card(s) from graveyards; drain per CR 119.3. When
    /// <paramref name="perCreatureDrain"/> is false (front — Graveyard
    /// Trespasser), drains exactly ONCE if any creature card was exiled; when
    /// true (back — Graveyard Glutton), drains 1 PER creature card exiled. v1
    /// deterministic target pick — prefers creature cards in any graveyard so
    /// the rider is meaningful.
    /// </summary>
    private static void ResolveExileAndDrain(
        Creature card,
        Player owner,
        IReadOnlyList<Player>? players,
        int maxCards,
        bool perCreatureDrain)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        Player controller = card.Controller ?? owner;

        // Scan every graveyard for exile candidates. "From a graveyard"
        // (CR 404) — any player's graveyard is legal. Prefer creature cards
        // first so the conditional drain fires (matches bot-grade play).
        var scope = players ?? new List<Player> { controller };
        var pool = scope
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .OrderByDescending(c => c.HasType(CardType.Creature) ? 1 : 0)
            .Take(maxCards)
            .ToList();
        if (pool.Count == 0) return; // "up to N" — no card to exile, no-op.

        var creatureCardsExiled = pool.Count(c => c.HasType(CardType.Creature));

        // CR 701.10 — exile the chosen cards from their graveyards.
        foreach (var target in pool)
        {
            Fx.MoveToExile(target);
        }

        if (creatureCardsExiled == 0) return;

        // CR 119.3 — drain. Front: once-if-any; back: once-per-creature-card.
        var drain = perCreatureDrain ? creatureCardsExiled : 1;
        foreach (var opp in scope.Where(p => !ReferenceEquals(p, controller)))
        {
            Fx.LoseLife(opp, drain);
        }
        Fx.GainLife(controller, drain);
    }
}
