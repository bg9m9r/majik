using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Generous Visitor (Theros Beyond Death — {G}).
///
/// Creature — Spirit 1/1. Oracle text:
///   "Whenever you cast an enchantment spell, put a +1/+1 counter on
///    target creature."
///
/// ## Implemented (v1)
/// - {G} 1/1 Creature — Spirit with owner/controller wiring (mirrors
///   the simple vanilla-shape factories — <see cref="MemniteFactory"/>
///   / <see cref="ArcboundWorkerFactory"/>).
/// - <b>Enchantment-cast triggered ability (CR 603.6 / 603.2c)</b>:
///   "Whenever you cast an enchantment spell, put a +1/+1 counter on
///   target creature." Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/>, gated on:
///     * <c>ReferenceEquals(e.Spell.Controller, owner)</c> — "you cast".
///     * <c>e.Spell.Card.HasType(CardType.Enchantment)</c> — covers
///       plain enchantments AND Auras (Auras carry the Enchantment card
///       type plus the Aura subtype per CR 303.1 — mirrors
///       <see cref="SythisHarvestsHandFactory"/>'s constellation gate).
/// - A 1..1 "target creature" <see cref="TargetRequest"/>. Resolution
///   reads <see cref="TriggeredAbility.ChosenTargets"/>, rechecks
///   legality (CR 608.2b — target must still be a Creature on the
///   battlefield), and places one
///   <see cref="CounterType.PlusOnePlusOne"/> counter via
///   <see cref="CounterCollection.Add"/> (mirrors Heliod, Sun-Crowned's
///   lifegain-trigger counter placement).
///
/// ## Notes
/// - Generous Visitor is a Creature, not an Enchantment — casting
///   Generous Visitor itself does NOT trigger this ability (the
///   type-gate filters on enchantment-cast). The single-arg dispatcher
///   path attaches the trigger to the card shape without
///   <see cref="TriggerManager"/> wiring; tests can fire the effect
///   directly by setting chosen targets + invoking <c>Execute</c>. The
///   <c>(owner, triggers)</c> overload registers with a live
///   <see cref="TriggerManager"/> so the bus surfaces the trigger as
///   pending.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time target filtering</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty by default (same posture as Heliod, Sun-Crowned /
///   Earthshaker Khenra). Choose-time filtering depends on the live
///   battlefield gather plumbing.
/// - <b>Agent-driven target prompt</b>: the trigger honours pre-set
///   <see cref="TriggeredAbility.ChosenTargets"/>; the factory does NOT
///   wire an <see cref="IPlayerAgent"/> prompt. Tests set chosen
///   targets via <see cref="TriggeredAbility.SetChosenTargets"/>
///   directly.
/// </summary>
[CardName("Generous Visitor")]
public static class GenerousVisitorFactory
{
    public const string CardName = "Generous Visitor";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Generous Visitor without live <see cref="TriggerManager"/>
    /// wiring. The enchantment-cast trigger is attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests
    /// observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Generous Visitor with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the
    /// enchantment-cast trigger is registered so the bus surfaces it as
    /// pending whenever the controller casts an enchantment.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enchantment-cast triggered ability — CR 603.6 / 603.2c.
        //   "Whenever you cast an enchantment spell, put a +1/+1 counter
        //    on target creature."
        //
        // Gate: SpellCastEvent where the cast spell's controller is
        // Generous Visitor's controller AND the spell's card has
        // CardType.Enchantment (covers Auras per CR 303.1 — same
        // posture as Sythis's constellation gate).
        // ----------------------------------------------------------------
        TriggeredAbility? buffTrigger = null;
        var buffEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on target creature",
            () =>
            {
                if (buffTrigger == null) return;
                if (buffTrigger.ChosenTargets.Count == 0) return;
                if (buffTrigger.ChosenTargets[0].Count == 0) return;

                var raw = buffTrigger.ChosenTargets[0][0];
                if (raw is not Permanent target) return;

                // CR 608.2b — resolve-time legality recheck. The chosen
                // target must still be a Creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && e.Spell.Card.HasType(CardType.Enchantment));

        buffTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { buffEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(buffTrigger);
        triggers?.RegisterTriggeredAbility(buffTrigger);

        return card;
    }
}
