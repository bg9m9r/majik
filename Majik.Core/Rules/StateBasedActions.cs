using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules.Sba;
using Majik.Core.Rules.Sba.Checks;
using Majik.Core.Services;

namespace Majik.Core.Rules;

/// <summary>
/// Coordinator for state-based actions (CR 704).
///
/// Runs a registered list of <see cref="IStateBasedActionCheck"/> in a
/// fixed-point loop (CR 704.4) until a pass produces no changes. The
/// check set defaults to the standard rule-704.5 ordering; callers may
/// supply a custom list for tests or to insert format-specific checks.
/// After SBAs settle, state-change triggers are evaluated (CR 603.2c).
/// </summary>
public class StateBasedActions
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;
    private readonly TriggerManager? _triggerManager;
    private readonly ReplacementBus? _replacements;
    private readonly IReadOnlyList<IStateBasedActionCheck> _checks;

    /// <summary>The check list active for this coordinator (read-only).</summary>
    public IReadOnlyList<IStateBasedActionCheck> Checks => _checks;

    public StateBasedActions(
        IEventBus? eventBus = null,
        ZoneService? zoneService = null,
        TriggerManager? triggerManager = null,
        ReplacementBus? replacements = null,
        IEnumerable<IStateBasedActionCheck>? checks = null)
    {
        _eventBus = eventBus;
        _zoneService = zoneService;
        _triggerManager = triggerManager;
        _replacements = replacements;
        _checks = (checks ?? DefaultChecks()).ToList();
    }

    /// <summary>
    /// The standard CR 704.5 ordering used when no custom list is passed.
    /// Order matters: e.g. counter cancellation must precede creature
    /// death so a -1/-1 + +1/+1 pair clears before lethal-damage check.
    /// </summary>
    public static IEnumerable<IStateBasedActionCheck> DefaultChecks()
    {
        yield return new PlayerLifeCheck();
        yield return new CounterCancellationCheck();
        yield return new TokensCeaseToExistCheck();
        yield return new AttachmentLegalityCheck();
        yield return new BattleDestroyedCheck();
        yield return new SagaSacrificedCheck();
        yield return new SpellWithNoCardCheck();
        yield return new CreatureDeathCheck();
        yield return new PlaneswalkerDeathCheck();
        yield return new LegendRuleCheck();
        yield return new PlaneswalkerUniquenessCheck();
    }

    /// <summary>
    /// Run the SBA loop until quiescent (CR 704.4), then evaluate
    /// state-change triggers (CR 603.2c).
    /// </summary>
    public void CheckStateBasedActions(IEnumerable<Player> players, IEnumerable<ICard> allCards)
    {
        if (players == null || allCards == null) return;

        var playerList = players.ToList();
        var ctx = new SbaContext(
            playerList,
            allCards.ToList(),
            _eventBus,
            _zoneService,
            _triggerManager,
            _replacements);

        bool anyExecuted;
        do
        {
            anyExecuted = false;
            foreach (var check in _checks)
            {
                if (check.Execute(ctx)) anyExecuted = true;
            }
            ctx.Cards = allCards.ToList();
        } while (anyExecuted);

        _triggerManager?.EvaluateStateChangeTriggers();
    }
}
