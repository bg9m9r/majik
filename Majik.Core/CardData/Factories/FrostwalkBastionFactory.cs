using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frostwalk Bastion (Kaldheim, snow manland).
///
/// Snow Land. Oracle text (verified Scryfall 2026-05-29):
///   "{T}: Add {C}.
///    {1}{S}: Until end of turn, this land becomes a 2/3 Construct artifact
///    creature. It's still a land. ({S} can be paid with one mana from a snow
///    source.)
///    Whenever this land deals combat damage to a creature, tap that creature
///    and it doesn't untap during its controller's next untap step."
///
/// Same posture as the sibling <see cref="CaveOfTheFrostDragonFactory"/>
/// manland: the base shape (Snow nonbasic Land, {T}: Add {C}) is materialised
/// from the embedded JSON definition (<c>frostwalk-bastion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the animate ability and the
/// per-instance combat-damage rider are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express either yet.
///
/// ## Implemented (v1)
/// - <b>Snow Land identity</b> + the <c>{T}: Add {C}</c> mana ability — both
///   from the JSON definition (Snow supertype, CR 605.1 mana ability, no
///   stack). {C} parses as colourless / +1 generic in <c>ManaCost.Parse</c>.
/// - <b>{1}{S}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{S}</c> ({S} parses as +1 generic — snow-source-specific payment
///   gating is deferred engine-wide, same as every other {S} card). Resolution
///   registers two end-of-turn-expirable continuous effects:
///     - Layer 4 (<see cref="FrostwalkBastionAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> and <see cref="CardType.Artifact"/>
///       to the permanent's effective types and grants
///       <see cref="CardSubtype.Construct"/>. The printed Land type is left
///       intact ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="FrostwalkBastionBecomesPTEffect"/>) — set-base
///       P/T 2/3 (CR 613.7b). Same "Land runtime instance" shim posture as
///       <see cref="CaveOfTheFrostDragonBecomesPTEffect"/> /
///       <see cref="MutavaultBecomesPTEffect"/>.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> =
///   true so cleanup-step expiry (CR 514.2) lifts the animation.
/// - <b>"Whenever this land deals combat damage to a creature" trigger
///   (CR 603.1)</b> — when an <see cref="IEventBus"/> is supplied, a
///   <see cref="CombatDamageDealtEvent"/> handler fires when the Bastion
///   (<c>SourceCard == this land</c>) deals combat damage to a creature
///   target. The effect:
///     1. <b>Taps that creature</b> (CR 701.21a) — guarded so an
///        already-tapped creature is left tapped (CR — tapping a tapped
///        permanent does nothing; <see cref="Permanent.Tap"/> throws on a
///        double tap so the guard is mandatory).
///     2. <b>Skips its next untap step</b> — registers the victim with
///        <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>
///        (CR 502.1); a one-shot <see cref="StepStartedEvent"/> handler
///        removes the skip after the first <see cref="PhaseStateType.Untap"/>
///        step belonging to the victim's controller (CR 611.2b). Mirrors the
///        skip-untap plumbing in <see cref="WallOfFrostFactory"/>.
///
/// ## Deferred (v1 gaps — shared with the manland cycle)
/// - <b>Combat math through Compute</b>: same gap as Cave of the Frost Dragon
///   / Mutavault — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to a
///   <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 2/3 body doesn't surface for combat
///   resolution. The combat-damage rider is wired event-first (same as
///   <see cref="WallOfFrostFactory"/>) so it fires whenever the engine
///   publishes a <see cref="CombatDamageDealtEvent"/> sourced from this land.
/// - <b>Snow-source {S} payment gating</b>: deferred engine-wide ({S} = +1
///   generic in <c>ManaCost.Parse</c>); the animate cost is payable from any
///   mana for now.
/// </summary>
[CardName("Frostwalk Bastion")]
public static class FrostwalkBastionFactory
{
    public const string CardName = "Frostwalk Bastion";
    public const string Slug = "frostwalk-bastion";
    public const string AnimateManaCost = "{1}{S}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Frostwalk Bastion with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="IEventBus"/> wired. The {T}: Add {C} mana ability (from
    /// JSON) + the animate ability are attached so the card surface is
    /// complete; the layer effects are not registered and the combat-damage
    /// rider is structurally attached only. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Frostwalk Bastion.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="eventBus">Event bus for the combat-damage rider (tap +
    /// skip-untap) and its one-shot cleanup. May be null — the rider is
    /// structurally attached but never fires.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Snow Land,
        // {T}: Add {C} mana ability). The animate ability + combat-damage
        // rider are layered on below — neither is expressible in the current
        // JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}{S}: Until end of turn, this land becomes a 2/3 Construct
        // artifact creature. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{S}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 2/3 Construct artifact creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature + Artifact types, Construct subtype.
                // Printed Land type stays ("it's still a land").
                effects.Register(new FrostwalkBastionAnimateEffect(land));

                // Layer 7b — set base P/T 2/3.
                effects.Register(new FrostwalkBastionBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(AnimateManaCost) },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // "Whenever this land deals combat damage to a creature, tap that
        //  creature and it doesn't untap during its controller's next untap
        //  step." (CR 603.1)
        //
        // Marker triggered ability (so shape/dispatch surfaces it); actual
        // firing is driven by the bus handler below — same event-first
        // posture as WallOfFrostFactory. Fires when this land is the source
        // of a CombatDamageDealtEvent whose target is a creature.
        // ----------------------------------------------------------------
        var combatRiderMarker = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.SourceCard, land) && e.TargetCard is Creature),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: tap the damaged creature; it skips its next untap step",
                    () => { /* no-op marker; bus handler performs the work */ }),
            },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(combatRiderMarker);

        if (eventBus != null)
        {
            eventBus.Subscribe<CombatDamageDealtEvent>(e =>
            {
                if (!ReferenceEquals(e.SourceCard, land)) return;
                if (e.TargetCard is not Creature victim) return;
                ApplyCombatRider(victim, eventBus);
            });
        }

        return land;
    }

    // --- Combat-damage rider body (CR 701.21a / 502.1 / 611.2b) ------------

    /// <summary>
    /// Resolve the "deals combat damage to a creature" rider against
    /// <paramref name="victim"/>: tap it (CR 701.21a, no-op if already tapped)
    /// and register a skip of its controller's next untap step (CR 502.1),
    /// with a one-shot <see cref="StepStartedEvent"/> cleanup (CR 611.2b).
    /// Public so the load-bearing behaviour can be exercised directly while
    /// the manland combat-math gap keeps an animated Land from surfacing as a
    /// <see cref="CombatDamageDealtEvent"/> source in production combat.
    /// </summary>
    public static void ApplyCombatRider(Creature victim, IEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(victim);
        ArgumentNullException.ThrowIfNull(eventBus);
        if (victim.Zone != ZoneType.Battlefield) return;

        // CR 701.21a — tap that creature. Guard: Permanent.Tap throws on a
        // double tap, and tapping an already-tapped permanent is a no-op.
        if (!victim.IsTapped)
        {
            victim.Tap();
        }

        // CR 502.1 — it doesn't untap during its controller's next untap
        // step. Register a per-permanent skip and schedule a one-shot
        // cleanup after the victim's controller's next untap step.
        var skipToken = new object();
        UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, victim);
        ScheduleSkipUntapCleanup(victim, skipToken, eventBus);
    }

    private static void ScheduleSkipUntapCleanup(
        Creature victim,
        object skipToken,
        IEventBus eventBus)
    {
        // CR 611.2b — one-shot: remove the skip on the first Untap step that
        // belongs to the victim's current controller.
        var targetController = victim.Controller;
        Action<GameEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (ev is not StepStartedEvent sse) return;
            if (sse.StepType != PhaseStateType.Untap) return;
            if (!ReferenceEquals(sse.Player, targetController)) return;

            UntapStepRestrictions.RemoveAll(skipToken);
            if (cleanupHandler != null)
                eventBus.UnsubscribeAll(cleanupHandler);
        };
        eventBus.SubscribeAll(cleanupHandler);
    }
}

/// <summary>
/// Frostwalk Bastion — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> and
/// <see cref="CardType.Artifact"/> to the permanent's effective types, plus
/// the <see cref="CardSubtype.Construct"/> subtype. Expires at end of turn
/// (CR 514.2).
///
/// CR 613.1c — types are added on top of printed values; the printed
/// <see cref="CardType.Land"/> remains intact, matching the oracle's "It's
/// still a land" rider.
/// </summary>
public sealed class FrostwalkBastionAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public FrostwalkBastionAnimateEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Types.Add(CardType.Creature);
        chars.Types.Add(CardType.Artifact);
        chars.Subtypes.Add(CardSubtype.Construct);
    }
}

/// <summary>
/// Frostwalk Bastion — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the 2/3 the manland turns into. Same "Land runtime
/// instance" shim posture as <see cref="CaveOfTheFrostDragonBecomesPTEffect"/>
/// and <see cref="MutavaultBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
/// <see cref="PermanentCharacteristics"/> with no P/T fields, so the effect is
/// registered for layer-system correctness and exposes <see cref="NewPower"/>
/// / <see cref="NewToughness"/> for inspection until
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> can upgrade the
/// chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class FrostwalkBastionBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public FrostwalkBastionBecomesPTEffect(Permanent target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 7b on a non-Creature row is observationally a no-op in the
        // current pipeline. See class xmldoc.
    }
}
