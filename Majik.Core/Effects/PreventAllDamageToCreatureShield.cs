using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all damage that would be dealt to <em>CREATURE</em>
/// this turn." Cancels every <see cref="DamageIntent"/> whose
/// <see cref="DamageIntent.TargetCreature"/> is the protected creature for
/// the remainder of the turn. Auto-drops at cleanup via
/// <see cref="IEndOfTurnExpirable"/>.
///
/// Backs the Favored Hoplite Heroic trigger ("prevent all damage that
/// would be dealt to Favored Hoplite this turn") and any future per-
/// creature, EOT-scoped prevention shield (Apostle's Blessing's
/// creature-half, Mother of Runes' protection grant, etc. — those have
/// their own keyword shapes, but the underlying damage-bus shield is
/// identical to this one).
///
/// Mirrors <see cref="PreventAllDamageToYouAndYourPermanentsShield"/>'s
/// design: a single-creature filter rather than a player + permanents
/// filter. CR 615.1 — prevention returns null to cancel the intent
/// entirely (no damage dealt, no lifelink, no deathtouch flag set per
/// CR 615.6).
/// </summary>
public sealed class PreventAllDamageToCreatureShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    private readonly Creature _protected;

    public PreventAllDamageToCreatureShield(Creature protectedCreature)
    {
        _protected = protectedCreature ?? throw new ArgumentNullException(nameof(protectedCreature));
    }

    /// <summary>The creature this shield protects. Exposed for tests.</summary>
    public Creature Protected => _protected;

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;
        return ReferenceEquals(intent.TargetCreature, _protected);
    }

    // CR 615.1 — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
