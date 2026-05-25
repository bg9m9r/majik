using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcbound Ravager (Darksteel / Modern Horizons 2,
/// {2}).
///
/// Artifact Creature — Beast 0/0. Oracle text:
///   "Sacrifice an artifact: Put a +1/+1 counter on this creature.
///    Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
///
/// ## Implemented (v1)
///
/// - 0/0 Artifact Creature — Beast (multi-type via
///   <see cref="Card.AddCardType"/>), mana cost {2}, owner/controller wired.
/// - <b>Modular 1 (CR 702.43)</b>: wired via the shared
///   <see cref="ModularFactory.Build"/> primitive (promoted out of this
///   factory in the Modular-promotion PR after Arcbound Worker + Arcbound
///   Stinger joined the roadmap). The primitive attaches:
///     - A <see cref="KeywordAbility"/> "Modular 1" marker.
///     - The ETB +1/+1-counter replacement (CR 702.43a / CR 614.1d) routed
///       through <see cref="CountersService.Add"/> so Hardened Scales bumps
///       apply (PR #494).
///     - The Battlefield -> Graveyard death trigger (CR 702.43b) that moves
///       the source's +1/+1 counters to a target artifact creature on the
///       controller's battlefield. Snapshot-counts off the graveyard object
///       (Undying-shape — bag survives the zone move).
/// - <b>Activated ability — sacrifice an artifact: +1/+1 counter</b>:
///   Ravager-specific, stays here. Wired via <see cref="ActivatedAbility"/>
///   with a <see cref="SacrificeAnArtifactCost"/>. The activation is mana-
///   free; only the sacrifice is required. The counter add is routed via
///   <see cref="CountersService.Add"/> so Hardened Scales bumps it too.
///
/// ## Deferred (v1 gaps)
///
/// - Per-target prompt for the Modular bestowal (deterministic first-
///   artifact-creature pick in v1 — same gap as Stoneforge Mystic's tutor).
/// - Cross-battlefield target enumeration (CR 702.43b is not controller-
///   restricted; v1 only scans the controller's battlefield until a
///   <c>Player.Opponents</c>-style enumerator lands).
/// - Artifact picker for the sacrifice cost (deterministic — chooses the
///   first artifact on the controller's battlefield).
/// </summary>
[CardName("Arcbound Ravager")]
public static class ArcboundRavagerFactory
{
    public const string CardName = "Arcbound Ravager";
    public const string PrintedManaCost = "{2}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int ModularValue = 1;

    /// <summary>
    /// Construct Arcbound Ravager with no live wiring. The ETB
    /// +1/+1-counter replacement is NOT registered (no bus supplied) —
    /// the <see cref="MarkEntersWithCounter"/> helper applies the counter
    /// manually instead when the test harness wants the on-battlefield
    /// post-ETB shape. The death trigger is attached to the card shape
    /// but not registered with a TriggerManager. Suitable for dispatcher
    /// / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Arcbound Ravager with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">ReplacementBus to register the Modular
    /// ETB +1/+1-counter replacement against (CR 614.1d). May be null —
    /// callers can stamp the counter manually via
    /// <see cref="MarkEntersWithCounter"/>.</param>
    /// <param name="triggers">TriggerManager for the Modular death trigger
    /// (CR 702.43b). May be null — the trigger is still attached to the
    /// card shape so dispatcher / shape tests can observe it.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType-based lookups + colour identity see
        // both types (mirrors Spellskite / Walking Ballista / Esika's
        // Chariot).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Modular 1 — keyword marker + ETB +1/+1 replacement + death
        // trigger, all from the shared primitive (CR 702.43).
        // ----------------------------------------------------------------
        ModularFactory.Build(
            source: card,
            n: ModularValue,
            effects: null,
            replacements: replacements,
            triggers: triggers);

        // ----------------------------------------------------------------
        // Activated ability — "Sacrifice an artifact: Put a +1/+1 counter
        // on this creature." (no mana cost, just the sacrifice).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: +1/+1 counter for sacrificed artifact",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeAnArtifactCost() },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Manually stamp Arcbound Ravager's Modular-1 ETB +1/+1 counter on
    /// the supplied instance. Used by shape-only tests that put Arcbound
    /// Ravager on the battlefield without funnelling through a
    /// <see cref="Services.ZoneService"/> + <see cref="ReplacementBus"/>
    /// pipeline. Delegates to <see cref="ModularFactory.MarkEntersWithCounters"/>.
    /// </summary>
    public static void MarkEntersWithCounter(Creature ravager)
    {
        if (ravager == null) throw new ArgumentNullException(nameof(ravager));
        ModularFactory.MarkEntersWithCounters(ravager, ModularValue);
    }
}
