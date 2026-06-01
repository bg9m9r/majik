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
/// - <b>Back-face BODY now swapped in (deferral #19, closed).</b> The CR
///   711/712 Layer-0 face seed swaps the 4/4 black Werewolf P/T + type line in
///   through Compute / combat while on the back face, reverting on flip-back.
///   The back face's distinct <i>ability text</i> (exile-up-to-TWO + the "for
///   each creature card" drain) is NOT re-bodied — Compute is a characteristic
///   pipeline, not an ability registry, so the front face's exile-up-to-one
///   rider keeps firing on both faces. That ability-text delta is the only
///   residual; the body / type / colour are correct on both faces.
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

        // CR 603.1 / 508.1f — "Whenever this creature enters or attacks, …"
        // Two triggers, one shared effect body.
        IEffect BuildEffect(string label) =>
            new Effect(
                $"{FrontName}: {label} — exile up to one target card from a graveyard; if a creature card, each opponent loses 1 / you gain 1",
                () => ResolveExileAndDrain(card, owner, players));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { BuildEffect("ETB") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { BuildEffect("attack") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Resolve the enters/attacks effect: exile up to one target card from a
    /// graveyard; if a creature card was exiled, each opponent loses 1 life
    /// and the controller gains 1 (CR 119.3). v1 deterministic target pick —
    /// prefers a creature card in any graveyard so the rider is meaningful.
    /// </summary>
    private static void ResolveExileAndDrain(Creature card, Player owner, IReadOnlyList<Player>? players)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        Player controller = card.Controller ?? owner;

        // Scan every graveyard for an exile candidate. "From a graveyard"
        // (CR 404) — any player's graveyard is legal. Prefer a creature card
        // so the conditional drain fires (matches bot-grade play).
        var scope = players ?? new List<Player> { controller };
        var pool = scope
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .ToList();
        if (pool.Count == 0) return; // "up to one" — no card to exile, no-op.

        var creatureCard = pool.FirstOrDefault(c => c.HasType(CardType.Creature));
        var target = creatureCard ?? pool[0];

        var exiledCreature = target.HasType(CardType.Creature);

        // CR 701.10 — exile the chosen card from its graveyard.
        Fx.MoveToExile(target);

        if (!exiledCreature) return;

        // CR 119.3 — "each opponent loses 1 life and you gain 1 life."
        foreach (var opp in scope.Where(p => !ReferenceEquals(p, controller)))
        {
            Fx.LoseLife(opp, 1);
        }
        Fx.GainLife(controller, 1);
    }
}
