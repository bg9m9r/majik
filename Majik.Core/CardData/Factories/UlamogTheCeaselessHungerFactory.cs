using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ulamog, the Ceaseless Hunger (Battle for
/// Zendikar, {10}).
///
/// Legendary Creature — Eldrazi 10/10. Oracle text (Scryfall, verified):
///   "When you cast this spell, exile two target permanents.
///    Indestructible
///    Whenever Ulamog attacks, defending player exiles the top twenty
///    cards of their library."
///
/// ## Implemented (v1)
/// - 10/10 Legendary Creature — Eldrazi at {10}.
/// - <b>Indestructible (CR 702.12)</b>: <see cref="KeywordAbility"/>("Indestructible")
///   marker — SBA reads via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///   (same wiring shape as The One Ring / Avacyn / Heliod's Pilgrim).
/// - <b>Cast trigger (CR 603.6a / CR 603.10)</b>: triggered ability over
///   <see cref="SpellCastEvent"/> filtered to <c>e.Spell.Card == card</c>
///   so the ability lands on the stack when Ulamog is cast (mirrors
///   <see cref="CrashingFootfallsFactory"/>'s Cascade self-cast detection
///   — the trigger is registered with <see cref="ZoneType.Stack"/> in its
///   active zones because Ulamog is on the stack at cast time). One 2..2
///   "target permanent" <see cref="TargetRequest"/>; on resolution each
///   chosen permanent is exiled (Zone → Exile via
///   <see cref="ZoneService.MoveCard"/> when supplied, or raw zone
///   manipulation otherwise). CR 701.21 — exile is NOT a destroy effect,
///   so indestructible permanents are exiled normally (CR 702.12b);
///   <see cref="ZoneMoveReason.Other"/> is the implicit reason when callers
///   use the raw zone path.
/// - <b>Attack trigger (CR 508.1f / CR 603.1)</b>: triggered ability over
///   <see cref="CreatureAttacksEvent"/> filtered to
///   <see cref="Triggers.OnAttackSelf"/>(card). On resolution the
///   <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/> is
///   captured; the top 20 cards of that player's library are moved to
///   exile one-by-one (Library → Exile via
///   <see cref="ZoneService.MoveCard"/> when supplied; raw zone
///   manipulation otherwise). Empty-library halts the loop cleanly —
///   running out of library is a CR 704.5b state-based loss the
///   subsequent SBA pass handles, not this effect.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Cast + attack triggers
///   attached; not registered with any <see cref="TriggerManager"/>; raw
///   zone manipulation used for exiles. Suitable for dispatcher /
///   structural tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. Both triggers register with <paramref name="triggers"/>;
///   exiles route through <paramref name="zones"/> so
///   <see cref="CardMovedEvent"/> publishes for any zone-change
///   subscribers (Containment Priest, Tormod's Crypt, etc.).
/// </summary>
[CardName("Ulamog, the Ceaseless Hunger")]
public static class UlamogTheCeaselessHungerFactory
{
    public const string CardName = "Ulamog, the Ceaseless Hunger";
    public const string PrintedManaCost = "{10}";
    public const int Power = 10;
    public const int Toughness = 10;
    public const int CastTriggerTargetCount = 2;
    public const int AttackTriggerExileCount = 20;

    /// <summary>
    /// Construct Ulamog with no live wiring. All abilities are attached
    /// for shape observability; triggers aren't registered with any
    /// <see cref="TriggerManager"/>; exile moves use raw zone manipulation.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Ulamog with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, all exile moves route through
    /// <see cref="ZoneService.MoveCard"/> so <see cref="CardMovedEvent"/>
    /// publishes for any zone-change subscribers.</param>
    /// <param name="triggers">When supplied, both the cast trigger and
    /// the attack trigger register with the bus so their respective
    /// events land them on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.12 — Indestructible. Marker only — SBA reads
        // KeywordAbility via CombatAbilities.HasIndestructible (same
        // wiring shape as The One Ring / Avacyn).
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, exile two target permanents."
        // Fires while Ulamog is on the stack (SpellCastEvent is published
        // as the spell moves to the stack), so activeZones = Stack —
        // matches CrashingFootfalls / Living End's Cascade self-cast
        // wiring posture.
        // ----------------------------------------------------------------
        TriggeredAbility? castTrigger = null;
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var castEffect = new Effect(
            $"{CardName}: exile two target permanents (cast trigger)",
            () =>
            {
                if (castTrigger == null) return;
                var chosen = castTrigger.ChosenTargets;
                if (chosen.Count == 0) return;

                foreach (var raw in chosen[0])
                {
                    if (raw is not Card permCard) continue;
                    // CR 608.2b — illegal-on-resolution check: target must
                    // still be on the battlefield.
                    if (permCard.Zone != ZoneType.Battlefield) continue;

                    // CR 701.21 — exile is NOT a destroy effect (CR 702.12b
                    // — indestructible doesn't gate exile). Route through
                    // ZoneService when supplied so CardMovedEvent fires.
                    if (zones != null)
                    {
                        zones.MoveCard(permCard, ZoneType.Battlefield, ZoneType.Exile);
                    }
                    else
                    {
                        var permController = permCard.Controller ?? permCard.Owner;
                        permController?.Zones.Battlefield.RemoveCard(permCard);
                        var exileOwner = permCard.Owner ?? owner;
                        exileOwner.Zones.Exile.AddCard(permCard);
                        permCard.SetZone(ZoneType.Exile);
                    }
                }
            });

        castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            interveningIf: null,
            // Cast trigger fires while the spell is on the stack — same
            // active-zone posture as Cascade (CrashingFootfalls / Living
            // End).
            activeZones: new[] { ZoneType.Stack },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "two target permanents",
                    MinTargets: CastTriggerTargetCount,
                    MaxTargets: CastTriggerTargetCount,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.1.
        //   "Whenever Ulamog attacks, defending player exiles the top
        //    twenty cards of their library."
        // Single attack-self trigger; defending-player is captured off
        // the live CreatureAttacksEvent (matches RagavanNimblePilferer's
        // captured-victim closure pattern).
        // ----------------------------------------------------------------
        Player? capturedDefender = null;

        var attackEffect = new Effect(
            $"{CardName}: defending player exiles top {AttackTriggerExileCount} cards of their library",
            () =>
            {
                var victim = capturedDefender;
                if (victim == null) return;

                for (var i = 0; i < AttackTriggerExileCount; i++)
                {
                    var top = victim.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) break; // CR 704.5b — empty library is
                                            // a state-based loss the next
                                            // SBA pass handles.

                    if (zones != null)
                    {
                        zones.MoveCard(top, ZoneType.Library, ZoneType.Exile);
                    }
                    else
                    {
                        victim.Zones.Library.RemoveCard(top);
                        victim.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);
                    }
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    if (!ReferenceEquals(e.Attacker, card)) return false;
                    // Capture defender — CR 506.2 — for the resolved effect.
                    // Player or Planeswalker; only Player here triggers the
                    // exile (a planeswalker defender doesn't have a library
                    // to exile from, so no-op).
                    capturedDefender = e.DefendingPlayerOrPlaneswalker as Player;
                    return true;
                }),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
