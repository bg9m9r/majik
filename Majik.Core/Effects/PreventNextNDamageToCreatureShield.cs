using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent the next N damage that would be dealt to <em>a chosen
/// creature</em> this turn." Combines the per-creature target filter of
/// <see cref="PreventAllDamageToCreatureShield"/> with the finite damage
/// pool of <see cref="PreventNextNDamageToAnyTargetShield"/>: only
/// <see cref="DamageIntent"/>s aimed at <see cref="Protected"/> are soaked,
/// and only up to <see cref="RemainingPool"/> points across the turn. When
/// the pool is drained the shield stops applying; if it isn't drained before
/// cleanup, <see cref="IEndOfTurnExpirable"/> drops it anyway (CR 514.2).
///
/// Backs Eiganjo Castle — "{W}, {T}: Prevent the next 2 damage that would be
/// dealt to target legendary creature this turn." The chosen target is read
/// off the resolving ability's <c>ChosenTargets</c> and bound here as
/// <see cref="Protected"/>.
///
/// CR 615.1 — when the shield fully soaks an intent it returns null (no
/// damage dealt, no lifelink, no deathtouch flag set per CR 615.6); a partial
/// soak passes through a reduced-amount copy.
/// </summary>
public sealed class PreventNextNDamageToCreatureShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    private readonly Creature _protected;

    public PreventNextNDamageToCreatureShield(Creature protectedCreature, int amount)
    {
        _protected = protectedCreature ?? throw new ArgumentNullException(nameof(protectedCreature));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        RemainingPool = amount;
    }

    /// <summary>The creature this shield protects. Exposed for tests.</summary>
    public Creature Protected => _protected;

    /// <summary>Damage points still available to prevent.</summary>
    public int RemainingPool { get; private set; }

    public bool OneShot => false;
    public object? Tag => this;  // fires once per intent
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
        RemainingPool > 0
        && intent.Amount > 0
        && ReferenceEquals(intent.TargetCreature, _protected);

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        var absorbed = Math.Min(RemainingPool, intent.Amount);
        RemainingPool -= absorbed;
        var remaining = intent.Amount - absorbed;
        // CR 615.1 — fully-soaked intent is cancelled (null); a partial soak
        // passes through a reduced-Amount copy.
        return remaining == 0 ? null : intent with { Amount = remaining };
    }
}
