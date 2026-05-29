using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Fortress (March of the Machine
/// "Restless" creature-land cycle, WB member — sibling of Raging Ravine's
/// Worldwake shape).
///
/// Land. Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {W} or {B}.
///    {2}{W}{B}: This land becomes a 1/4 white and black Nightmare creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, defending player loses 2 life and you gain
///    2 life."
///
/// Shares the manland animate shape used by <see cref="RagingRavineFactory"/>:
/// unconditional ETB-tapped (CR 614.1c), two mana abilities (one per colour,
/// CR 605.1), and a {2}{W}{B} animate-until-EOT <see cref="ActivatedAbility"/>
/// whose resolution registers a <see cref="ManlandCycleAnimateEffect"/>
/// (Layer 4 — add Creature + Nightmare subtype) and a
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T 1/4),
/// both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// ## What is unique to Restless Fortress vs Raging Ravine
/// The attack trigger is <b>printed on the land itself</b> (not granted by
/// the animate ability): "Whenever this land attacks, defending player loses
/// 2 life and you gain 2 life." (CR 508.1f). It is therefore attached to the
/// card unconditionally at construction, and its resolution drains the
/// defending player by 2 (CR 119.3) and gains the controller 2 life
/// (CR 119.3). The defending player is captured off the live
/// <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/> using the
/// same closure pattern as <see cref="GoblinGuideFactory"/>.
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>White/black colour identity of the animated form</b> — the engine
///   has no Layer-5 colour-set primitive yet (same gap as Creeping Tar Pit /
///   Raging Ravine). Recorded only in the effect-name string. The "Nightmare"
///   subtype and 1/4 body DO apply via Compute.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 1/4 is recorded for inspection but doesn't surface for combat
///   resolution yet.
/// - <b>Attack-trigger reachability</b> — <see cref="CreatureAttacksEvent.Attacker"/>
///   is typed <see cref="Creature"/>, but a Restless Fortress runtime
///   instance is always a <see cref="Land"/>; production never passes the
///   land as the event's Attacker, so the printed trigger is observationally
///   equivalent to one that exists only while animated (a land can't attack
///   once the animation expires — CR 508.1a). The drain effect is wired and
///   correct; only the production fire-path is the shared cycle gap. The
///   defender capture runs whenever the condition is evaluated so the effect
///   is fully testable.
/// </summary>
[CardName("Restless Fortress")]
public static class RestlessFortressFactory
{
    public const string CardName = "Restless Fortress";
    public const int AnimatedPower = 1;
    public const int AnimatedToughness = 4;
    public const int DrainAmount = 2;

    /// <summary>
    /// Construct Restless Fortress with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The mana abilities + the animate ability + the printed attack trigger
    /// are all attached so the card surface is complete; the layer effects are
    /// not registered, the ETB-tapped replacement is omitted, and the attack
    /// trigger is not registered with any manager (its effect still runs when
    /// driven manually). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Restless Fortress with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the printed attack trigger is
    /// registered with. May be null — the trigger is still attached to the
    /// land's ability list and resolvable when driven manually.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {W}  /  {T}: Add {B}
        // CR 605.1 — mana abilities do not use the stack. Modelled as two
        // distinct mana abilities (same pattern as Raging Ravine).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // ----------------------------------------------------------------
        // {2}{W}{B}: This land becomes a 1/4 white and black Nightmare
        // creature until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} white and black Nightmare creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Nightmare subtype. Printed
                // Land type stays ("it's still a land", CR 613.1c). No
                // printed keywords on the animated body.
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Nightmare },
                    extraTypes: null));

                // Layer 7b — set base P/T 1/4.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{W}{B}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Printed attack trigger (CR 508.1f):
        //   "Whenever this land attacks, defending player loses 2 life and
        //    you gain 2 life."
        // Attached to the land unconditionally (printed, not granted on
        // animate). The defender is captured off the live
        // CreatureAttacksEvent (same closure pattern as GoblinGuideFactory).
        // CR 119.3 — life loss / gain are independent events.
        // ----------------------------------------------------------------
        Player? capturedDefender = null;

        var drainEffect = new Effect(
            $"{CardName}: defending player loses {DrainAmount} life; you gain {DrainAmount} life",
            () =>
            {
                var victim = capturedDefender;
                // CR 119.3 — life loss happens even with no Player defender
                // resolves to a no-op (a planeswalker defender has no life
                // total to lose); the controller's gain is keyed on the
                // "loses 2 ... and you gain 2" being a single triggered
                // effect, but the gain clause applies regardless. Restless
                // Fortress only ever attacks a player (lands attack the
                // defending player or a planeswalker; the drain text reads
                // "defending player"), so a null capture (PW defender) leaves
                // the loss as a no-op while the controller still gains.
                victim?.LoseLife(DrainAmount);

                var controller = land.Controller ?? owner;
                controller.GainLife(DrainAmount);
            });

        var attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    // CR 506.2 — capture the defender for the resolved effect.
                    // Captured whenever the condition is evaluated so the
                    // drain effect can resolve against the right player.
                    capturedDefender = e.DefendingPlayerOrPlaneswalker as Player;
                    // CR 508.1f — fires when THIS land is the attacker. The
                    // event's Attacker is typed Creature and a Restless
                    // Fortress instance is always a Land, so this is the
                    // shared manland-cycle reachability gap (see class
                    // xmldoc) — true only when the land itself is supplied.
                    return ReferenceEquals(e.Attacker, land);
                }),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
