using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oblivion Stone (Mirrodin, {3}).
///
/// Artifact. Oracle text:
///   "{4}, {T}: Put a fate counter on each nonland permanent.
///    {5}, {T}, Sacrifice Oblivion Stone: Destroy each nonland permanent
///    without a fate counter on it. Then remove all fate counters from
///    all permanents."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {3}, owner / controller).
/// - <b>{4}, {T}: fate counter sweep</b> — <see cref="ActivatedAbility"/>
///   with <see cref="ManaCostCost"/>("{4}") + <see cref="AdditionalCost"/>.Tap.
///   On resolution iterates every battlefield supplied by the
///   <paramref name="allPlayersResolver"/> (falls back to the controller-
///   only path when null) and adds one <see cref="CounterType.Fate"/>
///   counter to every nonland permanent (per CR 121 the counter is added
///   only if absent — but since the engine's
///   <see cref="CounterCollection.Add(CounterType, int)"/> stacks counts,
///   we guard with a "skip if already has one" check so re-activation
///   doesn't grow the count beyond one. The printed text reads as a
///   per-permanent marker not a stacking resource).
/// - <b>{5}, {T}, Sacrifice: destroy each nonland permanent without a
///   fate counter, then remove all fate counters</b> — second
///   <see cref="ActivatedAbility"/> mirroring the Blast Zone / Pernicious
///   Deed sweep shape:
///   <list type="bullet">
///     <item>Sacrifice payment is a no-op stub at the engine level
///       (same posture as Engineered Explosives / Pernicious Deed /
///       Mishra's Bauble); the effect closure performs the move to
///       graveyard so visible state matches CR 701.16. Self-sac happens
///       BEFORE the sweep so Oblivion Stone isn't counted among the
///       "nonland permanents without a fate counter".</item>
///     <item>Sweep iterates every battlefield supplied by the
///       <paramref name="allPlayersResolver"/> (falls back to the
///       controller-only path when null). Each nonland permanent without
///       a <see cref="CounterType.Fate"/> counter is destroyed (moved to
///       its owner's graveyard).</item>
///     <item>After the sweep, every remaining permanent on every
///       scanned battlefield has its fate counters cleared.</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Targeting validator</b>: neither activated ability targets, so
///   no <see cref="Targeting.TargetRequest"/> is declared.
/// - <b>Indestructible / regenerate</b>: the sweep is a raw
///   battlefield→graveyard move (mirrors Pernicious Deed / Blast Zone);
///   when the engine grows an "indestructible" gate the sweep effect
///   will need to consult it (CR 702.12).
/// - <b>Multi-battlefield scan</b>: same shape as Pernicious Deed —
///   <paramref name="allPlayersResolver"/> drives scope. Null →
///   owner-only.
/// - <b>"Each nonland permanent" includes Oblivion Stone itself for the
///   first ability</b>: the first ability puts a fate counter on every
///   nonland permanent including Oblivion Stone (CR 109.5 — "each
///   nonland permanent" is unfiltered). This is implemented as written;
///   the second ability's destroy-without-fate filter therefore spares
///   Oblivion Stone if both have been activated in sequence, but
///   Oblivion Stone is sacrificed as a cost of the second ability so
///   the practical effect on Oblivion Stone is unchanged.
/// </summary>
[CardName("Oblivion Stone")]
public static class OblivionStoneFactory
{
    public const string CardName = "Oblivion Stone";
    public const string Cost = "{3}";

    /// <summary>
    /// Construct Oblivion Stone with no live runtime wiring. Both
    /// activated abilities are attached for shape observability; the
    /// sweep / counter scans see only the controller's battlefield.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Oblivion Stone. When
    /// <paramref name="allPlayersResolver"/> is supplied, both abilities
    /// scan every player's battlefield; otherwise only the controller's
    /// battlefield is scanned.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var stone = new Artifact(CardName, Cost);
        stone.SetOwner(owner);
        stone.SetController(owner);

        // ----------------------------------------------------------------
        // {4}, {T}: Put a fate counter on each nonland permanent.
        // CR 602 — ordinary activated ability. Iterates every supplied
        // battlefield (owner-only fallback) and adds a Fate counter to
        // each nonland permanent that doesn't already have one.
        // ----------------------------------------------------------------
        var markEffect = new Effect(
            $"{CardName}: put a fate counter on each nonland permanent",
            () =>
            {
                if (stone.Zone != ZoneType.Battlefield) return;

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    foreach (var perm in p.Zones.Battlefield.GetCards()
                                 .OfType<Permanent>()
                                 .ToList())
                    {
                        if (perm.HasType(CardType.Land)) continue;
                        // CR 121 — counters are markers, not a stacking
                        // resource for this card. Skip if already marked.
                        if (perm.Counters.Count(CounterType.Fate) > 0) continue;
                        perm.Counters.Add(CounterType.Fate, 1);
                    }
                }
            });

        stone.AddAbility(new ActivatedAbility(
            source: stone,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
                AdditionalCost.Tap(stone),
            },
            effects: new IEffect[] { markEffect }));

        // ----------------------------------------------------------------
        // {5}, {T}, Sacrifice Oblivion Stone: Destroy each nonland
        // permanent without a fate counter on it. Then remove all fate
        // counters from all permanents.
        //
        // Mirrors Blast Zone / Pernicious Deed sweep shape:
        //   - Move Oblivion Stone to its owner's graveyard first (the
        //     generic AdditionalCost.Pay sacrifice path is a no-op stub).
        //   - Iterate every supplied battlefield, snapshotting the list
        //     before mutating it. Destroy each nonland permanent without
        //     a Fate counter.
        //   - After the destroy pass, clear all Fate counters from every
        //     remaining permanent.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: destroy each nonland permanent without a fate counter; remove all fate counters",
            () =>
            {
                // Self-sac stub — perform the zone move so visible state
                // matches CR 701.16 BEFORE the sweep scans the battlefield.
                if (stone.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(stone);
                    owner.Zones.Graveyard.AddCard(stone);
                    stone.SetZone(ZoneType.Graveyard);
                }

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                // Destroy pass — collect victims first, then mutate.
                foreach (var p in players)
                {
                    var victims = p.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(perm => !perm.HasType(CardType.Land))
                        .Where(perm => perm.Counters.Count(CounterType.Fate) == 0)
                        .ToList();

                    foreach (var v in victims)
                    {
                        var victimOwner = v.Owner ?? p;
                        p.Zones.Battlefield.RemoveCard(v);
                        victimOwner.Zones.Graveyard.AddCard(v);
                        v.SetZone(ZoneType.Graveyard);
                    }
                }

                // Counter-clear pass — every permanent on every scanned
                // battlefield drops its Fate counters.
                foreach (var p in players)
                {
                    foreach (var perm in p.Zones.Battlefield.GetCards()
                                 .OfType<Permanent>()
                                 .ToList())
                    {
                        var fate = perm.Counters.Count(CounterType.Fate);
                        if (fate > 0)
                        {
                            perm.Counters.Remove(CounterType.Fate, fate);
                        }
                    }
                }
            });

        stone.AddAbility(new ActivatedAbility(
            source: stone,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{5}"),
                AdditionalCost.Tap(stone),
                AdditionalCost.Sacrifice(stone),
            },
            effects: new IEffect[] { sweepEffect }));

        return stone;
    }
}
