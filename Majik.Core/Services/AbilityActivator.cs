using Majik.Core.Abilities;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Services;

/// <summary>
/// Service for activating abilities.
/// Handles ability activation logic and adds abilities to the stack.
/// Implements Magic: The Gathering activation rules (Rule 602).
/// </summary>
public class AbilityActivator
{
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;
    private readonly CostPayment _costPayment;
    private readonly TargetValidator _targetValidator;

    public AbilityActivator(Majik.Core.Stack.Stack stack, IEventBus? eventBus = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _eventBus = eventBus;
        _costPayment = new CostPayment();
        _targetValidator = new TargetValidator();
    }

    /// <summary>
    /// Check if an ability can be activated. <paramref name="game"/> — when
    /// supplied — lets a context-aware "Activate only if" gate read live game
    /// state (CR 602.5c) that a context-less predicate can't reach (Hired Claw's
    /// "an opponent lost life this turn"). Null falls back to the context-less
    /// gate.
    /// </summary>
    public bool CanActivate(IActivatedAbility ability, Player player, Majik.Core.Game.GameContext? game = null)
    {
        if (ability == null || player == null)
        {
            return false;
        }

        // CR 602.5c / 605.1a — "Activate only if <condition>" gate. Reject
        // when the ability carries a condition predicate that is currently
        // unsatisfied (Metalcraft, Delirium, "you control a Forest", …).
        // Prefer the context-aware gate when a live GameContext is available.
        var canActivate = ability is ActivatedAbility aaGate
            ? aaGate.CanActivateNow(game)
            : ability.CanActivateNow();
        if (!canActivate)
        {
            return false;
        }

        // Basic validation - full implementation in Phase 4
        // For now, just check if player controls the source
        return true;
    }

    /// <summary>
    /// Activate an ability with targets and costs (full activation process per Rule 602.2a-d).
    /// <paramref name="game"/> threads the live <see cref="Majik.Core.Game.GameContext"/>
    /// into the context-aware activation gate (CR 602.5c).
    /// </summary>
    public void ActivateAbility(IActivatedAbility ability, Player player, IEnumerable<ITarget>? targets = null, IEnumerable<ICost>? costs = null, Majik.Core.Game.GameContext? game = null)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        // Step 602.2a: Announce ability and move to stack
        if (!CanActivate(ability, player, game))
        {
            throw new InvalidPlayerActionException("Cannot activate ability");
        }

        // Step 602.2b: Choose mode (if modal) - TODO: Future
        // Step 602.2c: Choose targets - TODO: Validate targets against specification
        var targetList = targets?.ToList() ?? new List<ITarget>();

        // Step 602.2d: Determine total cost and pay costs.
        // CR 106.4 — supply an ability-cost spend context built from the
        // ability's source so spend-restricted floating mana (Sunken Citadel
        // "abilities of land sources", Eldrazi Temple "or activate abilities of
        // Eldrazi") is honoured: restricted mana the source doesn't satisfy is
        // withheld from this payment, and a matching restriction is positively
        // satisfied by the source's types/subtypes.
        var costList = costs?.ToList() ?? new List<ICost>();
        var spendContext = Majik.Core.Mana.ManaSpendContext.ForAbilityCost(
            ability.Source as Majik.Core.Cards.ICard);
        _costPayment.PayCosts(player, costList, spendContext);

        // Create activated ability with targets and costs. CR 602.4 — the
        // ability that goes on the stack must carry the SAME effects /
        // target requests / sorcery-speed rider as the original. Previously
        // the wrapper only got source/controller/targets/costs, so every
        // non-mana activation resolved to a no-op (fetchlands didn't
        // sacrifice or fetch, planeswalker abilities didn't fire, etc.).
        //
        // When ANY new field is added to ActivatedAbility, mirror it here
        // or the stack object will be a stub.
        var sourceAbility = ability as ActivatedAbility;
        var activatedAbility = new ActivatedAbility(
            source: ability.Source,
            controller: player,
            targets: targetList,
            costs: costList,
            effects: sourceAbility?.Effects,
            targetRequests: sourceAbility?.TargetRequests,
            sorcerySpeed: ability.IsSorcerySpeed,
            canActivateCheck: sourceAbility?.CanActivateCheck,
            canActivateCheckCtx: sourceAbility?.CanActivateCheckCtx);
        if (sourceAbility != null && sourceAbility.ChosenTargets.Count > 0)
        {
            activatedAbility.SetChosenTargets(sourceAbility.ChosenTargets);
        }
        // GAP 2 — mirror the per-activation chosen X onto the stack object so its
        // resolution effect reads the real X (ResolutionContext.ChosenX), not 0.
        // Parallel to the ChosenTargets copy above (CR 602.4 — the stack object
        // carries the same characteristics as the original).
        if (sourceAbility?.ChosenX is { } chosenX)
        {
            activatedAbility.SetChosenX(chosenX);
        }

        // Add ability to stack
        _stack.Push(activatedAbility);

        // Fire events
        _eventBus?.Publish(new AbilityActivatedEvent(activatedAbility));
        if (targetList.Any())
        {
            _eventBus?.Publish(new TargetsChosenEvent(activatedAbility, targetList));
        }
        if (costList.Any())
        {
            _eventBus?.Publish(new CostsPaidEvent(activatedAbility, costList));
        }
    }

}
