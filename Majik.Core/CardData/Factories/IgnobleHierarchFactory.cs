using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ignoble Hierarch (Modern Horizons 3).
///
/// Creature — Goblin Shaman {G} 0/1.
/// Oracle text:
///   "Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    {T}: Add {B}, {R}, or {G}."
///
/// Mono-G black/red/green sibling of <see cref="NobleHierarchFactory"/> —
/// identical shape, swapped mana colours (B/R/G instead of W/U/G) and
/// subtypes (Goblin Shaman instead of Human Druid). See
/// <see cref="NobleHierarchFactory"/> for the in-depth Exalted wiring notes;
/// this factory follows the same trigger + closure pattern.
///
/// ## Implemented (v1)
/// - 0/1 Creature — Goblin Shaman, mana cost {G}.
/// - <b>Three mana abilities (CR 605.1)</b>: {T}: Add {B}, {T}: Add {R},
///   {T}: Add {G}. Each is a <see cref="ManaAbility"/> with a
///   <c>canActivateCheck = !IsTapped</c> gate.
/// - <b>Exalted keyword marker</b> (CR 702.90) wired as a
///   <see cref="KeywordAbility"/>.
/// - <b>Exalted trigger (CR 702.90b)</b>: fires on every
///   <see cref="CreatureAttacksEvent"/> while Ignoble Hierarch is on the
///   battlefield. Solo-attacker check + +1/+1 EOT pump are read from the
///   injected <c>attackingCreaturesSource</c> closure (same source-closure
///   pattern as Noble Hierarch / Goblin Piledriver).
/// </summary>
public static class IgnobleHierarchFactory
{
    public const string CardName = "Ignoble Hierarch";
    public const string PrintedManaCost = "{G}";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Ignoble Hierarch with no live TriggerManager wiring and no
    /// attackers-source. Suitable for card-shape / dispatcher tests — the
    /// exalted trigger is attached to the card shape but the pump body is a
    /// no-op. All three mana abilities are always wired.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Ignoble Hierarch with optional runtime services.
    /// <paramref name="triggers"/> registers the exalted trigger with a live
    /// manager. <paramref name="attackingCreaturesSource"/> supplies the live
    /// attacker snapshot at trigger-resolution time so the "attacks alone"
    /// check can be made.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the exalted trigger
    /// against. May be null — trigger is still attached to the card shape so
    /// <see cref="ICard.Abilities"/> includes it.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list. Called at trigger resolution. May be null —
    /// pump body is a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Exalted keyword marker so data-side tools see it.
        card.AddAbility(new KeywordAbility("Exalted", card, owner));

        // CR 605.1 — Three mana abilities (no stack). Each taps Ignoble
        // Hierarch via the Permanent.Tap() path inside ManaAbility.Activate();
        // the canActivateCheck gates on !IsTapped so duplicate activations
        // are prevented. Mirrors Noble Hierarch's multi-colour pattern.

        // {T}: Add {B}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{B}"),
            canActivateCheck: () => !card.IsTapped));

        // {T}: Add {R}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{R}"),
            canActivateCheck: () => !card.IsTapped));

        // {T}: Add {G}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !card.IsTapped));

        // CR 702.90b — Exalted. "Whenever a creature you control attacks
        // alone, that creature gets +1/+1 until end of turn."
        var exaltedEffect = new Effect(
            "Ignoble Hierarch Exalted: +1/+1 EOT when a creature attacks alone",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                // Count only creatures controlled by Ignoble Hierarch's
                // current controller (CR 702.90b — "a creature you control
                // attacks alone" means no other controlled creatures are
                // attacking).
                var controlledAttackers = new List<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, card.Controller)) continue;
                    controlledAttackers.Add(atk);
                }

                // "attacks alone" — exactly 1 controlled attacker.
                if (controlledAttackers.Count != 1) return;

                var soloAttacker = controlledAttackers[0];
                if (soloAttacker.ActiveEffects == null) return;

                soloAttacker.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(soloAttacker, 1, 1));
            });

        var exaltedTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, card.Controller)),
            effects: new IEffect[] { exaltedEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(exaltedTrigger);
        triggers?.RegisterTriggeredAbility(exaltedTrigger);

        return card;
    }
}
