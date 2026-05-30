using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ratchet Bomb. Artifact, {2}.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "{T}: Put a charge counter on this artifact.
///    {T}, Sacrifice this artifact: Destroy each nonland permanent with
///    mana value equal to the number of charge counters on this artifact."
///
/// Analogue: <see cref="BlastZoneFactory"/> — same charge-counter accrual +
/// "{T}, Sacrifice: destroy each nonland permanent with mv = charge counters"
/// sweep. Ratchet Bomb drops Blast Zone's land mana ability, its ETB
/// charge-counter trigger, and the {X}{X} charge-counter activation; instead
/// it accrues exactly one counter per {T} activation.
///
/// ## Implemented (v1)
/// - Plain Artifact identity ({2}, no supertype, no printed subtype).
/// - <b>{T}: Put a charge counter on this artifact</b> —
///   <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="AdditionalCost.Tap"/>. The effect adds one
///   <see cref="CounterType.Charge"/> counter (CR 122.1 / 606). Not a mana
///   ability — it uses the stack (CR 605.1a excludes it: it doesn't add
///   mana).
/// - <b>{T}, Sacrifice this artifact: Destroy each nonland permanent with
///   mv = charge counters</b> — <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>.
///   Mirrors the Blast Zone / Pernicious Deed sweep shape:
///   <list type="bullet">
///     <item>The charge count is snapshotted BEFORE the sacrifice so the
///       sweep target is correct — once Ratchet Bomb leaves the battlefield
///       its counters cease to exist (CR 121.2).</item>
///     <item>Sacrifice payment is a no-op stub at the engine cost level
///       (same posture as Blast Zone / Engineered Explosives / Pernicious
///       Deed); the effect closure performs the zone move so visible state
///       matches CR 701.16.</item>
///     <item>Sweep iterates every battlefield supplied by the
///       <paramref name="allPlayersResolver"/> (falls back to the
///       controller-only path when null). For each card on the battlefield:
///       if it is not a Land and its mana value equals the charge-counter
///       count (snapshotted before the sac), destroy it — move it to its
///       owner's graveyard (CR 701.7b).</item>
///   </list>
///
/// ## Deferred (v1 gaps — identical posture to Blast Zone)
/// - <b>"Can't be regenerated" rider</b>: implicit — no regenerate prompt
///   exists. (Ratchet Bomb's printed text carries no regeneration rider
///   anyway; destruction is a plain CR 701.7 move here.)
/// - <b>Multi-battlefield scan</b>: <paramref name="allPlayersResolver"/>
///   drives scope. Null -> owner-only.
/// - <b>Sacrifice cost as a real cost</b>: stubbed in the effect closure,
///   same as Blast Zone.
/// </summary>
[CardName("Ratchet Bomb")]
public static class RatchetBombFactory
{
    public const string CardName = "Ratchet Bomb";
    private const string ManaCost = "{2}";

    /// <summary>
    /// Construct Ratchet Bomb with no live runtime wiring; the sweep scans
    /// only the controller's battlefield. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Ratchet Bomb. When <paramref name="allPlayersResolver"/> is
    /// supplied, the sweep scans every player's battlefield for nonland
    /// permanents with mv = charge counters; otherwise only the controller's
    /// battlefield is scanned.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bomb = new Artifact(CardName, ManaCost, supertypes: null, subtypes: null);
        bomb.SetOwner(owner);
        bomb.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Put a charge counter on this artifact.
        // CR 602 — ordinary activated ability. Cost = tap. Adds one charge
        // counter (CR 122.1). Not a mana ability (it produces no mana), so
        // it uses the stack.
        // ----------------------------------------------------------------
        var chargeEffect = new Effect(
            $"{CardName}: put a charge counter ({{T}})",
            () =>
            {
                if (bomb.Zone != ZoneType.Battlefield) return;
                bomb.Counters.Add(CounterType.Charge, 1);
            });

        bomb.AddAbility(new ActivatedAbility(
            source: bomb,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(bomb),
            },
            effects: new IEffect[] { chargeEffect }));

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact: Destroy each nonland permanent with
        // mana value equal to the number of charge counters on this
        // artifact.
        //
        // CR 602. No "activate only as a sorcery" rider — instant speed per
        // oracle. Mirrors Blast Zone / Pernicious Deed sweep shape:
        // - Snapshot the charge count BEFORE moving Ratchet Bomb to the
        //   graveyard so the sweep target is correct (CR 121.2 — counters
        //   cease to exist on zone change).
        // - Sacrifice cost is a no-op stub at AdditionalCost.Pay; the effect
        //   closure moves Ratchet Bomb to its owner's graveyard (CR 701.16).
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: destroy each nonland permanent with mv = charge counters",
            () =>
            {
                // Snapshot before the sacrifice — once Ratchet Bomb is in
                // the graveyard its Counters bag is gone (CR 121.2).
                var target = bomb.Counters.Count(CounterType.Charge);

                // Sacrifice payment is a no-op stub at the engine level —
                // move Ratchet Bomb to its owner's graveyard here so SBAs +
                // visible state line up with CR 701.16.
                if (bomb.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(bomb);
                    owner.Zones.Graveyard.AddCard(bomb);
                    bomb.SetZone(ZoneType.Graveyard);
                }

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    // Snapshot — we mutate the battlefield list inside the
                    // loop. Mirror Blast Zone / Pernicious Deed's pattern.
                    var victims = p.Zones.Battlefield.GetCards()
                        .OfType<Card>()
                        .Where(c => !c.HasType(CardType.Land))
                        .Where(c => c.ManaCostValue.TotalValue == target)
                        .ToList();

                    foreach (var v in victims)
                    {
                        // Owner-routed graveyard move (CR 701.7b — destroyed
                        // permanents go to their owner's graveyard). Falls
                        // back to the iterated player when Owner is null so
                        // shape-only tests with untyped controllers still
                        // surface the destruction visibly.
                        var victimOwner = v.Owner ?? p;
                        p.Zones.Battlefield.RemoveCard(v);
                        victimOwner.Zones.Graveyard.AddCard(v);
                        v.SetZone(ZoneType.Graveyard);
                    }
                }
            });

        bomb.AddAbility(new ActivatedAbility(
            source: bomb,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(bomb),
                AdditionalCost.Sacrifice(bomb),
            },
            effects: new IEffect[] { sweepEffect }));

        return bomb;
    }
}
