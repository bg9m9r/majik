using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Engineered Explosives (Fifth Dawn, reprinted in
/// Modern Horizons). Artifact — {X}. Oracle text:
///
///   "Sunburst. (This permanent enters with a +1/+1 or charge counter on
///    it for each color of mana spent to cast it. If it's a creature,
///    use +1/+1 counters. Otherwise, use charge counters.)"
///   "{2}, Sacrifice Engineered Explosives: Destroy each nonland
///    permanent with mana value equal to the number of charge counters
///    on Engineered Explosives."
///
/// ## Implemented (v1)
/// - Artifact {X} with owner/controller wired.
/// - <b>Sunburst ETB (CR 702.43, approximated)</b>: Engineered
///   Explosives is a non-creature artifact, so Sunburst yields charge
///   counters (CR 702.43a). The engine has no per-cast mana-provenance
///   ledger yet — colors-spent is not tracked. v1 approximation: the
///   factory accepts a <c>Func&lt;int&gt; xValueProvider</c> that
///   returns the number of charge counters to apply when the ETB
///   effect fires. Callers wire this to whatever signal they have for
///   "X" or "colors spent" (in practice the cast-time printed X value
///   is the upper bound on colors spent for {X} artifacts). The
///   single-arg dispatcher path returns 0 — Engineered Explosives
///   enters with no counters in that shape, matching the "cast for
///   X=0 with no colored mana" line.
/// - <b>Activated ability (CR 602.1)</b>: {2}, Sacrifice this:
///   destroy each nonland permanent across every battlefield whose
///   mana value equals the charge counter count on Engineered
///   Explosives. The mana-cost component is declared on the ability;
///   the sacrifice cost is declared via
///   <see cref="AdditionalCost.Sacrifice"/> (currently a no-op stub —
///   see <see cref="AdditionalCost"/> / Mishra's Bauble), so the
///   effect itself moves Engineered Explosives to its owner's
///   graveyard so test-visible behavior matches CR 701.16. The
///   battlefield scan iterates the resolver-supplied player list; the
///   single-arg dispatcher path scans only the controller's
///   battlefield.
///
/// ## Deferred (v1 gaps)
/// - <b>Sunburst colour-provenance</b>: the engine does not track
///   which colours of mana paid a spell's cost. Use the
///   <see cref="Create(Player, Func{int}?, Func{IReadOnlyList{Player}}?)"/>
///   overload to supply an X-value provider; the printed cast-time X
///   is the natural upper bound when no colour ledger exists.
/// - <b>"Each nonland permanent" target/regenerate prompts</b>: v1
///   destroys every nonland permanent unconditionally (CR 701.7b —
///   "can't be regenerated" rider is implicit at v1 since there's no
///   regenerate prompt).
/// - <b>Sacrifice payment side effects</b>: same stub status as
///   Mishra's Bauble — the effect closure performs the zone move so
///   behavior is observable. Once <see cref="AdditionalCost.Pay"/>
///   performs the sacrifice itself the explicit move-to-graveyard can
///   be removed.
/// </summary>
[CardName("Engineered Explosives")]
public static class EngineeredExplosivesFactory
{
    /// <summary>
    /// Construct Engineered Explosives with no live runtime wiring.
    /// Sunburst ETB applies zero counters (single-arg path can't know X);
    /// the activated ability scans only the controller's battlefield.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, xValueProvider: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Engineered Explosives. When <paramref name="xValueProvider"/>
    /// is supplied, the Sunburst ETB effect adds that many charge counters
    /// when executed (callers wire this to the cast-time X value or a
    /// colour-provenance ledger). When <paramref name="allPlayersResolver"/>
    /// is supplied, the activated ability scans every player's battlefield
    /// for nonland permanents with mv = charge count; otherwise only the
    /// controller's battlefield is scanned.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<int>? xValueProvider,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact("Engineered Explosives", "{X}");
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sunburst ETB (CR 702.43). Engineered Explosives is a non-creature
        // permanent → charge counters (CR 702.43a). v1: rely on the caller-
        // supplied X provider; default to 0 when none was wired (shape-only
        // path) — matches "cast with X=0, no coloured mana spent".
        // The ETB effect is exposed as a TriggeredAbility so call sites can
        // surface it for shape inspection; live ETB-trigger registration is
        // out of scope for v1 (the printed effect is "enters with N
        // counters", which is a replacement-style application — engine has
        // no enters-with-counters primitive yet, see Walking Ballista's
        // matching deferral).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Engineered Explosives: enter with X charge counters (Sunburst)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var x = xValueProvider?.Invoke() ?? 0;
                if (x <= 0) return;
                card.Counters.Add(CounterType.Charge, x);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability (CR 602.1): {2}, Sacrifice this: destroy each
        // nonland permanent with mv = charge count.
        // - Mana cost: {2} → ManaCostCost.
        // - Sacrifice cost: AdditionalCost.Sacrifice(card). Payment is a
        //   no-op stub at the engine level (see Mishra's Bauble); the
        //   effect closure performs the zone move so visible state matches
        //   CR 701.16.
        // - Effect: iterate every battlefield (resolver-supplied; falls
        //   back to controller-only when no resolver), destroy nonland
        //   permanents whose mana value equals the charge count. Tokens
        //   and copies are caught by the same predicate (token mv is its
        //   printed mana cost's converted value — CR 110.5a).
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            "Engineered Explosives: destroy each nonland permanent with mv = charge counters",
            () =>
            {
                var target = card.Counters.Count(CounterType.Charge);

                // Sacrifice payment is a no-op stub — move EE to graveyard
                // here so SBAs + visible state line up.
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    owner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    // Snapshot — we mutate the battlefield list inside the
                    // loop. ICard exposes HasType + Zone, but ManaCostValue
                    // lives on the concrete Card; cast through OfType<Card>
                    // (every battlefield resident in practice derives from
                    // Card — same approach UpTheBeanstalk takes).
                    var victims = p.Zones.Battlefield.GetCards()
                        .OfType<Card>()
                        .Where(c => !c.HasType(CardType.Land))
                        .Where(c => c.ManaCostValue.TotalValue == target)
                        .ToList();

                    foreach (var v in victims)
                    {
                        p.Zones.Battlefield.RemoveCard(v);
                        p.Zones.Graveyard.AddCard(v);
                        v.SetZone(ZoneType.Graveyard);
                    }
                }
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sweepEffect });

        card.AddAbility(ability);

        return card;
    }
}
