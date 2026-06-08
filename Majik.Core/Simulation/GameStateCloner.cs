using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Simulation;

/// <summary>
/// Deep-clones live game runtime state for sandbox simulation. Static card
/// DEFINITION data (oracle text, abilities, base mana cost, types) is shared
/// by reference — only per-game runtime state is copied. Two passes:
///   1. value-clone players + every card they own (no cross-references yet);
///   2. re-link reference fields (controller, attachments, stack targets)
///      through the InstanceId / player remap tables.
/// </summary>
public static class GameStateCloner
{
    /// <summary>All ZoneType values — used to walk every zone on each player.</summary>
    private static readonly ZoneType[] AllZoneTypes = (ZoneType[])Enum.GetValues(typeof(ZoneType));

    /// <summary>
    /// Clone players only (Tasks 1–5 overload). Delegates to the full overload
    /// with null stack and turn-state so existing callers are unaffected.
    /// </summary>
    public static ClonedGame Clone(IReadOnlyList<Player> players)
        => Clone(players, liveStack: null, liveTurnState: null);

    /// <summary>
    /// Clone players, optionally the live stack, and optionally the live
    /// per-turn tally.
    ///
    /// <para><b>Stack cloning scope:</b> only <see cref="Majik.Core.Spells.Spell"/>
    /// stack objects are cloned.  <see cref="Majik.Core.Abilities.ActivatedAbility"/>
    /// and <see cref="Majik.Core.Abilities.TriggeredAbility"/> carry effect closures
    /// that captured the original game state and cannot be safely remapped —
    /// those objects are dropped from the cloned stack. See the escalation note
    /// in the task spec for rationale.</para>
    ///
    /// <para><b>TurnState Guid keys:</b> <see cref="Player.Id"/> is NOT preserved
    /// by <see cref="Player.CloneEmpty"/> (the clone gets a fresh Guid); therefore
    /// all Guid-keyed dictionaries in TurnState are remapped via playerMap so the
    /// cloned TurnState is keyed on the clone players' Ids.</para>
    /// </summary>
    public static ClonedGame Clone(
        IReadOnlyList<Player> players,
        Majik.Core.Stack.Stack? liveStack = null,
        TurnState? liveTurnState = null)
    {
        var playerMap = new Dictionary<Player, Player>();
        var cardMap = new Dictionary<Guid, ICard>();
        var originalById = new Dictionary<Guid, Card>();   // InstanceId → original Card (for Pass 2c)

        // Pass 1: empty player shells (life/name copied; zones empty).
        foreach (var p in players)
        {
            var clone = p.CloneEmpty();
            playerMap[p] = clone;
        }

        // Pass 2a: clone cards into zones, preserving InstanceId and order.
        // Build originalById in parallel so Pass 2c can look up originals cheaply.
        foreach (var p in players)
        {
            var clonePlayer = playerMap[p];
            foreach (var zoneType in AllZoneTypes)
            {
                foreach (var card in p.Zones.GetZone(zoneType).GetCards())
                {
                    var src = (Card)card;
                    originalById[src.InstanceId] = src;
                    var cc = src.CloneForSim();
                    cardMap[cc.InstanceId] = cc;
                    clonePlayer.Zones.GetZone(zoneType).AddCard(cc);
                }
            }
        }

        // Pass 2c: re-link reference fields (Owner, Controller, AttachedTo,
        // _attachments) through the remap tables — so every reference on a
        // cloned object points at the CLONE, never the original.
        foreach (var (instanceId, clonedCard) in cardMap)
        {
            var src = originalById[instanceId];
            ((Card)clonedCard).RelinkReferences(src, cardMap, playerMap);
        }

        // Pass 3 (optional): clone the stack (Spell objects only).
        Majik.Core.Stack.Stack? clonedStack = null;
        if (liveStack != null)
            clonedStack = Majik.Core.Stack.Stack.CloneFrom(liveStack, cardMap, playerMap);

        // Pass 4 (optional): clone the per-turn tally, remapping Guid keys.
        TurnState? clonedTurnState = null;
        if (liveTurnState != null)
        {
            clonedTurnState = new TurnState();
            clonedTurnState.CopyFrom(liveTurnState, cardMap, playerMap);
        }

        // Pass 5: re-register sim-cloneable continuous effects on the clone.
        // Anthems / lords (LordStaticEffect) must re-apply on the cloned battlefield
        // so P/T reads in the search sandbox are correct (CR 613).
        //
        // Algorithm:
        //   a) Find the live ContinuousEffectsService by scanning the original
        //      players' battlefields for any permanent with ActiveEffects != null.
        //      All battlefield permanents share one CES in production, so the
        //      first hit gives us the shared instance.
        //   b) If none found (no live CES), skip — every cloned permanent stays
        //      with ActiveEffects == null and returns base P/T, which is correct
        //      for boards that never had a CES wired.
        //   c) Build a fresh ContinuousEffectsService for the sandbox.
        //   d) For each live effect whose Source is a battlefield permanent that
        //      was cloned (InstanceId is in cardMap) AND IsActive() (gates out
        //      inactive manland / transient effects), call effect.CloneForSim(...)
        //      and register the non-null result on the fresh CES.
        //      Effects whose CloneForSim returns null (bespoke long-tail not yet
        //      ported) are silently skipped — eval loses those effects temporarily.
        //   e) Assign the fresh CES to EVERY cloned battlefield permanent so
        //      non-source permanents also consult it for incoming buffs.
        //      Register() already wires the source permanent's ActiveEffects;
        //      the explicit assignment below covers the rest.
        var clonedPlayers = players.Select(p => playerMap[p]).ToList();
        ContinuousEffectsService? freshCes = null;

        ContinuousEffectsService? FindLiveCes()
        {
            foreach (var p in players)
            {
                foreach (var card in p.Zones.GetZone(ZoneType.Battlefield).GetCards())
                {
                    if (card is Permanent perm && perm.ActiveEffects != null)
                        return perm.ActiveEffects;
                }
            }
            return null;
        }

        var liveCes = FindLiveCes();
        if (liveCes != null)
        {
            freshCes = new ContinuousEffectsService();

            // clonedPlayersProvider lazy lambda — available to CloneForSim overrides
            // that need a whole-roster resolver (e.g. future allPlayers-variant lords).
            IReadOnlyList<Player> ClonedPlayersProvider() => clonedPlayers;

            foreach (var liveEffect in liveCes.RegisteredEffects)
            {
                // Gate 1: effect must have a source permanent that was cloned
                // (i.e. existed on the original battlefield).
                if (liveEffect.Source is not Permanent liveSrc) continue;
                if (!cardMap.TryGetValue(liveSrc.InstanceId, out var clonedCard)) continue;
                if (clonedCard is not Permanent clonedSrc) continue;

                // Gate 2: effect must currently be active (source on battlefield,
                // duration not expired).  This screens out manland effects that
                // registered while animated but since de-animated, transient "until
                // end of turn" effects, etc.
                if (!liveEffect.IsActive()) continue;

                // Delegate reconstruction to the effect itself.  Returns null for
                // bespoke effect classes not yet ported (acceptable — they return the
                // base-class default null and are silently skipped).
                var cloned2 = liveEffect.CloneForSim(clonedSrc, ClonedPlayersProvider);
                if (cloned2 != null)
                    freshCes.Register(cloned2);
            }

            // Assign the fresh CES to every cloned battlefield permanent (including
            // non-source permanents that receive incoming buffs from the lord).
            // Register() already wired ActiveEffects on each source permanent
            // (via the "if null" branch in Register); the loop below covers the
            // remaining permanents that are targets-only (no registered effect of
            // their own).
            foreach (var cp in clonedPlayers)
            {
                foreach (var card in cp.Zones.GetZone(ZoneType.Battlefield).GetCards())
                {
                    if (card is Permanent cperm)
                        cperm.ActiveEffects = freshCes;
                }
            }
        }

        return new ClonedGame
        {
            Players = clonedPlayers,
            PlayerMap = playerMap,
            CardMap = cardMap,
            Stack = clonedStack,
            TurnState = clonedTurnState,
            Effects = freshCes,
        };
    }
}
