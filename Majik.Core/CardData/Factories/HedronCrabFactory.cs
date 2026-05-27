using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hedron Crab (Zendikar, {U}).
///
/// Creature — Homarid 0/2. Oracle text:
///   "Landfall — Whenever a land enters under your control, target player
///    puts the top three cards of their library into their graveyard."
///
/// One of Modern Mill's pillar one-drops — every fetch / shockland trigger
/// nets a free mill 3. Same landfall trigger predicate as
/// <see cref="TirelessProvisionerFactory"/> /
/// <see cref="TirelessTrackerFactory"/>; the resolve body mills via
/// <see cref="MillAction.Apply"/> (CR 701.13) with a 1..1 target-player
/// TargetRequest in the Bojuka-Bog shape.
///
/// ## Implemented (v1)
/// - 0/2 Creature — Homarid, mana cost {U}, owner / controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 614 / CR 702.142)
///   — fires on <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "land entering the battlefield under controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate.
///   Carries one 1..1 "target player" <see cref="TargetRequest"/> (same
///   shape as Bojuka Bog's ETB).
/// - <b>Resolve — mill 3 to chosen target player</b>: snapshots
///   <c>ChosenTargets[0][0]</c>; when it is a <see cref="Player"/>, mills
///   <see cref="MillCount"/> via <see cref="MillAction.Apply"/>. When no
///   target was set (shape-only / dispatcher path) the effect falls back
///   to milling the controller (deterministic — mirrors Bojuka Bog's
///   no-target fallback). Library shorter than 3 mills all remaining
///   cards without losing the game (CR 701.13a).
///
/// ## Deferred (v1 gaps)
/// - <b>Target player agent prompt</b>: v1 reads
///   <c>ChosenTargets[0][0]</c>; no dedicated prompt UX. Mirrors
///   Bojuka Bog / Tormod's Crypt.
/// - <b>Trigger registration</b>: shape-only <see cref="Create(Player)"/>
///   path attaches the trigger to the card for shape inspection but
///   doesn't register it with a bus. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing.
/// </summary>
[CardName("Hedron Crab")]
public static class HedronCrabFactory
{
    public const string CardName = "Hedron Crab";
    public const string PrintedManaCost = "{U}";
    public const int Power = 0;
    public const int Toughness = 2;
    public const int MillCount = 3;

    /// <summary>
    /// Construct Hedron Crab with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but
    /// not registered with a bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Hedron Crab. When <paramref name="triggers"/> is supplied
    /// the landfall mill-3 trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Homarid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall trigger — CR 603.1 / 603.6a / CR 614 / CR 702.142.
        //   "Whenever a land enters under your control, target player
        //    puts the top three cards of their library into their
        //    graveyard."
        // Predicate is shared with TirelessProvisioner / TirelessTracker.
        // Single 1..1 "target player" TargetRequest mirrors Bojuka Bog's
        // ETB shape.
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var millEffect = new Effect(
            $"{CardName}: target player mills {MillCount} cards (landfall)",
            () =>
            {
                if (trigger == null) return;

                // Resolve target player from ChosenTargets; fall back to
                // the controller (v1 deterministic path — mirrors
                // Bojuka Bog / Tormod's Crypt no-target fallback).
                Player targetPlayer;
                if (trigger.ChosenTargets.Count > 0
                    && trigger.ChosenTargets[0].Count > 0
                    && trigger.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = card.Controller ?? owner;
                }

                MillAction.Apply(targetPlayer, MillCount);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { millEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
