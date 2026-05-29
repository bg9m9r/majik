using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Merrow Reejerey (Lorwyn, {2}{U}).
///
/// Creature — Merfolk Soldier 2/2. Oracle text:
///   "Other Merfolk creatures you control get +1/+1.
///    Whenever you cast a Merfolk spell, you may tap or untap target
///    permanent."
///
/// ## Implemented (v1)
/// - 2/2 Merfolk Soldier, mana cost {2}{U}, owner/controller wired.
/// - <b>Anthem lord "Other Merfolk creatures you control get +1/+1"</b>
///   (CR 613.7c, Layer 7c) wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Merfolk</c>, <c>power: 1, toughness: 1</c>,
///   <c>includeSelf: false</c> ("Other"), <c>allPlayers: false</c>
///   ("you control" — controller-scoped, so an opponent's Merfolk are
///   unaffected). Unlike <see cref="LordOfAtlantisFactory"/> /
///   <see cref="MasterOfThePearlTridentFactory"/> this lord grants NO
///   keyword (no Islandwalk), so <c>grantedKeywords</c> is left empty.
///   The registered effect self-gates on Merrow Reejerey being on the
///   battlefield via <see cref="LordStaticEffect.IsActive"/>, so LTB /
///   flicker lifts the bonus naturally (same "no LTB unregister" posture
///   as the other Merfolk lords).
/// - <b>Cast trigger "Whenever you cast a Merfolk spell, you may tap or
///   untap target permanent"</b> (CR 603.1, fires off
///   <see cref="SpellCastEvent"/>). Predicate: the spell's controller is
///   Merrow Reejerey's controller ("you cast") AND the spell's card has
///   the Merfolk subtype (CR 205.3g). The targeted effect mirrors
///   <see cref="PestermiteFactory"/>'s "you may tap or untap target
///   permanent" — a 0..1 <see cref="TargetRequest"/> over any permanent,
///   resolved with a deterministic "useful flip" (untap a tapped target,
///   tap an untapped one), and a clean no-op when the printed "may" is
///   declined or the target left the battlefield (CR 608.2b).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Neither the anthem
///   nor the cast trigger is registered (no services supplied); the
///   trigger is still attached structurally for inspection. Suitable for
///   dispatcher / shape tests. Mirrors <see cref="SramSeniorEdificerFactory"/>.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — anthem
///   wired against the layers service (cast trigger attached but not
///   registered). Mirrors the Merfolk-lord analogues.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — cast
///   trigger registered so <see cref="SpellCastEvent"/>s route through it.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven "may" / tap-or-untap mode prompt</b>: v1 collapses
///   the printed binary choice into a deterministic "useful flip"
///   (untap if currently tapped, else tap) — same posture as
///   <see cref="PestermiteFactory"/>. A real agent prompt for the
///   tap-vs-untap mode + the "may" decline rides alongside the broader
///   modal-trigger queue.
/// - <b>Target legality at choose-time</b>: <c>LegalCandidates</c> is
///   left empty (same posture as Pestermite / Deceiver Exarch — the
///   production agent enumerates the live battlefield itself); the
///   resolve-time recheck enforces battlefield presence (CR 608.2b).
/// </summary>
[CardName("Merrow Reejerey")]
public static class MerrowReejereyFactory
{
    public const string CardName = "Merrow Reejerey";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Merrow Reejerey with no live wiring. The anthem is NOT
    /// registered (no effects service) and the cast trigger is attached
    /// structurally but NOT registered (no trigger manager). Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Merrow Reejerey with the anthem wired against
    /// <paramref name="continuousEffects"/>. The cast trigger is attached
    /// structurally but not registered (no trigger manager supplied).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Merrow Reejerey with the cast trigger registered against
    /// <paramref name="triggers"/>. The anthem is not wired (no effects
    /// service supplied).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
        => Create(owner, continuousEffects: null, eventBus, triggers);

    /// <summary>
    /// Construct a fully-wireable Merrow Reejerey.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 anthem against (CR 613.7c). May be null — no live bonus.</param>
    /// <param name="eventBus">Event bus (unused directly — the trigger
    /// routes via <paramref name="triggers"/>); accepted to mirror the
    /// <see cref="SramSeniorEdificerFactory"/> wiring shape.</param>
    /// <param name="triggers">TriggerManager to register the cast trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape but won't fire off the bus.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus; // accepted for wiring symmetry; trigger routes via TriggerManager.

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 613.7c — anthem. "Other Merfolk creatures you control get
        // +1/+1." allPlayers: false → controller-scoped ("you control");
        // includeSelf: false honours "Other". No keyword granted (unlike
        // Lord of Atlantis / Master of the Pearl Trident).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Merfolk,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // CR 603.1 — cast trigger: "Whenever you cast a Merfolk spell, you
        // may tap or untap target permanent."
        // "You cast" → the spell's controller is this card's controller.
        // Subtype gate: the spell's card carries the Merfolk subtype
        // (CR 205.3g). The tap-or-untap effect mirrors Pestermite's
        // deterministic "useful flip" at resolution (v1 deferral).
        // ----------------------------------------------------------------
        TriggeredAbility? castTrigger = null;

        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.1 — controller match for the printed "you cast".
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            // CR 205.3g — the cast spell must be a Merfolk spell.
            return e.Spell.Card.HasSubtype(CardSubtype.Merfolk);
        });

        var tapOrUntapEffect = new Effect(
            $"{CardName} — you may tap or untap target permanent",
            () =>
            {
                if (castTrigger == null) return;
                var chosen = castTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // printed "may" declined

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution: target must still be
                // on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // Deterministic "useful flip" (v1 deferral, same as
                // Pestermite): untap a tapped target, tap an untapped one.
                if (target.IsTapped) target.Untap();
                else target.Tap();
            });

        castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { tapOrUntapEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
