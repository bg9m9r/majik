using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Combat;

/// <summary>
/// Service for managing combat.
/// Coordinates all combat operations according to Magic: The Gathering rules (Rule 506-511).
/// </summary>
public class CombatManager
{
    private readonly IEventBus? _eventBus;
    private readonly CombatValidator _validator;
    private readonly StateBasedActions? _stateBasedActions;
    private readonly ZoneService? _zoneService;
    private readonly CombatDamageAssigner _damageAssigner;

    private Combat? _currentCombat;

    /// <summary>
    /// The current combat instance.
    /// </summary>
    public Combat? CurrentCombat => _currentCombat;

    /// <summary>
    /// Whether combat is currently active.
    /// </summary>
    public bool IsInCombat => _currentCombat != null && !_currentCombat.IsEnded;

    public CombatManager(
        IEventBus? eventBus = null,
        StateBasedActions? stateBasedActions = null,
        ZoneService? zoneService = null,
        ContinuousEffectsService? continuousEffects = null)
    {
        _eventBus = eventBus;
        _validator = new CombatValidator(continuousEffects);
        _stateBasedActions = stateBasedActions;
        _zoneService = zoneService;
        _damageAssigner = new CombatDamageAssigner(eventBus);
    }

    /// <summary>
    /// Start a new combat (Rule 507: Beginning of Combat step).
    /// </summary>
    public void StartCombat(Player activePlayer)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (IsInCombat)
        {
            throw new InvalidGameStateException("Combat is already in progress");
        }

        // Combat starts with no attackers declared yet
        // The target will be determined when attackers are declared
        _currentCombat = null; // Will be created when attackers are declared

        _eventBus?.Publish(new CombatStartedEvent(activePlayer));
    }

    /// <summary>
    /// Declare attackers (Rule 508: Declare Attackers step).
    /// </summary>
    public void DeclareAttackers(Player activePlayer, IEnumerable<AttackerDeclaration> declarations)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (declarations == null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        var declarationList = declarations.ToList();

        // Determine target from first attacker
        Player? targetPlayer = null;
        Planeswalker? targetPlaneswalker = null;

        if (declarationList.Count > 0)
        {
            var firstDecl = declarationList[0];
            targetPlayer = firstDecl.TargetPlayer;
            targetPlaneswalker = firstDecl.TargetPlaneswalker;
        }

        // Validate all attackers
        var attackers = declarationList.Select(d => d.Creature).ToList();
        if (!_validator.IsValidAttackDeclaration(attackers, activePlayer, targetPlayer, targetPlaneswalker))
        {
            throw new InvalidPlayerActionException("Invalid attacker declaration");
        }

        // Create combat instance
        _currentCombat = new Combat(activePlayer, targetPlayer, targetPlaneswalker);

        // Create attacker objects and add to combat
        foreach (var declaration in declarationList)
        {
            var hasFirstStrike = CombatAbilities.HasFirstStrike(declaration.Creature);
            var hasDoubleStrike = CombatAbilities.HasDoubleStrike(declaration.Creature);
            var hasTrample = CombatAbilities.HasTrample(declaration.Creature);
            var hasDeathtouch = CombatAbilities.HasDeathtouch(declaration.Creature);
            var hasVigilance = CombatAbilities.HasVigilance(declaration.Creature);

            var attacker = new Attacker(
                declaration.Creature,
                declaration.TargetPlayer,
                declaration.TargetPlaneswalker,
                hasFirstStrike,
                hasDoubleStrike,
                hasTrample,
                hasDeathtouch,
                hasVigilance);

            _currentCombat.AddAttacker(attacker);

            // Tap attacker (unless has vigilance) (Rule 508.1k)
            if (!hasVigilance)
            {
                declaration.Creature.Tap();
            }
        }

        _currentCombat.TransitionToDeclaringBlockers();

        _eventBus?.Publish(new AttackersDeclaredEvent(_currentCombat));
    }

    /// <summary>
    /// CR 508.3g / Mobilize — splice a creature into the current combat as an
    /// attacker that is already <b>tapped and attacking</b>, without it being
    /// "declared" as an attacker. This is the combat primitive behind effects
    /// that "create a tapped and attacking token" (Mobilize, Geist of Saint
    /// Traft's Angel, Goblin Rabblemaster-style payoffs).
    ///
    /// The creature is tapped (CR 508.3 — it enters combat tapped) and added
    /// to <see cref="CurrentCombat"/>'s attacker set against the same
    /// defending player / planeswalker as the in-progress combat (CR 508.4).
    /// It is legal to call while combat is in any state before it ends — the
    /// creature bypasses the declare-attackers step entirely, so it does NOT
    /// publish a <see cref="CreatureAttacksEvent"/> (CR 508.3g — a creature
    /// put onto the battlefield attacking was never "declared as an attacker",
    /// so "whenever a creature attacks" abilities do not trigger).
    ///
    /// Returns the created <see cref="Attacker"/>, or <c>null</c> when there is
    /// no combat in progress (nothing to attach to — the caller should fall
    /// back to a plain battlefield token).
    /// </summary>
    public Attacker? AddTappedAndAttackingToken(Creature creature)
        => AddTappedAndAttackingToken(creature, targetPlaneswalker: null);

    /// <summary>
    /// CR 508.4 overload — splice a tapped-and-attacking token into the current
    /// combat against a <b>specified defender</b>. When
    /// <paramref name="targetPlaneswalker"/> is non-null the token attacks that
    /// planeswalker (which must be controlled by the combat's defending player —
    /// "that player OR a planeswalker they control", Adeline); when null the
    /// token attacks the combat's own defender exactly as the parameterless
    /// overload does. The planeswalker band is validated by
    /// <see cref="Combat.AddAttackerInProgress"/> (CR 508.4 — same defending
    /// player). Returns the created <see cref="Attacker"/>, or <c>null</c> when
    /// no combat is in progress.
    /// </summary>
    public Attacker? AddTappedAndAttackingToken(Creature creature, Cards.Planeswalker? targetPlaneswalker)
    {
        if (creature == null)
        {
            throw new ArgumentNullException(nameof(creature));
        }

        if (_currentCombat == null || _currentCombat.IsEnded)
        {
            return null;
        }

        // CR 508.3 — the creature enters combat tapped. Guard the Tap() call
        // because Permanent.Tap throws if already tapped.
        if (!creature.IsTapped)
        {
            creature.Tap();
        }

        // CR 508.4 — attacking the chosen defender. When a planeswalker is
        // specified the token bands against it (it must belong to the combat's
        // defending player); otherwise mirror the combat's single defender. The
        // combat targets exactly one of a player / planeswalker, so pick the
        // player band only when no planeswalker is chosen.
        Player? bandPlayer = targetPlaneswalker != null ? null : _currentCombat.DefendingPlayer;
        Cards.Planeswalker? bandWalker = targetPlaneswalker ?? _currentCombat.TargetPlaneswalker;

        var attacker = new Attacker(
            creature,
            bandPlayer,
            bandWalker,
            CombatAbilities.HasFirstStrike(creature),
            CombatAbilities.HasDoubleStrike(creature),
            CombatAbilities.HasTrample(creature),
            CombatAbilities.HasDeathtouch(creature),
            CombatAbilities.HasVigilance(creature));

        _currentCombat.AddAttackerInProgress(attacker);
        return attacker;
    }

    /// <summary>
    /// CR 509.1 (token-creation analogue) — splice a creature into the current
    /// combat as a token that is already <b>blocking a specific attacker</b>,
    /// without it being "declared" as a blocker. This is the block-side sibling
    /// of <see cref="AddTappedAndAttackingToken"/>: the primitive behind effects
    /// that "create a token that's blocking that creature" (Brimaz, King of
    /// Oreskos's block trigger — CR 509.4 / 509.1h, a creature put onto the
    /// battlefield blocking is "blocking" but was never "declared as a blocker",
    /// so "whenever a creature blocks" abilities do NOT trigger off it).
    ///
    /// The blocking <paramref name="token"/> is attached to the
    /// <see cref="Attacker"/> wrapping <paramref name="blockedAttacker"/> in the
    /// current combat, so it participates in the combat-damage step exactly as a
    /// declared blocker would (deals/takes damage with that attacker). It does
    /// NOT tap (CR 509.1 — blocking does not tap) and does not bypass any block
    /// LEGALITY checks because it was put into combat by an effect, not declared
    /// (CR 509.4).
    ///
    /// Returns the created <see cref="Blocker"/>, or <c>null</c> when there is no
    /// combat in progress or <paramref name="blockedAttacker"/> is not an
    /// attacker in it (nothing to block — the caller should fall back to a plain
    /// battlefield token).
    /// </summary>
    public Blocker? AddBlockingToken(Creature token, Creature blockedAttacker)
    {
        if (token == null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        if (blockedAttacker == null)
        {
            throw new ArgumentNullException(nameof(blockedAttacker));
        }

        if (_currentCombat == null || _currentCombat.IsEnded)
        {
            return null;
        }

        // Find the attacker entry the token is to block. A token can only be
        // created blocking a creature that is actually attacking this combat
        // (CR 509.1 — you block an attacking creature).
        var attacker = _currentCombat.Attackers
            .FirstOrDefault(a => ReferenceEquals(a.Creature, blockedAttacker));
        if (attacker == null)
        {
            return null;
        }

        var blocker = new Blocker(
            token,
            attacker,
            CombatAbilities.HasFirstStrike(token),
            CombatAbilities.HasDoubleStrike(token),
            CombatAbilities.HasDeathtouch(token));

        attacker.AddBlocker(blocker);
        return blocker;
    }

    /// <summary>
    /// Declare blockers (Rule 509: Declare Blockers step).
    /// </summary>
    public void DeclareBlockers(Player defendingPlayer, IEnumerable<BlockerDeclaration> declarations)
    {
        if (defendingPlayer == null)
        {
            throw new ArgumentNullException(nameof(defendingPlayer));
        }

        if (declarations == null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        if (_currentCombat == null)
        {
            throw new InvalidGameStateException("No combat in progress");
        }

        if (_currentCombat.State != CombatState.DeclaringBlockers)
        {
            throw new InvalidGameStateException($"Cannot declare blockers in state {_currentCombat.State}");
        }

        var declarationList = declarations.ToList();

        // Validate all blockers. The full attacker set + the defending
        // player's untapped battlefield creatures are passed so the validator
        // can enforce "all creatures able to block ~ do so" requirements
        // (CR 509.1c — Lure / Breaker of Armies / Nemesis Mask family), not
        // just per-block legality.
        var blocks = declarationList.Select(d => (d.Creature, d.Attacker)).ToList();
        var availableBlockers = defendingPlayer.Zones.Battlefield
            .GetCards()
            .OfType<Creature>()
            .ToList();
        if (!_validator.IsValidBlockDeclaration(
                blocks, defendingPlayer, _currentCombat.Attackers, availableBlockers))
        {
            throw new InvalidPlayerActionException("Invalid blocker declaration");
        }

        // Create blocker objects and add to attackers
        foreach (var declaration in declarationList)
        {
            var hasFirstStrike = CombatAbilities.HasFirstStrike(declaration.Creature);
            var hasDoubleStrike = CombatAbilities.HasDoubleStrike(declaration.Creature);
            var hasDeathtouch = CombatAbilities.HasDeathtouch(declaration.Creature);

            var blocker = new Blocker(
                declaration.Creature,
                declaration.Attacker,
                hasFirstStrike,
                hasDoubleStrike,
                hasDeathtouch);

            declaration.Attacker.AddBlocker(blocker);
        }

        _currentCombat.TransitionToAssigningDamage();

        _eventBus?.Publish(new BlockersDeclaredEvent(_currentCombat));
    }

    /// <summary>
    /// Assign combat damage (Rule 510: Combat Damage step).
    /// </summary>
    public void AssignCombatDamage()
    {
        if (_currentCombat == null)
        {
            throw new InvalidGameStateException("No combat in progress");
        }

        if (_currentCombat.State != CombatState.AssigningDamage)
        {
            throw new InvalidGameStateException($"Cannot assign damage in state {_currentCombat.State}");
        }

        if (_damageAssigner.HasFirstStrikeDamage(_currentCombat))
        {
            _damageAssigner.AssignAndResolve(_currentCombat, isFirstStrike: true);
            RunSbaForCombat(_currentCombat);
            _damageAssigner.Reset(_currentCombat);
        }

        _damageAssigner.AssignAndResolve(_currentCombat, isFirstStrike: false);
        RunSbaForCombat(_currentCombat);

        _currentCombat.TransitionToResolvingDamage();
    }

    private void RunSbaForCombat(Combat combat)
    {
        if (_stateBasedActions == null || combat.AttackingPlayer == null) return;
        var players = new[] { combat.AttackingPlayer, combat.DefendingPlayer }
            .Where(p => p != null)
            .Cast<Player>();
        _stateBasedActions.CheckStateBasedActions(players, _damageAssigner.GetCombatCreatures(combat));
    }

    /// <summary>
    /// End combat (Rule 511: End of Combat step).
    /// </summary>
    public void EndCombat()
    {
        if (_currentCombat == null)
        {
            return; // No combat to end
        }

        _eventBus?.Publish(new CombatEndedEvent(_currentCombat));
        _currentCombat.End();
        _currentCombat = null;
    }

    /// <summary>
    /// Get valid attackers for a player.
    /// </summary>
    public IEnumerable<Creature> GetValidAttackers(Player player)
    {
        if (player == null)
        {
            return Enumerable.Empty<Creature>();
        }

        // Get all creatures controlled by player on battlefield
        // TODO: Get from ZoneService or player's battlefield zone
        return Enumerable.Empty<Creature>();
    }

    /// <summary>
    /// Get valid blockers for a player against an attacker.
    /// </summary>
    public IEnumerable<Creature> GetValidBlockers(Player player, Attacker attacker)
    {
        if (player == null || attacker == null)
        {
            return Enumerable.Empty<Creature>();
        }

        // Get all creatures controlled by player on battlefield that can block attacker
        // TODO: Get from ZoneService or player's battlefield zone
        return Enumerable.Empty<Creature>();
    }
}

/// <summary>
/// Declaration of an attacker.
/// </summary>
public class AttackerDeclaration
{
    public Creature Creature { get; }
    public Player? TargetPlayer { get; }
    public Planeswalker? TargetPlaneswalker { get; }

    public AttackerDeclaration(Creature creature, Player? targetPlayer = null, Planeswalker? targetPlaneswalker = null)
    {
        Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        TargetPlayer = targetPlayer;
        TargetPlaneswalker = targetPlaneswalker;

        if (targetPlayer == null && targetPlaneswalker == null)
        {
            throw new ArgumentException("Must specify either target player or target planeswalker");
        }
    }
}

/// <summary>
/// Declaration of a blocker.
/// </summary>
public class BlockerDeclaration
{
    public Creature Creature { get; }
    public Attacker Attacker { get; }

    public BlockerDeclaration(Creature creature, Attacker attacker)
    {
        Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
    }
}
