using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
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
/// Back face (Graveyard Glutton): Creature — Werewolf 4/4, black. "Ward—Discard
/// a card. Whenever this creature enters or attacks, exile up to two target
/// cards from graveyards. For each creature card exiled this way, each
/// opponent loses 1 life and you gain 1 life. Nightbound (…)." The 4/4
/// Werewolf BODY is now swapped in through the CR 711/712 Layer-0 face seed
/// (deferral #19): when the day/night transform flips to the back face,
/// Compute reads the 4/4 black Werewolf characteristics; reverts on flip-back.
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
/// ## Deferred (v1 gaps)
/// - <b>Ward—Discard a card (CR 702.21d non-mana ward).</b> The engine's
///   <see cref="WardEffect"/> models mana-cost ward only; "Ward—Discard a
///   card" (a non-mana ward cost) is not yet supported. Omitted here — the
///   creature is targetable without the discard tax. Same gap surfaces on
///   every "Ward—[non-mana cost]" card.
/// - <b>Back-face BODY swapped in (deferral #19, closed).</b> The CR 711/712
///   Layer-0 face seed swaps the 4/4 black Werewolf P/T + type line in through
///   Compute / combat while on the back face, reverting on flip-back.
/// - <b>Back-face ABILITY TEXT swapped in (deferral #19 residual b, closed).</b>
///   Both faces' triggered-ability sets are attached and gated by an
///   <see cref="TriggeredAbility.ActiveWhen"/> face predicate (CR 711.3): on the
///   FRONT face only the front rider (exile up-to-ONE + drain-if-any-creature)
///   can fire; once the day/night transform flips to the BACK face only the
///   Glutton rider (exile up-to-TWO + drain-per-creature-card) can fire. No
///   register/unregister churn — the dormant face's triggers simply don't match.
/// </summary>
[CardName("Graveyard Trespasser")]
public static class GraveyardTrespasserFactory
{
    public const string FrontName = "Graveyard Trespasser";
    public const string BackName = "Graveyard Glutton";
    public const string FrontCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 3;

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

        // CR 711 / 712 — DFC face tracker carrying the BACK face's printed
        // characteristics. Front = Graveyard Trespasser (daybound), back =
        // Graveyard Glutton (nightbound, Creature — Werewolf 4/4, black).
        // Starts front-face up; the day/night transform flips IsBackFace and
        // Compute then seeds the 4/4 Werewolf body (deferral #19).
        card.MdfcState = new MdfcState(FrontName, BackName, new BackFaceCharacteristics(
            name: BackName,
            types: new[] { CardType.Creature },
            subtypes: new[] { CardSubtype.Werewolf },
            colors: new[] { Majik.Core.ValueObjects.ManaColor.Black },
            power: 4,
            toughness: 4));

        // CR 702.145 — Daybound (front) + Nightbound (back) markers consumed
        // by DayboundNightbound. The Werewolf carries both; the transform
        // logic gates on the current face.
        card.AddAbility(new KeywordAbility(DayboundNightbound.DayboundKeyword, card, owner));
        card.AddAbility(new KeywordAbility(DayboundNightbound.NightboundKeyword, card, owner));

        // CR 603.1 / 508.1f / 711.3 — "Whenever this creature enters or
        // attacks, …" Each face has its own distinct rider, and a DFC's
        // abilities are those of its currently-active face. We attach BOTH
        // face ability sets and gate each with an ActiveWhen face predicate so
        // only the active face's triggers can fire:
        //
        //   FRONT (Graveyard Trespasser, daybound): exile up to ONE card; if a
        //     creature card was exiled, each opponent loses 1 / you gain 1.
        //   BACK  (Graveyard Glutton, nightbound): exile up to TWO cards; for
        //     EACH creature card exiled this way, each opponent loses 1 / you
        //     gain 1.
        //
        // The day/night transform flips MdfcState.IsBackFace; the front set
        // goes dormant and the back set wakes (and vice-versa) automatically —
        // no register/unregister churn (deferral #19 residual b, CR 711.3).
        var mdfc = card.MdfcState!;
        bool OnFront() => !mdfc.IsBackFace;
        bool OnBack() => mdfc.IsBackFace;

        IEffect FrontEffect(string label) =>
            new Effect(
                $"{FrontName}: {label} — exile up to one target card from a graveyard; if a creature card, each opponent loses 1 / you gain 1",
                () => ResolveExileAndDrain(card, owner, players, maxCards: 1, perCreatureDrain: false));

        IEffect BackEffect(string label) =>
            new Effect(
                $"{BackName}: {label} — exile up to two target cards from graveyards; for each creature card, each opponent loses 1 / you gain 1",
                () => ResolveExileAndDrain(card, owner, players, maxCards: 2, perCreatureDrain: true));

        void AddTrigger(ITriggerCondition condition, IEffect effect, Func<bool> activeWhen)
        {
            var trig = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: condition,
                effects: new[] { effect },
                activeZones: new[] { ZoneType.Battlefield },
                activeWhen: activeWhen);
            card.AddAbility(trig);
            triggers?.RegisterTriggeredAbility(trig);
        }

        // Front face (Graveyard Trespasser) — ETB + attack, up-to-one rider.
        AddTrigger(Triggers.OnEnterBattlefieldSelf(card), FrontEffect("ETB"), OnFront);
        AddTrigger(Triggers.OnAttackSelf(card), FrontEffect("attack"), OnFront);

        // Back face (Graveyard Glutton) — ETB + attack, up-to-two + per-creature.
        AddTrigger(Triggers.OnEnterBattlefieldSelf(card), BackEffect("ETB"), OnBack);
        AddTrigger(Triggers.OnAttackSelf(card), BackEffect("attack"), OnBack);

        return card;
    }

    /// <summary>
    /// Resolve the enters/attacks effect for either face (CR 119.3 / 711.3):
    /// exile up to <paramref name="maxCards"/> target cards from graveyards;
    /// then drain. The FRONT face (Graveyard Trespasser) passes
    /// <paramref name="maxCards"/> = 1 and <paramref name="perCreatureDrain"/>
    /// = false (drain 1 if <em>any</em> creature card was exiled). The BACK
    /// face (Graveyard Glutton) passes <paramref name="maxCards"/> = 2 and
    /// <paramref name="perCreatureDrain"/> = true (drain 1 per creature card
    /// exiled). v1 deterministic target pick — prefers creature cards in any
    /// graveyard so the rider is meaningful.
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
            .OrderByDescending(c => c.HasType(CardType.Creature))
            .ToList();
        if (pool.Count == 0) return; // "up to N" — nothing to exile, no-op.

        var targets = pool.Take(maxCards).ToList();
        var creaturesExiled = targets.Count(c => c.HasType(CardType.Creature));

        // CR 701.10 — exile the chosen cards from their graveyards.
        foreach (var target in targets)
        {
            Fx.MoveToExile(target);
        }

        if (creaturesExiled == 0) return;

        // CR 119.3 — drain. Front: 1 if any creature card was exiled. Back:
        // 1 per creature card exiled ("for each creature card exiled this way").
        int drain = perCreatureDrain ? creaturesExiled : 1;
        foreach (var opp in scope.Where(p => !ReferenceEquals(p, controller)))
        {
            Fx.LoseLife(opp, drain);
        }
        Fx.GainLife(controller, drain);
    }
}
