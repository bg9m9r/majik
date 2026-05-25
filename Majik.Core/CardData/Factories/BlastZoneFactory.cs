using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blast Zone (War of the Spark, reprinted in
/// Commander Masters). Land.
///
/// Oracle text (current Comp Rules printing — verified Scryfall
/// 2026-05-24):
///   "This land enters with a charge counter on it.
///    {T}: Add {C}.
///    {X}{X}, {T}: Put X charge counters on this land.
///    {3}, {T}, Sacrifice this land: Destroy each nonland permanent with
///    mana value equal to the number of charge counters on this land."
///
/// (The original WAR printing had "enters with a charge counter on it for
/// each mana spent to cast it" + a {3}, {T}, Sacrifice sweep. The card
/// was errata'd to today's static-1-counter ETB + dedicated {X}{X}, {T}
/// charge-counter activation so the ETB doesn't need a per-cast mana
/// ledger.)
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>ETB triggered ability (CR 603.6a / CR 614.1d simulated)</b> —
///   "this land enters with a charge counter on it" is modelled as an
///   ETB <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   filtered to (self, ToZone=Battlefield) via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. The effect adds one
///   <see cref="CounterType.Charge"/> counter. v1 deviation: a true
///   "enters with N counters" replacement (CR 614.1d) would set the
///   counter as part of the ETB move so triggers like Hardened Scales
///   could see it pre-entry, but the engine's
///   <see cref="Effects.EntersWithCountersReplacement"/> only handles
///   +1/+1 counters today — the trigger-shape mirrors Engineered
///   Explosives' Sunburst path (same posture).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack). {C} is bucketed as +1 generic in
///   <see cref="ValueObjects.ManaCost"/> today.
/// - <b>{X}{X}, {T}: Put X charge counters on this land</b> —
///   <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>
///   <c>{X}{X}</c> and an <see cref="AdditionalCost.Tap"/>. Engine has
///   no live X-payment ledger; the caller supplies an
///   <c>xValueProvider</c> sampled at resolution to determine how many
///   charge counters to add. Defaults to 0 in the shape-only path
///   (matches "activate for X=0" — pay {0}{0}{T} for no counters; legal
///   but useless).
/// - <b>{3}, {T}, Sacrifice: destroy each nonland permanent with mv =
///   charge counters</b> — <see cref="ActivatedAbility"/> with mana cost
///   <c>{3}</c>, <see cref="AdditionalCost.Tap"/>, and
///   <see cref="AdditionalCost.Sacrifice"/>. Mirrors the Engineered
///   Explosives / Pernicious Deed sweep shape:
///   <list type="bullet">
///     <item>Sacrifice payment is a no-op stub at the engine level (same
///       posture as Engineered Explosives / Pernicious Deed / Mishra's
///       Bauble); the effect closure performs the zone move so visible
///       state matches CR 701.16.</item>
///     <item>Sweep iterates every battlefield supplied by the
///       <paramref name="allPlayersResolver"/> (falls back to the
///       controller-only path when null). For each card on the
///       battlefield: if it is not a Land and its mana value equals the
///       charge counter count on Blast Zone (sampled BEFORE the sac so
///       the count is correct), destroy it (move to its owner's
///       graveyard).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed gate on the sweep</b>: the printed oracle says
///   "Activate only as a sorcery." Today the
///   <see cref="ActivatedAbility(object, Player, IEnumerable{ITarget}?, IEnumerable{ICost}?, IEnumerable{IEffect}?, IEnumerable{TargetRequest}?, bool)"/>
///   constructor accepts a <c>sorcerySpeed</c> flag — passed as
///   <c>true</c> here so
///   <see cref="Rules.ActionValidator"/> rejects out-of-phase activations
///   (CR 117.1a / 307.5).
/// - <b>Charge-counter activation X-value provenance</b>: same gap as
///   Engineered Explosives' Sunburst — no per-activation X ledger. The
///   v1 approximation accepts a caller-supplied
///   <c>chargeXValueProvider</c>; production callers wire this from the
///   cast / activation context.
/// - <b>"Can't be regenerated" rider</b>: implicit at v1 — no regenerate
///   prompt exists.
/// - <b>Multi-battlefield scan</b>: same shape as Pernicious Deed —
///   <paramref name="allPlayersResolver"/> drives scope. Null → owner-only.
/// </summary>
[CardName("Blast Zone")]
public static class BlastZoneFactory
{
    public const string CardName = "Blast Zone";

    /// <summary>
    /// Construct Blast Zone with no live runtime wiring. The ETB charge-
    /// counter trigger is attached for shape observability; the
    /// charge-counter activation resolves with X = 0; the sweep scans
    /// only the controller's battlefield. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, chargeXValueProvider: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Blast Zone. When <paramref name="chargeXValueProvider"/>
    /// is supplied, the {X}{X}, {T} activated ability adds that many
    /// charge counters at resolution (callers wire this to the
    /// activation-time X value). When <paramref name="allPlayersResolver"/>
    /// is supplied, the sweep scans every player's battlefield for
    /// nonland permanents with mv = charge counters; otherwise only the
    /// controller's battlefield is scanned.
    /// </summary>
    public static Land Create(
        Player owner,
        Func<int>? chargeXValueProvider,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB: "This land enters with a charge counter on it."
        // CR 614.1d in spirit; modelled as an ETB triggered ability on
        // self (same posture as Engineered Explosives' Sunburst — see
        // EntersWithCountersReplacement's +1/+1-only limitation).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with a charge counter",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                land.Counters.Add(CounterType.Charge, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {X}{X}, {T}: Put X charge counters on this land.
        // CR 602 — ordinary activated ability. Cost = {X}{X} + tap.
        // No "activate only as a sorcery" rider — instant speed per
        // oracle.
        // ----------------------------------------------------------------
        var chargeEffect = new Effect(
            $"{CardName}: add X charge counters ({{X}}{{X}}, {{T}})",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                var x = chargeXValueProvider?.Invoke() ?? 0;
                if (x <= 0) return;
                land.Counters.Add(CounterType.Charge, x);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{X}{X}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { chargeEffect }));

        // ----------------------------------------------------------------
        // {3}, {T}, Sacrifice this land: Destroy each nonland permanent
        // with mana value equal to the number of charge counters on this
        // land. Activate only as a sorcery.
        //
        // CR 602 + CR 117.1a / 307.5 (sorcery-speed rider).
        // Mirrors Engineered Explosives / Pernicious Deed sweep shape:
        // - Snapshot the charge count BEFORE moving Blast Zone to the
        //   graveyard so the sweep target is correct.
        // - Sacrifice cost is a no-op stub at AdditionalCost.Pay; the
        //   effect closure moves Blast Zone to its owner's graveyard.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: destroy each nonland permanent with mv = charge counters",
            () =>
            {
                // Snapshot the charge count BEFORE the sacrifice — once
                // Blast Zone is in the graveyard its Counters bag is
                // gone-on-paper (counters cease to exist on zone change,
                // CR 121.2). We pay the cost effectively before reading,
                // mirroring Engineered Explosives' "sample target then
                // sac" order.
                var target = land.Counters.Count(CounterType.Charge);

                // Sacrifice payment is a no-op stub at the engine level —
                // move Blast Zone to its owner's graveyard here so SBAs +
                // visible state line up with CR 701.16.
                if (land.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(land);
                    owner.Zones.Graveyard.AddCard(land);
                    land.SetZone(ZoneType.Graveyard);
                }

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    // Snapshot — we mutate the battlefield list inside
                    // the loop. Mirror Pernicious Deed's pattern.
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

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { sweepEffect },
            sorcerySpeed: true));

        return land;
    }
}
