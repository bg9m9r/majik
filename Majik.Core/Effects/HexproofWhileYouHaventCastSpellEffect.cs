using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.3 (Layer 6, ability-adding) — a self-applied continuous effect that
/// grants the <c>Hexproof</c> keyword (CR 702.11) to its source creature ONLY
/// while that creature's controller has not cast a spell this turn.
///
/// Models Stoic Sphinx's "This creature has hexproof as long as you haven't
/// cast a spell this turn." Direct sibling of
/// <see cref="HexproofWhileUntappedEffect"/> (Paradise Druid) — the only
/// difference is the gating condition: Paradise Druid's "as long as it's
/// untapped" reads off the source's own tap state, whereas this reads "has the
/// controller cast a spell this turn?" — a per-turn, per-player fact (CR 608 /
/// CR 105) that lives in the game's turn tally
/// (<see cref="Majik.Core.Game.TurnState.SpellsCastByPlayer"/>).
///
/// <para>Because a <see cref="ContinuousEffect"/> only receives the in-flight
/// <see cref="CreatureCharacteristics"/> at <see cref="Apply"/> time (no
/// turn-state parameter) — and the source-generated effects-aware factory
/// dispatch (<c>Create(Player, ContinuousEffectsService)</c>) does not thread a
/// turn-state object through — this effect tracks the controller's
/// spell-casting itself off the live event bus exposed by the
/// <see cref="ContinuousEffectsService"/>:</para>
/// <list type="bullet">
///   <item><see cref="SpellCastEvent"/> — when the cast spell's controller IS
///   this creature's controller (CR 601 — the caster is the spell's
///   controller, surfaced via <c>Spell.Card.Controller</c>), the
///   "you've cast a spell" flag is set, so hexproof drops for the rest of the
///   turn.</item>
///   <item><see cref="TurnStartedEvent"/> — clears the flag at the start of
///   every turn (CR 500.1 / 514 — the per-turn tally resets), so hexproof is
///   restored at the next turn boundary.</item>
/// </list>
/// The keyword is added in <see cref="Apply"/> only while the flag is clear, so
/// the condition is re-evaluated on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> pass with no extra
/// wiring — same posture as <see cref="HexproofWhileUntappedEffect"/>.
///
/// <para>Subscriptions are taken at construction (when a non-null bus is
/// supplied) and released in <see cref="OnExpired"/> (CR 613 teardown — fired
/// when the service drops the effect on the source leaving the battlefield, on
/// end-of-turn cleanup, or on explicit unregister). When the bus is null
/// (shape-only / unit construction outside a live game graph) no subscriptions
/// are taken and the flag can be driven directly via
/// <see cref="MarkSpellCastThisTurn"/> / <see cref="ResetForNewTurn"/> for
/// tests.</para>
///
/// <see cref="Majik.Core.Targeting.TargetLegality"/> reads the computed keyword
/// set (<c>ActiveEffects.Compute(c).Keywords</c>) when the creature has a
/// <see cref="ContinuousEffectsService"/> attached, so an opponent can't target
/// Stoic Sphinx while its controller hasn't cast a spell this turn, and can
/// once they have.
/// </summary>
public sealed class HexproofWhileYouHaventCastSpellEffect : ContinuousEffect
{
    private readonly Creature _source;
    private readonly IEventBus? _bus;

    // True once this creature's controller has cast a spell this turn (CR 601).
    // While true, the conditional hexproof is suppressed for the rest of the
    // turn; reset at each turn boundary (CR 500.1 / 514).
    private bool _controllerCastSpellThisTurn;

    // Captured delegates so the same instances can be unsubscribed in
    // OnExpired (Action<T> equality is delegate-identity based).
    private readonly Action<SpellCastEvent>? _onSpellCast;
    private readonly Action<TurnStartedEvent>? _onTurnStarted;

    /// <summary>
    /// Build the conditional-hexproof effect for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature that has hexproof while its controller
    /// hasn't cast a spell this turn.</param>
    /// <param name="bus">The live event bus (typically
    /// <see cref="ContinuousEffectsService.EventBus"/>). When non-null the
    /// effect subscribes to <see cref="SpellCastEvent"/> +
    /// <see cref="TurnStartedEvent"/> to track the condition; when null
    /// (shape-only tests) no subscriptions are taken — drive the flag via
    /// <see cref="MarkSpellCastThisTurn"/> / <see cref="ResetForNewTurn"/>.</param>
    public HexproofWhileYouHaventCastSpellEffect(Creature source, IEventBus? bus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = bus;

        if (_bus != null)
        {
            _onSpellCast = OnSpellCast;
            _onTurnStarted = OnTurnStarted;
            _bus.Subscribe(_onSpellCast);
            _bus.Subscribe(_onTurnStarted);
        }
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the creature generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// Active while the source is on the battlefield. The "you haven't cast a
    /// spell this turn" condition is applied in <see cref="Apply"/>, not here,
    /// so the effect stays attached and is re-evaluated each Compute as the
    /// flag flips (cast a spell → drops; new turn → restored).
    /// </summary>
    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        // CR 702.11 — grant Hexproof only while the controller hasn't cast a
        // spell this turn.
        if (!_controllerCastSpellThisTurn)
        {
            chars.Keywords.Add("Hexproof");
        }
    }

    // CR 601 — the player who cast the spell is its controller. Prefer the
    // concrete spell's Controller (set at cast time by SpellCaster /
    // SpellCastFlow — CR 601.2a, authoritative even before the spell card's
    // own Controller is stamped), falling back to the spell card's Controller.
    // If that's this creature's controller, the condition lapses for the rest
    // of the turn.
    private void OnSpellCast(SpellCastEvent e)
    {
        var caster = (e.Spell as Majik.Core.Spells.Spell)?.Controller
                     ?? e.Spell?.Card?.Controller;
        if (caster != null && ReferenceEquals(caster, _source.Controller))
        {
            MarkSpellCastThisTurn();
        }
    }

    // CR 500.1 / 514 — the per-turn "spells you've cast this turn" tally resets
    // each turn, restoring the conditional hexproof.
    private void OnTurnStarted(TurnStartedEvent e) => ResetForNewTurn();

    /// <summary>
    /// Mark that this creature's controller has cast a spell this turn (CR
    /// 601), suppressing the conditional hexproof for the rest of the turn.
    /// Invalidates the layer cache so the next
    /// <see cref="ContinuousEffectsService.Compute(Permanent)"/> re-evaluates.
    /// Public so shape-only tests (null bus) can drive the gate directly.
    /// </summary>
    public void MarkSpellCastThisTurn()
    {
        if (_controllerCastSpellThisTurn) return;
        _controllerCastSpellThisTurn = true;
        _source.ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Reset the per-turn flag (CR 500.1 / 514), restoring the conditional
    /// hexproof. Invalidates the layer cache. Public so shape-only tests (null
    /// bus) can drive the gate directly.
    /// </summary>
    public void ResetForNewTurn()
    {
        if (!_controllerCastSpellThisTurn) return;
        _controllerCastSpellThisTurn = false;
        _source.ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// CR 613 teardown — release the bus subscriptions when the service drops
    /// this effect (source left the battlefield / end-of-turn / unregister), so
    /// a stale effect for a no-longer-on-battlefield Sphinx doesn't keep
    /// listening. Idempotent (Unsubscribe of an unknown handler is a no-op).
    /// </summary>
    public override void OnExpired()
    {
        if (_bus == null) return;
        if (_onSpellCast != null) _bus.Unsubscribe(_onSpellCast);
        if (_onTurnStarted != null) _bus.Unsubscribe(_onTurnStarted);
    }
}
