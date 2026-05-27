using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kambal, Consul of Allocation (Kaladesh,
/// {1}{W}{B}).
///
/// Legendary Creature — Human Advisor 2/3. Oracle text:
///   "Whenever an opponent casts a noncreature spell, that player loses
///    2 life and you gain 2 life."
///
/// ## Implemented (v1)
///
/// - 2/3 Legendary Creature — Human Advisor, mana cost {1}{W}{B}.
/// - <b>Opponent-noncreature-cast trigger (CR 603.1)</b>:
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="SpellCastEvent"/>:
///     * The spell's controller is not Kambal's controller (CR 109.5 —
///       "opponent" reads against the trigger's controller).
///     * The spell's card does NOT have <see cref="CardType.Creature"/>
///       (CR 202.3 — type line check; v1 reads the printed type set,
///       which matches the Eidolon of the Great Revel / Monastery
///       Mentor noncreature-spell trigger family).
///   Pending-caster is boxed in a single-element array so the resolve
///   body can re-read it (same shape as
///   <see cref="EidolonOfTheGreatRevelFactory"/>).
///
/// - <b>Resolution</b>: that opponent loses 2 life via
///   <see cref="Player.LoseLife"/> (CR 119.3 — increments
///   <see cref="Player.LifeLostThisTurn"/> so spectacle / revolt /
///   lifegain observers see the loss). Kambal's controller gains 2 life
///   via <see cref="Player.GainLife"/> (CR 119 — direct gain). The two
///   halves are independent; printed text uses "and", so neither half
///   is conditional on the other resolving (mirrors symmetric drain-
///   gain wording on Bontu's Last Reckoning / Tendrils of Agony).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Spell-type check for split / DFC / Adventure casts</b>: v1 reads
///   <see cref="ICard.HasType"/> on the spell's source card. For
///   Adventure cards cast as their adventure half (Instant / Sorcery)
///   the printed type set on the underlying card still includes
///   Creature; v1 would treat such a cast as a creature spell and miss
///   Kambal's trigger. Same shape gap noted on
///   <see cref="EidolonOfTheGreatRevelFactory"/>.
/// - <b>Resolver-side stack queuing</b>: the trigger registers with the
///   supplied <see cref="TriggerManager"/> so the bus drives it; the
///   resolve body runs the life-change directly (no separate stack
///   object beyond the trigger itself). Same wiring posture as
///   Eidolon — fine for v1 because the life change is a single atomic
///   effect with no targets.
/// </summary>
[CardName("Kambal, Consul of Allocation")]
public static class KambalConsulOfAllocationFactory
{
    public const string CardName = "Kambal, Consul of Allocation";
    public const string PrintedManaCost = "{1}{W}{B}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Kambal with no live <see cref="TriggerManager"/> wiring.
    /// The trigger is attached to the card shape so dispatcher tests see
    /// it; pass the (owner, triggers) overload to register it for live
    /// <see cref="SpellCastEvent"/> dispatch.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Kambal with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the trigger
    /// is registered so any qualifying <see cref="SpellCastEvent"/>
    /// (opponent's noncreature spell) automatically queues the drain-
    /// gain effect.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Advisor });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever an opponent casts a noncreature spell, that player
        //  loses 2 life and you gain 2 life."
        // CR 603.1 — triggered ability over SpellCastEvent. Predicate
        // gates on (a) caster != Kambal's controller, (b) spell card is
        // not a Creature. The opponent's identity is captured in a
        // single-element array so the resolve body can route the
        // life-loss to the correct player (Eidolon-style closure).
        // ----------------------------------------------------------------
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null) return false;

            // CR 109.5 — "an opponent" reads against the trigger's
            // controller. Kambal's own casts do not fire the trigger.
            if (ReferenceEquals(caster, owner)) return false;

            // CR 202.3 — noncreature spell. Reads the printed type set
            // on the spell's source card (same posture as Eidolon /
            // Monastery Mentor noncreature triggers).
            if (e.Spell.Card.HasType(CardType.Creature)) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var drainEffect = new Effect(
            $"{CardName}: opponent loses 2, you gain 2",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;

                // CR 119.3 — life loss. Routes through Player.LoseLife
                // so LifeLostThisTurn ticks (spectacle / revolt observe
                // the loss). Symmetric gain via Player.GainLife.
                caster.LoseLife(2);
                owner.GainLife(2);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
