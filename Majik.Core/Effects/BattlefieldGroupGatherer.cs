using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 611.2c / 109.5 — reusable candidate gatherer for a Layer-6 group
/// ability-grant (<see cref="GrantAbilityToGroupStaticEffect"/> /
/// <see cref="GrantAbilityToGroupLifecycle"/>). Enumerates EVERY permanent on
/// the battlefield across ALL players' battlefield zones, so the group static's
/// <c>scope</c> filter can select members by their EFFECTIVE controller
/// (<see cref="Permanent.Controller"/>) rather than by which player's
/// battlefield zone physically holds the card.
///
/// <para><b>Why this exists (controlled-but-not-owned).</b> A control-change
/// effect (Threaten / Mindslaver / Act of Treason / Persuasion) swaps
/// <see cref="Permanent.Controller"/> via <see cref="Card.ChangeController"/>
/// but does NOT move the card between zone collections — a stolen permanent
/// stays in its OWNER's <see cref="ZoneManager.GetZone"/> Battlefield collection
/// while its <see cref="Permanent.Controller"/> points at the thief (CR 110.2,
/// 700.6). A gatherer that walks only <c>controller.Zones.Battlefield</c>
/// therefore MISSES a permanent the source's controller controls but does not
/// own, and SPURIOUSLY enumerates a permanent the source's controller owns but
/// no longer controls. Both are wrong for a controller-scoped static such as
/// "Lands you control have …" (Chromatic Lantern) or "Creatures you control
/// have …" (Serra's Emissary).</para>
///
/// <para>The fix is to make the candidate set the WHOLE battlefield (every
/// owner's Battlefield zone, deduped) and let the static's <c>scope</c>
/// predicate — which already tests <c>ReferenceEquals(p.Controller,
/// source.Controller)</c> — pick out the effective-controller group. A
/// symmetric static ("All artifacts …", Kataki) simply uses a controller-blind
/// scope over the same whole-board set.</para>
///
/// <para>Membership is recomputed live on every reconcile (the providers
/// returned here are re-evaluated each <see cref="GrantAbilityToGroupStaticEffect.Sync"/>),
/// so permanents entering / leaving / changing control are picked up
/// (CR 611.2c). Only permanents currently in the Battlefield
/// <see cref="ZoneType"/> are returned; a permanent that has left play but
/// lingers in a stale zone collection is excluded.</para>
/// </summary>
public static class BattlefieldGroupGatherer
{
    /// <summary>
    /// All permanents currently on the battlefield across every player in
    /// <paramref name="players"/>. Players are de-duplicated by reference so a
    /// caller may safely pass a list that repeats a player. A null player or a
    /// null card is skipped.
    /// </summary>
    public static IEnumerable<Permanent> AllBattlefieldPermanents(IEnumerable<Player>? players)
    {
        if (players == null) yield break;

        var seenPlayers = new HashSet<Player>(ReferenceEqualityComparer.Instance);
        foreach (var player in players)
        {
            if (player == null) continue;
            if (!seenPlayers.Add(player)) continue;

            foreach (var card in player.Zones.Battlefield.GetCards())
            {
                if (card is Permanent permanent && permanent.Zone == ZoneType.Battlefield)
                {
                    yield return permanent;
                }
            }
        }
    }

    /// <summary>
    /// Build a live membership provider over the whole battlefield for a group
    /// ability-grant. The returned delegate re-reads every player's Battlefield
    /// zone on each call so a permanent that entered, left, or changed control
    /// since the last reconcile is reflected (CR 611.2c).
    ///
    /// <para><paramref name="playersProvider"/> returns the game's players. It
    /// is a provider (not a captured snapshot) so the gatherer stays correct if
    /// the player set is materialised lazily; in practice it returns the
    /// <c>Game.Players</c> list, which is fixed for the game's duration.</para>
    ///
    /// <para>The caller's group static supplies the <c>scope</c> predicate that
    /// narrows this whole-board set to the intended group — e.g.
    /// <c>p =&gt; p is Land &amp;&amp; ReferenceEquals(p.Controller,
    /// source.Controller)</c> for "lands you control", picking up a stolen land
    /// the source's controller controls but an opponent owns.</para>
    /// </summary>
    public static Func<IEnumerable<Permanent>> WholeBattlefield(
        Func<IEnumerable<Player>?> playersProvider)
    {
        ArgumentNullException.ThrowIfNull(playersProvider);
        return () => AllBattlefieldPermanents(playersProvider());
    }
}
