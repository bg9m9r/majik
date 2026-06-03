using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn, Legacy Reforged (Dominaria United, {5}).
///
/// Legendary Artifact Creature — Golem. Oracle text (verified against
/// Scryfall):
///   "Karn's power and toughness are each equal to the greatest mana value
///    among artifacts you control.
///    At the beginning of your upkeep, add {C} for each artifact you
///    control. This mana can't be spent to cast nonartifact spells. Until
///    end of turn, you don't lose this mana as steps and phases end."
///
/// ## Implemented (v1)
/// - {5} Legendary Artifact Creature — Golem. Both card types stamped
///   (CR 301.1 — Artifact Creature) and the Legendary supertype seeded.
/// - <b>CDA P/T</b> (CR 604.3 / 613.2 Layer 7a) — "power and toughness are
///   each equal to the greatest mana value among artifacts you control."
///   A <see cref="CdaPowerToughnessEffect"/> sets the base P/T to
///   <see cref="GreatestArtifactManaValue"/> over the controller's
///   battlefield artifacts (Karn itself counts — it is an artifact, mana
///   value 5, so the floor is 5 while Karn is in play). Re-evaluated every
///   Compute, so casting a bigger artifact grows Karn live; an empty board
///   (Karn off the battlefield) yields 0. Mirrors the Mortivore / Tarmogoyf
///   CDA wiring.
/// - <b>Upkeep mana trigger</b> (CR 603.1) — "At the beginning of your
///   upkeep, add {C} for each artifact you control." On resolution the
///   controller's battlefield artifacts are counted and that many colorless
///   ({C}) mana are added to the pool, carrying TWO riders:
///     1. <b>Spend-restriction</b> (CR 106.4) — "This mana can't be spent to
///        cast nonartifact spells." Modelled as a
///        <see cref="SpendRestriction"/> whose predicate admits only
///        artifact spells; the
///        <see cref="Majik.Core.Costs.ManaPaymentResolver"/> gate withholds
///        these colorless units from any payment for a nonartifact spell
///        (rejecting it atomically) and lets them pay an artifact spell.
///     2. <b>Doesn't-empty</b> (CR 500.4 exception) — "Until end of turn, you
///        don't lose this mana as steps and phases end." Each unit's
///        <see cref="ManaProvenanceSlot.DoesNotEmpty"/> flag keeps it
///        floating across step/phase-boundary empties; it lapses on the
///        end-of-turn empty (CR 514.2).
///
/// ## Notes
/// - The single-arg <see cref="Create(Player)"/> overload attaches the
///   trigger + CDA shape for structural / dispatcher tests but does not
///   register live trigger / continuous-effect wiring. The full overload
///   registers the upkeep trigger with a <see cref="TriggerManager"/> and
///   the CDA with a <see cref="ContinuousEffectsService"/>, and binds the
///   artifact-count closure to the controller's battlefield.
/// </summary>
[CardName("Karn, Legacy Reforged")]
public static class KarnLegacyReforgedFactory
{
    private const string CardName = "Karn, Legacy Reforged";
    private const string Cost = "{5}";

    // CR 106.4 — "This mana can't be spent to cast nonartifact spells."
    // Shared static restriction (delegate-by-reference equality) so every
    // produced {C} unit carries the same restriction instance. Admits only
    // artifact spells; everything else (including a null/no-spell context
    // such as an ability-activation cost) is rejected — SatisfiedBy treats a
    // null spell as "no permission".
    private static readonly SpendRestriction ArtifactSpellsOnly =
        new("artifact spell", spell => spell.Card.HasType(CardType.Artifact));

    /// <summary>
    /// Greatest mana value among the artifacts <paramref name="controller"/>
    /// controls on the battlefield (CR 202.3 — mana value of the printed
    /// mana cost). Returns 0 when the controller controls no artifacts. Pure
    /// helper exposed for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int GreatestArtifactManaValue(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var greatest = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (!card.HasType(CardType.Artifact)) continue;
            // CR 202.3 — mana value of the printed mana cost. ICard exposes
            // the cost as a string; parse it (lands / {0} → 0).
            var mv = string.IsNullOrWhiteSpace(card.ManaCost)
                ? 0
                : ManaCost.Parse(card.ManaCost).TotalValue;
            if (mv > greatest) greatest = mv;
        }
        return greatest;
    }

    /// <summary>
    /// Count of artifacts <paramref name="controller"/> controls on the
    /// battlefield (CR 109.2). Pure helper exposed for tests; mirrors the
    /// closure the upkeep trigger reads at resolution.
    /// </summary>
    public static int ArtifactsControlled(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var count = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card.HasType(CardType.Artifact)) count++;
        }
        return count;
    }

    /// <summary>
    /// Shape-only construct (no live trigger / CDA registration). The upkeep
    /// trigger + CDA are attached to the card for structural / dispatcher
    /// tests; fire / Compute them manually in tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Karn with optional live wiring. When
    /// <paramref name="effects"/> is supplied the CDA P/T is registered;
    /// when <paramref name="triggers"/> is supplied the upkeep mana trigger
    /// is registered so a controller-upkeep <see cref="StepStartedEvent"/>
    /// places it on the stack.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T is CDA-defined ("*/*"); seed 0/0 placeholders since
        // Layer 7a overwrites them on every Compute.
        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 0,
            toughness: 0,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Golem });

        // CR 301.1 — Artifact Creature: additively flag the Artifact type so
        // HasType lookups (and Karn's own artifact-counting clauses) see both
        // types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep mana trigger — CR 603.1.
        //   "At the beginning of your upkeep, add {C} for each artifact you
        //    control. This mana can't be spent to cast nonartifact spells.
        //    Until end of turn, you don't lose this mana as steps and phases
        //    end."
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: add {{C}} per artifact (artifact-spells-only, doesn't empty)",
            () =>
            {
                var artifacts = ArtifactsControlled(owner);
                if (artifacts <= 0) return;

                // {C} × artifacts — colorless mana stored in the Generic
                // bucket (CR 106.1b), each unit carrying the artifact-spell
                // spend-restriction (CR 106.4) AND the doesn't-empty rider
                // (CR 500.4 exception).
                var mana = ManaCost.Parse($"{artifacts}");
                owner.AddManaToPool(
                    mana,
                    provenanceSource: card,
                    restriction: ArtifactSpellsOnly,
                    doesNotEmpty: true);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // CDA P/T — CR 604.3 / 613.2 Layer 7a.
        //   "Karn's power and toughness are each equal to the greatest mana
        //    value among artifacts you control."
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new KarnCdaLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Karn's CDA. Registers a
    /// <see cref="CdaPowerToughnessEffect"/> (both power and toughness =
    /// <see cref="GreatestArtifactManaValue"/>) while Karn is on the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <c>MortivoreCdaLifecycle</c>.
    /// </summary>
    private sealed class KarnCdaLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public KarnCdaLifecycle(
            Creature source,
            Player controller,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _controller = controller;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => GreatestArtifactManaValue(_controller),
                    toughnessOf: _ => GreatestArtifactManaValue(_controller));
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
