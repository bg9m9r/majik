using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.11 / CR 514.2 — a "this player has hexproof until end of turn" grant
/// for a fixed set of players, with no permanent source. Backs Surge of
/// Salvation's "You ... gain hexproof until end of turn" rider (and any future
/// instant/sorcery that grants player-hexproof for the turn).
///
/// Unlike <see cref="PlayerHexproofEffect"/> — whose lifetime is gated on a
/// battlefield source via <see cref="CardMovedEvent"/> — this grant has no
/// source permanent: it is registered on the controller's
/// <see cref="ContinuousEffectsService"/> at resolution and torn down by the
/// cleanup-step <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> sweep
/// (CR 514.2). The grant is wired into <see cref="PlayerStaticAbilities"/>
/// immediately on construction (so the same priority window sees the
/// hexproof — CR 117.5) and removed in <see cref="OnExpired"/>.
///
/// The effect carries no creature-characteristic contribution
/// (<see cref="Apply(CreatureCharacteristics)"/> is a no-op); it exists purely
/// so the layers service owns its end-of-turn teardown alongside every other
/// "until end of turn" effect.
/// </summary>
public sealed class PlayerHexproofUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly object _token = new();

    public PlayerHexproofUntilEndOfTurnEffect(IEnumerable<Player> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        foreach (var p in players)
        {
            if (p == null) continue;
            PlayerStaticAbilities.AddHexproof(_token, p);
        }
    }

    public override Layer Layer => Layer.Abilities;

    public override bool ExpiresAtEndOfTurn => true;

    // No creature-characteristic contribution — the grant lives in the
    // player-static registry, not on any creature's characteristics.
    public override bool AppliesTo(Creature creature) => false;

    public override void Apply(CreatureCharacteristics chars) { /* no-op */ }

    /// <summary>CR 514.2 — remove the player-hexproof grant at cleanup.</summary>
    public override void OnExpired() => PlayerStaticAbilities.RemoveHexproof(_token);
}
