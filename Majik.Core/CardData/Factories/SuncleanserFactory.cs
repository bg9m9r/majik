using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Suncleanser (Core Set 2021, {1}{W}).
///
/// Creature — Human Cleric 1/4. Oracle text:
///   "When this creature enters, choose one —
///    • Remove all counters from target creature. It can't have counters put
///      on it for as long as this creature remains on the battlefield.
///    • Target opponent loses all counters. That player can't get counters
///      for as long as this creature remains on the battlefield."
///
/// ## Implemented (v1)
/// - 1/4 Creature — Human Cleric, mana cost {1}{W}.
/// - <b>ETB modal triggered ability</b> (CR 700.2d — "Choose one —", CR
///   603.1 / CR 603.6a). The mode is captured at factory time (mirrors
///   <see cref="CharmingPrinceFactory"/>'s <c>mode</c> closure) so tests can
///   exercise each arm deterministically without a full agent.
///
///   Both modes are two-part: (1) wipe the target's existing counters at
///   resolution, then (2) register a CR 614 replacement that PREVENTS any
///   future counter placement on that same target while Suncleanser remains
///   on the battlefield. Each replacement self-gates on
///   <c>card.Zone == Battlefield</c> (CR 614.6) AND target identity, so it
///   auto-revokes the moment Suncleanser leaves — no explicit deregister
///   needed (same battlefield-gated seam as
///   <see cref="SolemnityCounterAddReplacement"/> /
///   <see cref="StaticPrisonFactory"/>).
///
/// ## Modes
/// - <b>Mode 0 — target creature</b> (CR 122 / CR 614): wipes the creature's
///   <see cref="CounterCollection"/> via <see cref="CounterCollection.Clear"/>,
///   then registers a <see cref="CounterAddIntent"/> replacement that rewrites
///   every future placement ON THAT CREATURE to <c>Amount = 0</c> (the
///   Solemnity shape, scoped to a single permanent). Honoured by
///   <see cref="Services.CountersService.Add"/>.
/// - <b>Mode 1 — target opponent / player</b> (CR 122 / CR 107.16 / CR
///   107.14 / CR 704.5c / CR 614): wipes the player's poison / energy /
///   experience / generic counters via <see cref="Player.RemoveAllCounters"/>,
///   then registers a <see cref="PlayerCounterAddIntent"/> replacement that
///   rewrites every future placement ON THAT PLAYER to <c>Amount = 0</c>.
///   Honoured by <see cref="Services.PlayerCountersService.Add"/> (the path
///   <see cref="Player.AddPoisonCounters"/> / <see cref="Player.GainEnergy"/>
///   / <see cref="Player.AddCounters"/> route through when a bus is attached).
///   Only the chosen player is locked — other players keep getting counters.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. No replacements registered,
///   no trigger-manager. Suitable for dispatcher / structural tests. Defaults
///   to mode 1 (player), the gap this card closes.
/// - <see cref="Create(Player, int, ReplacementBus?, TriggerManager?)"/> —
///   captures the mode; when a <see cref="ReplacementBus"/> is supplied the
///   chosen mode's "can't get counters" lock is registered, and when a
///   <see cref="TriggerManager"/> is supplied the ETB trigger fires on the bus.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode prompt</b>: the mode is captured at factory
///   time for test determinism (same posture as
///   <see cref="CharmingPrinceFactory"/>). The agent mode-prompt surface is
///   the wiring point when it ships.
/// - <b>Mode-1 "opponent" restriction</b>: the printed text targets an
///   opponent. The target gatherer offers every player; opponent-only
///   filtering is enforced by the targeting layer / candidate list at
///   cast time (same posture as other "target opponent" cards). The lock
///   itself is correct for whichever player is chosen.
/// </summary>
[CardName("Suncleanser")]
public static class SuncleanserFactory
{
    public const string CardName = "Suncleanser";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>Mode index for "Remove all counters from target creature; it can't have counters put on it…".</summary>
    public const int ModeCreature = 0;
    /// <summary>Mode index for "Target opponent loses all counters; that player can't get counters…".</summary>
    public const int ModePlayer = 1;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Remove all counters from target creature. It can't have counters put on it for as long as this creature remains on the battlefield.",
        "Target opponent loses all counters. That player can't get counters for as long as this creature remains on the battlefield.",
    };

    /// <summary>
    /// Construct Suncleanser with no live wiring. The ETB trigger is attached
    /// for shape inspection; no replacement / trigger-manager is registered.
    /// Defaults to <see cref="ModePlayer"/>. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, mode: ModePlayer, replacements: null, triggers: null);

    /// <summary>
    /// Construct Suncleanser with optional replacement-bus + trigger-manager
    /// wiring and a captured mode. When <paramref name="replacements"/> is
    /// supplied the chosen mode's "can't get counters" lock is registered
    /// (gated on Suncleanser staying on the battlefield + the chosen target).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0 = creature, 1 = player).</param>
    /// <param name="replacements">Replacement bus — when supplied the chosen
    /// mode's lock is registered.</param>
    /// <param name="triggers">TriggerManager — registers the ETB trigger for
    /// bus-driven firing. May be null.</param>
    public static Creature Create(
        Player owner,
        int mode,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB modal trigger (CR 603.6a / CR 700.2d). Two target slots,
        // both MinTargets=0 so the unchosen mode's slot doesn't gate the
        // ETB (CR 700.2d — only the chosen mode's targeting is relevant).
        //   Slot 0 → mode 0's "target creature".
        //   Slot 1 → mode 1's "target player".
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: choose one — wipe + lock target creature; or target player",
            () =>
            {
                if (etbTrigger == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                if (mode == ModeCreature)
                {
                    ExecuteCreatureMode(etbTrigger, card, replacements);
                }
                else
                {
                    ExecutePlayerMode(etbTrigger, card, replacements);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // Slot 0 — mode 0 "target creature".
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
                // Slot 1 — mode 1 "target opponent" (any player offered;
                // opponent filtering handled by the targeting layer).
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Mode 0 — "Remove all counters from target creature. It can't have
    /// counters put on it for as long as this creature remains on the
    /// battlefield." (CR 122 / CR 614.)
    /// </summary>
    private static void ExecuteCreatureMode(
        TriggeredAbility trigger,
        Creature source,
        ReplacementBus? replacements)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // (1) Wipe existing counters (CR 122).
        target.Counters.Clear();

        // (2) Lock — register a CR 614 replacement that zeroes every future
        // placement on this creature while Suncleanser is on the battlefield.
        replacements?.Register<CounterAddIntent>(
            new SuncleanserCreatureLockReplacement(source, target));
    }

    /// <summary>
    /// Mode 1 — "Target opponent loses all counters. That player can't get
    /// counters for as long as this creature remains on the battlefield."
    /// (CR 122 / CR 107.16 / CR 107.14 / CR 704.5c / CR 614.) The gap this
    /// card closes.
    /// </summary>
    private static void ExecutePlayerMode(
        TriggeredAbility trigger,
        Creature source,
        ReplacementBus? replacements)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count < 2 || chosen[1].Count == 0) return;
        if (chosen[1][0] is not Player target) return;

        // (1) "loses all counters" — wipe poison / energy / experience /
        // generic in one shot (CR 122).
        target.RemoveAllCounters();

        // (2) Lock — register a CR 614 replacement that zeroes every future
        // counter placement on THIS player while Suncleanser is on the
        // battlefield. Only this player is affected.
        replacements?.Register<PlayerCounterAddIntent>(
            new SuncleanserPlayerLockReplacement(source, target));
    }
}

/// <summary>
/// CR 614 replacement: while Suncleanser is on the battlefield, every
/// <see cref="PlayerCounterAddIntent"/> targeting the locked player is
/// rewritten to <c>Amount = 0</c> — "that player can't get counters for as
/// long as this creature remains on the battlefield." Self-gates on
/// Suncleanser's zone (CR 614.6) so it auto-revokes when Suncleanser leaves;
/// scoped by target identity so only the chosen player is locked.
/// </summary>
public sealed class SuncleanserPlayerLockReplacement : IReplacementEffect<PlayerCounterAddIntent>
{
    private readonly Creature _source;
    private readonly Player _lockedPlayer;

    public SuncleanserPlayerLockReplacement(Creature source, Player lockedPlayer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lockedPlayer = lockedPlayer ?? throw new ArgumentNullException(nameof(lockedPlayer));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Suncleanser instance this lock is keyed to.</summary>
    public Creature Source => _source;

    /// <summary>The player whose counter-gain is locked.</summary>
    public Player LockedPlayer => _lockedPlayer;

    public bool Applies(PlayerCounterAddIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — only active while Suncleanser is on the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;
        // Only the chosen player is locked (CR 614.1 — affects the named target).
        if (!ReferenceEquals(intent.Target, _lockedPlayer)) return false;
        if (intent.Amount <= 0) return false;
        return true;
    }

    public PlayerCounterAddIntent? Replace(PlayerCounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = 0 };
}

/// <summary>
/// CR 614 replacement: while Suncleanser is on the battlefield, every
/// <see cref="CounterAddIntent"/> targeting the locked creature is rewritten
/// to <c>Amount = 0</c> — "it can't have counters put on it for as long as
/// this creature remains on the battlefield." The Solemnity shape, scoped to
/// a single permanent. Self-gates on Suncleanser's zone (CR 614.6).
/// </summary>
public sealed class SuncleanserCreatureLockReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Creature _source;
    private readonly Permanent _lockedCreature;

    public SuncleanserCreatureLockReplacement(Creature source, Permanent lockedCreature)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lockedCreature = lockedCreature ?? throw new ArgumentNullException(nameof(lockedCreature));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Suncleanser instance this lock is keyed to.</summary>
    public Creature Source => _source;

    /// <summary>The creature whose counter-placement is locked.</summary>
    public Permanent LockedCreature => _lockedCreature;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (!ReferenceEquals(intent.Target, _lockedCreature)) return false;
        if (intent.Amount <= 0) return false;
        return true;
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = 0 };
}
