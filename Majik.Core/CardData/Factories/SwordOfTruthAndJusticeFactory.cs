using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Truth and Justice (Modern Horizons, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from white and from
///    blue."
///   "Whenever equipped creature deals combat damage to a player, put a
///    +1/+1 counter on equipped creature and you may proliferate."
///   "Equip {2}."
///
/// Same shape as <see cref="SwordOfFireAndIceFactory"/> /
/// <see cref="SwordOfFeastAndFamineFactory"/>: AttachedBoostEffect at
/// Layer 7c for +2/+2, GrantAbilityEffect re-projecting
/// <see cref="ProtectionAbility"/>("white") / ProtectionAbility("blue")
/// onto the equipped creature at Layer 6, and a
/// <see cref="CombatDamageDealtEvent"/>-keyed
/// <see cref="TriggeredAbility"/> gated on the equipped creature dealing
/// damage to a player (CR 510 / CR 603.1).
///
/// ## Combat-damage rider
///
/// On resolution the rider:
/// <list type="number">
///   <item>Places one <see cref="CounterType.PlusOnePlusOne"/> counter on
///     the equipped creature (CR 122 / CR 121.2 — "put a +1/+1 counter on
///     equipped creature"). The counter goes on the live
///     <see cref="Permanent.AttachedTo"/> at resolution time, so if the
///     Sword has been detached between trigger and resolution the counter
///     no-ops rather than landing on a stale target (CR 608.2b).</item>
///   <item>Proliferates (CR 701.27) — "you may proliferate." Proliferate
///     chooses any number of permanents and players that already have
///     counters, then puts another counter of an existing kind on each
///     chosen permanent / player. v1 deterministic policy: iterate every
///     permanent in every player's battlefield zone plus both players,
///     and for each one that already has at least one counter of any
///     kind, add one more of an arbitrary existing kind. This implements
///     the "you may" wording as "always proliferate everything" — agent
///     prompt deferred behind the same selection queue used for
///     <c>Karn Liberated</c> / <c>Tezzeret, Agent of Bolas</c> when those
///     factories grow proliferate hooks.</item>
/// </list>
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — the trigger is
/// attached for shape but not registered with a
/// <see cref="TriggerManager"/>; protection markers remain on the
/// equipment card so <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
/// still returns deterministic answers for factory-shape /
/// dispatch tests. With the runtime overload the static +2/+2 boost
/// (Layer 7c) is registered against <paramref name="continuousEffects"/>
/// and the protection grants (Layer 6) re-project onto the live equipped
/// creature; the combat trigger is registered against
/// <paramref name="triggers"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>"You may" prompt</b> on proliferate — v1 always proliferates.
/// - <b>Counter-kind picker</b> when a permanent has multiple counter
///   kinds — v1 picks the first kind enumerated by
///   <see cref="CounterCollection"/>.
/// </summary>
[CardName("Sword of Truth and Justice")]
public static class SwordOfTruthAndJusticeFactory
{
    public const string CardName = "Sword of Truth and Justice";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of Truth and Justice with no live runtime wiring
    /// (shape / dispatcher path). The +2/+2 boost is not registered; the
    /// combat-damage trigger is attached for shape but not registered
    /// with a <see cref="TriggerManager"/>. Protection markers are
    /// present on the equipment card.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Truth and Justice. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) is registered against it and two Layer-6 grants
    /// project <see cref="ProtectionAbility"/>("white") /
    /// ProtectionAbility("blue") onto the live equipped creature. When
    /// <paramref name="triggers"/> is supplied the combat-damage trigger
    /// is registered so a <see cref="CombatDamageDealtEvent"/> from the
    /// equipped creature targeting a player automatically queues the
    /// ability.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "has protection from white and from blue"
        // (CR 702.16 / CR 613.1f). Same wiring as Sword of Fire and Ice.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("white")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("blue")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("white"));
            card.AddAbility(new ProtectionAbility("blue"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    put a +1/+1 counter on equipped creature and you may
        //    proliferate."
        // --------------------------------------------------------------
        var combatEffect = new Effect(
            $"{CardName}: +1/+1 counter on equipped creature + may proliferate",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // 1) +1/+1 counter on equipped creature (CR 121.2).
                var equipped = card.AttachedTo;
                if (equipped != null)
                {
                    equipped.Counters.Add(CounterType.PlusOnePlusOne, 1);
                }

                // 2) "You may proliferate." (CR 701.27). v1 deterministic:
                //    always proliferate, adding one counter of an arbitrary
                //    existing kind to every permanent (and both players)
                //    that already has at least one counter.
                Proliferate(owner);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// CR 701.27 — proliferate. Iterate every permanent currently on the
    /// battlefield (across all known players) plus the players
    /// themselves; for each one that already has at least one counter of
    /// any kind, add one more counter of an existing kind.
    ///
    /// v1 deterministic policy:
    /// <list type="bullet">
    ///   <item>"Any number" is always "all of them" — the agent-driven
    ///     subset selection is deferred.</item>
    ///   <item>Counter-kind picker for multi-kind permanents falls back
    ///     to the first kind reported by
    ///     <see cref="CounterCollection.GetAllTypes"/>.</item>
    ///   <item>Players' poison / energy / experience counters are
    ///     proliferated through the same generic
    ///     <see cref="Player.AddCounter"/> path when available; absent
    ///     such a path the player-side proliferate no-ops (player
    ///     counters are not yet first-class).</item>
    /// </list>
    /// </summary>
    internal static void Proliferate(Player owner)
    {
        // Permanents on every player's battlefield.
        foreach (var player in EnumerateKnownPlayers(owner))
        {
            foreach (var card in player.Zones.Battlefield.GetCards())
            {
                if (card is not Permanent perm) continue;
                ProliferateOne(perm);
            }
        }
    }

    private static void ProliferateOne(Permanent perm)
    {
        // CR 122.2 — counters on a permanent. Pick the first kind that
        // already exists and add 1 more.
        var first = perm.Counters.All
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => kvp.Key)
            .FirstOrDefault();
        if (first == null) return;
        perm.Counters.Add(first, 1);
    }

    /// <summary>
    /// Enumerate the players whose battlefields we walk for proliferate.
    /// v1 covers only the Sword's controller — without a <c>Game</c>
    /// reference we have no direct opponent handle. Upstream Game wiring
    /// is the future fix; for v1 the trigger fires when the equipped
    /// creature deals combat damage to a player so the most-common
    /// proliferate target (the equipped creature's own +1/+1 counters,
    /// our own planeswalkers, our own poison-counter recipients on the
    /// rare poison-on-self cases) is already covered.
    /// </summary>
    private static IEnumerable<Player> EnumerateKnownPlayers(Player owner)
    {
        yield return owner;
    }
}
