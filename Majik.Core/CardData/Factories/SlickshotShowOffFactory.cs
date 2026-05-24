using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slickshot Show-Off (Outlaws of Thunder Junction,
/// {1}{R}).
///
/// Creature — Human Mercenary Jock 1/1. Oracle text:
///   "Flying.
///    Haste.
///    Whenever you cast a noncreature spell, this creature gets +3/+0 until
///    end of turn.
///    Plot {R} (You may pay {R} and exile this card from your hand. Cast it
///    as a sorcery on a later turn for its plot cost without paying its
///    mana cost.)"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Mercenary Jock, mana cost {1}{R}, owner/controller
///   wired.
/// - Flying (CR 702.9) + Haste (CR 702.10) keyword markers via
///   <see cref="KeywordAbility"/> — same wiring shape as Arclight Phoenix /
///   Phoenix of Ash's Flying+Haste pair.
/// - Cast-noncreature pump triggered ability (CR 603.1 / CR 122.1 / CR 514.2)
///   over <see cref="SpellCastEvent"/> filtered to the controller + non-Creature
///   spell (same predicate as <see cref="Keywords.ProwessFactory"/>). On
///   resolve registers a raw <see cref="PumpUntilEndOfTurnEffect"/>(+3, 0) on
///   Slickshot's own <see cref="Creature.ActiveEffects"/> when one is wired —
///   delivers the printed "+3/+0 until end of turn" (Layer 7c +P/+T,
///   end-of-turn-expirable per CR 514.2). The +0 toughness portion is
///   deliberate; this is NOT prowess (+1/+1) — the printed body is +3/+0.
///   Self-cast does NOT contribute: the SpellCastEvent for Slickshot itself
///   fires while Slickshot is still a spell on the stack and is a Creature
///   spell, so the noncreature predicate filters it out (CR 603.2c —
///   triggers only fire when the condition is met; CR 110.4 — once on the
///   stack the spell carries its card types). Multiple noncreature casts in
///   a single turn stack additively: each cast registers a fresh
///   PumpUntilEndOfTurnEffect, all three's deltas apply at Layer 7c (CR 613
///   — multiple Layer 7c effects all apply to the same characteristic).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Cast-pump trigger is attached
///   to the card for shape observability; without a <see cref="TriggerManager"/>
///   the bus won't pick it up, and without an <see cref="Effects.ContinuousEffectsService"/>
///   wired into <see cref="Creature.ActiveEffects"/> the pump body silently
///   no-ops on execute. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, Events.IEventBus?, TriggerManager?, Effects.ContinuousEffectsService?)"/>
///   — fully wired. Pump trigger is registered with <paramref name="triggers"/>;
///   <paramref name="effects"/> is bound onto the card's
///   <see cref="Creature.ActiveEffects"/> so live P/T reads flow through the
///   layers compute (CR 613 — Layer 7c applies <see cref="PumpUntilEndOfTurnEffect"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Plot (CR 718)</b>: the printed "Plot {R}" rider is NOT yet wired. Plot is
///   a brand-new OTJ mechanic — same family shape as Suspend (cast-from-exile
///   alternative on a later turn) but with sorcery-speed semantics: the
///   {R} plot cost is paid from the hand to exile this card with a "plotted"
///   marker; on any subsequent turn during a main phase with an empty stack,
///   the controller may cast it from exile for {0} mana cost (CR 718.2).
///   Wiring needs (a) an activated-from-hand "pay {R}, exile with plot
///   marker" ability — currently no activated-from-hand-with-alt-cost
///   primitive exists in the engine (closest sibling is
///   <see cref="Costs.CastFromExileAlternativeCost"/> + Cascade-style
///   host-callback grant, used by Crashing Footfalls / Living End), (b) a
///   "plotted card may be cast on a later turn at sorcery speed for {0}"
///   permission slot — adjacent to <see cref="Costs.CastFromExileAlternativeCost"/>
///   but additionally gated by "turn-cast-on > turn-plotted" + sorcery
///   speed (CR 718.2 / CR 117.1a), and (c) a once-per-turn-per-plotted-card
///   cast cap (CR 718.2c). Same posture as <see cref="BurstLightningFactory"/>'s
///   deferred Kicker rider — ship the printed shape + the most common
///   triggered/static body, defer the alt-cost mechanic until its
///   primitive lands. Bot evaluation will treat Slickshot as a vanilla
///   1/1 Flying+Haste body with prowess-style pump rider until Plot
///   ships.
/// </summary>
public static class SlickshotShowOffFactory
{
    public const string CardName = "Slickshot Show-Off";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int PumpPower = 3;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Slickshot Show-Off with no live wiring. The cast-pump
    /// trigger is attached to the card for shape observability; the pump
    /// body silently no-ops without a <see cref="ContinuousEffectsService"/>
    /// on <see cref="Creature.ActiveEffects"/>, and the trigger isn't
    /// registered with any <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Slickshot Show-Off with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly today; reserved for future
    /// lifecycle subscribers (LTB unregister, Plot exile-on-cast hooks).</param>
    /// <param name="triggers">TriggerManager for the cast-noncreature pump
    /// trigger. May be null — the trigger is still attached to the card
    /// shape so <see cref="ICard.Abilities"/> includes it.</param>
    /// <param name="effects">ContinuousEffectsService for the +3/+0 EOT pump
    /// (CR 613 Layer 7c, CR 514.2 EOT cleanup). Bound onto the card's
    /// <see cref="Creature.ActiveEffects"/> so live P/T reads flow through
    /// the layers compute. May be null — the pump body silently no-ops on
    /// execute.</param>
    public static Creature Create(
        Player owner,
        Events.IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Mercenary, CardSubtype.Jock });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. CR 702.10 — Haste. Both shipped as keyword
        // markers consumed by CombatValidator / CombatAbilities (same wire-up
        // shape as Arclight Phoenix / Phoenix of Ash).
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // Bind the effects service onto the card so live P/T reads through
        // ActiveEffects flow through layers compute. Mirrors MonasteryMentor's
        // pattern. Done before the trigger is built so the closure has a
        // stable reference (the closure also reads card.ActiveEffects so a
        // late-bound service still works; this is just a fast-path).
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 603.1 — "Whenever you cast a noncreature spell, this creature
        // gets +3/+0 until end of turn." Predicate matches ProwessFactory's
        // (controller + non-Creature) but the effect is raw +3/+0 instead of
        // the keyword's +1/+1. Slickshot's own cast does NOT trigger this:
        // the SpellCastEvent for Slickshot fires while Slickshot is on the
        // stack as a Creature spell (CR 110.4), failing the noncreature
        // predicate.
        var pumpCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var pumpEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} until end of turn (whenever you cast a noncreature spell)",
            () =>
            {
                // CR 514.2 — EOT cleanup is automatic via the layers service's
                // ExpiresAtEndOfTurn flag on PumpUntilEndOfTurnEffect. Without
                // a live effects service the pump silently no-ops; the
                // factory's two-arg overload binds one onto card.ActiveEffects.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));
            });

        var pumpTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: pumpCondition,
            effects: new IEffect[] { pumpEffect });

        card.AddAbility(pumpTrigger);
        triggers?.RegisterTriggeredAbility(pumpTrigger);

        return card;
    }
}
