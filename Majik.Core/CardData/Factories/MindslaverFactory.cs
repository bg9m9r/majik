using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mindslaver (Mirrodin, {6}).
///
/// Legendary Artifact. Oracle text:
///   "{4}, {T}, Sacrifice Mindslaver: You control target player during
///    that player's next turn. (You see all cards that player could see
///    and make all decisions for them.)"
///
/// ## Implemented (v1)
/// - Card identity (Legendary Artifact, mana cost {6}, owner / controller).
/// - <b>{4}, {T}, Sacrifice Mindslaver: control target player next turn</b>
///   — <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>("{4}")
///   + <see cref="AdditionalCost"/>.Tap + <see cref="AdditionalCost"/>.Sacrifice.
///   A 1..1 <see cref="TargetRequest"/> for "target player" is declared.
///   On resolution the factory reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and confirms the choice
///   is a <see cref="Player"/>; the Mindslaver self-sac is performed by
///   the effect closure (mirrors Aether Spellbomb — the generic
///   <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
///
/// ## Mind-control (CR 720 — implemented)
/// - On resolution the closure resolves the chosen target player, sacrifices
///   Mindslaver, then takes control of that player's next turn via the live
///   <see cref="Majik.Core.Players.ControlPlayerRegistry"/> (looked up at
///   resolution time through
///   <see cref="Majik.Core.Players.ControlPlayerRegistryProvider"/> — the v1
///   sync effect model has no service parameter, so the registry is resolved
///   the same way tutor closures resolve their ZoneService). The controller
///   (Mindslaver's controller) then makes every decision the target player
///   would make during that player's next turn (CR 720.1); the target keeps
///   their own cards, hand, life, and library (CR 720.2 / CR 720.3).
/// - The optional <paramref name="mindControlSink"/> is still invoked with
///   the chosen target (after the grant) as a test / observability hook. In
///   shape-only construction (no live registry registered — the single-arg
///   dispatcher / shape tests) the grant is a no-op and only the sink fires.
///
/// ## Deferred sub-caveats (CR 720.5 / 720.6 — documented, not modelled)
/// - The controller still can't make the controlled player concede
///   (CR 720.6) and engine-resolved random choices (discard at random) are
///   unaffected. Neither regresses existing behaviour — see
///   <see cref="Majik.Core.Players.ControlPlayerRegistry"/>'s class doc.
/// </summary>
[CardName("Mindslaver")]
public static class MindslaverFactory
{
    public const string CardName = "Mindslaver";
    public const string Cost = "{6}";

    /// <summary>
    /// Construct Mindslaver with no live runtime wiring. The activated
    /// ability is attached for shape observability; resolution sacrifices
    /// Mindslaver and records the chosen target (if any) but no mind-
    /// control primitive runs.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, mindControlSink: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c>; an artifact gets the
    /// <c>[CardName]</c> factory instance-swap in production, so this IS the
    /// prod path). Threads <c>effects.EventBus</c> into the self-sacrifice cost
    /// so paying it publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a).
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, mindControlSink: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Mindslaver. When <paramref name="mindControlSink"/> is
    /// supplied, the activated ability's resolution closure invokes it
    /// with the chosen target player (after Mindslaver has been
    /// sacrificed). The sink is typically a test hook or a future
    /// ControlPlayer service shim — see the class xmldoc gap note. When
    /// <paramref name="eventBus"/> is supplied the self-sacrifice activation
    /// cost publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static Artifact Create(
        Player owner,
        Action<Player>? mindControlSink,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var slaver = new Artifact(
            CardName,
            Cost,
            supertypes: new[] { CardSupertype.Legendary });
        slaver.SetOwner(owner);
        slaver.SetController(owner);

        // ----------------------------------------------------------------
        // {4}, {T}, Sacrifice Mindslaver: You control target player
        // during that player's next turn.
        // ----------------------------------------------------------------
        ActivatedAbility? mindControlAbility = null;
        var mindControlEffect = new Effect(
            $"{CardName}: control target player next turn + sac self",
            () =>
            {
                // Self-sac stub — perform the zone move so visible state
                // matches CR 701.16. Done first so the target-player
                // record is independent of Mindslaver's zone. CR 701.16a —
                // when a bus is supplied (prod effects-aware build) route
                // through Fx.Sacrifice so a PermanentSacrificedEvent is
                // published; in the live activation path the cost already
                // moved the slaver, so this no-ops (single publish either way).
                if (slaver.Zone == ZoneType.Battlefield)
                {
                    if (eventBus != null)
                    {
                        Fx.Sacrifice(slaver, slaver.Controller ?? owner, eventBus);
                    }
                    else
                    {
                        owner.Zones.Battlefield.RemoveCard(slaver);
                        owner.Zones.Graveyard.AddCard(slaver);
                        slaver.SetZone(ZoneType.Graveyard);
                    }
                }

                // Resolve target.
                if (mindControlAbility == null) return;
                if (mindControlAbility.ChosenTargets.Count == 0) return;
                if (mindControlAbility.ChosenTargets[0].Count == 0) return;
                if (mindControlAbility.ChosenTargets[0][0] is not Player targetPlayer)
                    return; // CR 608.2b — illegal target → no-op

                // CR 720.1 — "You control target player during that player's
                // next turn." Take control via the live ControlPlayerRegistry,
                // resolved at resolution time through the provider (keyed by
                // the controlling player). Null in shape-only construction
                // (single-arg dispatcher / shape tests) → grant is a no-op.
                var registry = Majik.Core.Players.ControlPlayerRegistryProvider.Get(owner);
                registry?.GrantControl(controller: owner, controlled: targetPlayer);

                // Test / observability hook — fires after the grant with the
                // chosen target (see class doc).
                mindControlSink?.Invoke(targetPlayer);
            });

        mindControlAbility = new ActivatedAbility(
            source: slaver,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
                AdditionalCost.Tap(slaver),
                // CR 701.16a — bus on the SAC COST so the live activation path
                // (CostPayment -> cost.Pay) publishes PermanentSacrificedEvent.
                AdditionalCost.Sacrifice(slaver, eventBus),
            },
            effects: new IEffect[] { mindControlEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        slaver.AddAbility(mindControlAbility);

        return slaver;
    }
}
