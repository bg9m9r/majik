using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Falkenrath Noble (Innistrad, {3}{B}).
///
/// Creature — Vampire 2/2. Oracle text (Scryfall, verified):
///   "Flying
///    Whenever Falkenrath Noble or another creature dies, target player
///    loses 1 life and you gain 1 life."
///
/// Falkenrath Noble is Blood Artist's flying older sibling — same
/// dies-drain trigger, but the body costs {3}{B} for 2/2 flier instead
/// of {1}{B} for 0/1. Pairs with Blood Artist + Zulaport Cutthroat as
/// the Death-Drain Cycle.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Vampire at {3}{B}, owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/>
///   marker; read by CombatAbilities.HasFlying and the evasion
///   enforcement path.
/// - <b>Death trigger</b> (CR 603.1 + CR 700.4): identical shape to
///   Blood Artist — fires on <see cref="CardMovedEvent"/> with FromZone
///   = Battlefield + ToZone = Graveyard when the moved card has
///   <see cref="CardType.Creature"/>. The printed "Falkenrath Noble or
///   another creature" wording collapses to "a creature" because Noble
///   is itself a creature.
/// - <b>Drain side</b>: on resolution drains 1 from the chosen target
///   player and gains 1 life to the controller. The target is supplied
///   by an optional <paramref name="targetResolver"/> (mirrors Blood
///   Artist — single-arg <c>Create(owner)</c> silently no-ops the drain
///   side; lifegain ALWAYS fires).
///
/// ## Notes
/// - <b>Self-trigger</b>: Noble's own death triggers its ability
///   (CR 603.6c — self-naming dies trigger reads LKI just before
///   leaving the battlefield). v1 keeps activeZones at Battlefield +
///   Graveyard so the self-death case still resolves correctly.
/// - <b>Targeting</b>: "target player" is a single-player target
///   (CR 115.1); resolver lambda supplies the picked player.
/// - <b>Identical body math to Blood Artist's trigger</b>: differs only
///   in cost ({3}{B} vs {1}{B}), stats (2/2 vs 0/1), and Flying.
///   Modulo cost/stats, the trigger is line-for-line Blood Artist.
///
/// ## Deferred (v1 gaps)
/// - <b>Last-known-information for the dying permanent's
///   controller</b>: CR 603.10 — controller must be read from LKI at
///   the moment of death. v1 reads <see cref="Permanent.Controller"/>
///   off the moved card directly. Same posture as Blood Artist /
///   Cruel Celebrant / Meathook.
/// </summary>
[CardName("Falkenrath Noble")]
public static class FalkenrathNobleFactory
{
    public const string CardName = "Falkenrath Noble";
    public const string PrintedManaCost = "{3}{B}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Falkenrath Noble with no live runtime services. The
    /// death-trigger is attached to the card shape but not registered
    /// with a <see cref="TriggerManager"/>, and no target resolver is
    /// wired (so the drain side is a no-op while the lifegain side
    /// still fires). Flying is wired unconditionally. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, targetResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Falkenrath Noble with optional runtime services.
    /// <paramref name="targetResolver"/> supplies the single target
    /// player the death-trigger drains 1 life from on resolution.
    /// <paramref name="triggers"/> registers the triggered ability so
    /// the bus drives it automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<Player?>? targetResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever Falkenrath Noble or another creature dies, target
        //    player loses 1 life and you gain 1 life."
        // Identical shape to Blood Artist (same printed text modulo the
        // self-name).
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            return e.Card.HasType(CardType.Creature);
        });

        var drainEffect = new Effect(
            $"{CardName}: target player loses 1 life + controller gains 1 life",
            () =>
            {
                var target = targetResolver?.Invoke();
                target?.LoseLife(DrainAmount);
                owner.GainLife(GainAmount);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { drainEffect },
            // CR 603.6c — self-naming dies trigger must remain active in
            // the graveyard so Noble's OWN death still resolves the
            // drain/gain.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
