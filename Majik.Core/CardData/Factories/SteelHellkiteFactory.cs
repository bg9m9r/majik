using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steel Hellkite (Scars of Mirrodin, {6}).
///
/// Artifact Creature — Dragon 5/5. Oracle text (Scryfall):
///   "Flying.
///    {2}: This creature gets +1/+0 until end of turn.
///    {X}: Destroy each nonland permanent with mana value X whose
///    controller was dealt combat damage by this creature this turn.
///    Activate only once each turn."
///
/// ## Implemented (v1)
/// - 5/5 Artifact Creature — Dragon at {6} (multi-type via
///   <see cref="Card.AddCardType"/>, mirroring Esika's Chariot / Walking
///   Ballista).
/// - <b>Flying</b> (CR 702.9) wired as a <see cref="KeywordAbility"/>
///   marker; combat code reads it directly.
/// - <b>{2}: +1/+0 EOT</b>: vanilla <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of <c>{2}</c>; effect registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> on Steel Hellkite for +1/+0
///   via <see cref="Creature.ActiveEffects"/>. No sorcery-speed gate
///   (printed as instant-speed pump).
/// - <b>{X}: Destroy each nonland permanent with mv = X whose
///   controller was dealt combat damage by this creature this turn.
///   Activate only once each turn.</b>
///   <list type="bullet">
///     <item>Mana cost <c>{X}</c> via <see cref="ManaCostCost"/>; X reads the
///     per-activation ledger (<see cref="ResolutionContext.ChosenX"/>, GAP 2).
///     The <c>xValueProvider</c> closure is kept ONLY as a shape/unit-test
///     override.</item>
///     <item><b>Combat-damage-victim tracking (RE-SOURCE-SAFE)</b>: an
///     event-bus subscriber on <see cref="CombatDamageDealtEvent"/>
///     accumulates the <see cref="Player"/> controllers of every entity
///     dealt combat damage this turn, KEYED BY THE DAMAGE-SOURCE PERMANENT
///     (<c>Dictionary&lt;Permanent, HashSet&lt;Player&gt;&gt;</c>). Damage to
///     a creature / planeswalker contributes its controller; damage to a
///     player contributes that player. Keying by source — rather than gating
///     on a captured Steel Hellkite reference — is what makes the "damaged by
///     this creature this turn" linkage RE-SOURCEABLE: the {X} sweep is marked
///     <see cref="ActivatedAbility.RebindSafe"/> and reads its victim set off
///     its LIVE source (<see cref="ResolutionContext.Source"/>), so when
///     Agatha's Soul Cauldron re-homes it to a BEARER via
///     <see cref="ActivatedAbility.RebindTo"/> the sweep destroys permanents
///     whose controller the BEARER damaged (CR 707.2 / 613.1f), never the
///     exiled Steel Hellkite's stale linkage. The map is reset on
///     <see cref="TurnStartedEvent"/> (CR 700.5 — "this turn" memory ends at
///     the cleanup step; resetting on the next turn-start is observationally
///     identical and discards stale state across turn boundaries).</item>
///     <item><b>Sorcery-speed-like gate</b> ("Activate only during your
///     turn") via <see cref="ActivatedAbility"/>'s
///     <c>sorcerySpeed</c> flag — true here, so
///     <see cref="Rules.ActionValidator"/> rejects activations on
///     opponents' turns (CR 117.1a / 307.5). v1 caveat: this flag also
///     gates the activation to main-phase + empty-stack; the printed
///     "during your turn" rider is broader (any step of your turn). The
///     stricter v1 gate is observationally safer (no false-positive
///     activations) and matches the same posture Steel Hellkite-style
///     "during your turn" cards take in this repo until a dedicated
///     "any-phase your-turn-only" rider lands.</item>
///     <item>Sweep iterates every battlefield read from the live
///     <c>ctx.Game.AllPlayers</c> (falls back to controller-only when no live
///     game context) and destroys every <b>nonland</b> permanent (Scryfall
///     oracle "each nonland permanent" — land detection via
///     <see cref="Card.HasType"/>(<see cref="CardType.Land"/>)) whose
///     <see cref="Card.ManaCostValue"/>'s total equals X AND whose controller
///     is in the tracked victim set for the sweep's live source.</item>
///   </list>
///
/// ## Source-closure injection
/// - <c>xValueProvider</c>: sampled at activation resolution to determine
///   the destruction mv target. Same shape as
///   <see cref="EngineeredExplosivesFactory"/> / <see cref="BlastZoneFactory"/>.
/// - <c>allPlayersResolver</c>: drives the sweep scope. Same shape as
///   Engineered Explosives / Pernicious Deed / Blast Zone.
/// - <c>eventBus</c>: when supplied, the combat-damage-victim tracker
///   subscribes to <see cref="CombatDamageDealtEvent"/> (accumulate) and
///   <see cref="TurnStartedEvent"/> (clear).
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed vs "your-turn-only"</b>: see ability-2 comments.
///   The stricter gate is acceptable until the engine grows a separate
///   <c>YourTurnOnly</c> activation flag.
/// - <b>Pre-bus combat damage</b>: if the event bus is wired AFTER Steel
///   Hellkite has already dealt combat damage this turn, that prior
///   damage is not tracked. Production callers wire the bus at ETB time
///   (mirroring the <see cref="UmezawasJitteFactory"/> /
///   <see cref="BorosReckonerFactory"/> "subscribe-at-construction"
///   posture).
/// - <b>Multi-controller damage in one packet</b>: combat damage is
///   dealt per-(source, target) pair (CR 510.1c), so a single packet
///   only ever credits one controller. The tracking set is correct by
///   construction; no merge logic needed.
/// - <b>Lifetime of subscriptions</b>: when Steel Hellkite leaves the
///   battlefield (or zone-changes), the subscriptions remain attached
///   to the event bus. The handlers gate on the activation availability
///   check (off-battlefield → no activations fire), so stale handlers
///   are harmless but accumulate. Same posture as the per-instance
///   subscriptions in <see cref="OmnathLocusOfCreationFactory"/>.
/// </summary>
[CardName("Steel Hellkite")]
public static class SteelHellkiteFactory
{
    public const string CardName = "Steel Hellkite";
    public const string PrintedManaCost = "{6}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Steel Hellkite with no live runtime wiring. The pump
    /// activation is wired structurally; the destruction activation
    /// resolves with X = 0 and an empty victim set (so it destroys
    /// nothing). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner,
            xValueProvider: null,
            eventBus: null);

    /// <summary>
    /// Construct Steel Hellkite. When <paramref name="xValueProvider"/>
    /// is supplied, the {X} activation samples it at resolution. The sweep
    /// scans every player's battlefield read from the LIVE resolution context
    /// (<c>ctx.Game.AllPlayers</c>) at resolution — no captured player
    /// resolver, so it is correct on the production routed build (mirrors
    /// #2551); with no live game context it falls back to the controller's
    /// battlefield. When <paramref name="eventBus"/> is supplied, the
    /// combat-damage-victim tracker subscribes to
    /// <see cref="CombatDamageDealtEvent"/> (accumulate) and
    /// <see cref="TurnStartedEvent"/> (clear).
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<int>? xValueProvider,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dragon });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag Artifact
        // so HasType lookups + colour identity see both types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 Flying — keyword marker.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // {2}: Steel Hellkite gets +1/+0 until end of turn.
        // CR 602.1 — plain activated ability, instant speed.
        //
        // RE-SOURCE-SAFE (agatha-stale-body-rewrite-then-migrate): the pump
        // reads the live ResolutionContext.Source (the ability's own Source at
        // resolution) and registers the PumpUntilEndOfTurnEffect on THAT
        // permanent, falling back to `card` only on the context-less legacy sync
        // path. Marked RebindSafe below so Agatha's Soul Cauldron re-homes the
        // REAL +1/+0 pump to a counter-bearing bearer via
        // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — the pump bumps the
        // BEARER, never the exiled Steel Hellkite. The pump effect registers on
        // the subject's own ActiveEffects service (the bearer's, when re-homed),
        // so a re-home with no live effects service no-ops cleanly.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 EOT for {{2}}",
            ctx =>
            {
                var subject = (ctx.Source as Creature) ?? card;
                subject.ActiveEffects?.Register(new PumpUntilEndOfTurnEffect(subject, 1, 0));
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}") },
            effects: new IEffect[] { pumpEffect },
            rebindSafe: true));

        // ----------------------------------------------------------------
        // Combat-damage-victim tracker (RE-SOURCE-SAFE, agatha-rebind…sweep).
        //
        // Accumulates the controllers of every entity dealt combat damage THIS
        // turn, keyed BY THE DAMAGE-SOURCE PERMANENT (CR 510.1c). Keying by
        // source — instead of gating to the single captured Steel Hellkite —
        // is what makes the "damaged by ~ this turn" linkage RE-SOURCEABLE:
        // when Agatha's Soul Cauldron re-homes the {X} sweep to a BEARER via
        // ActivatedAbility.RebindTo, the rebound ability reuses THIS SAME effect
        // closure (and this same shared map), and the sweep resolves the victim
        // set for its LIVE source (ResolutionContext.Source = the bearer), so it
        // destroys permanents whose controller the BEARER damaged — never the
        // exiled Steel Hellkite's stale linkage. The map subscribes to the live
        // game's event bus, so the bearer's CombatDamageDealtEvent is captured
        // exactly the way Steel Hellkite's own is.
        //
        // Reset on TurnStartedEvent (CR 700.5 — "this turn" memory).
        // ----------------------------------------------------------------
        var combatVictimsBySource = new Dictionary<Permanent, HashSet<Player>>();

        if (eventBus != null)
        {
            eventBus.Subscribe<CombatDamageDealtEvent>(e => TrackCombatVictim(e, combatVictimsBySource));
            eventBus.Subscribe<TurnStartedEvent>(_ => combatVictimsBySource.Clear());
        }

        // ----------------------------------------------------------------
        // {X}: Destroy each nonland permanent with mana value X whose
        // controller was dealt combat damage by this creature this turn.
        // (Scryfall oracle; CR 701.7b destroy.) CR 117.1a / 307.5 —
        // sorcery-speed-equivalent gate via ActivatedAbility.sorcerySpeed
        // (see class xmldoc for the v1 caveat on the "once each turn" rider).
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: destroy each nonland permanent with mv = X whose controller took combat damage from this creature this turn",
            ctx =>
            {
                // GAP 2 — X comes from the per-activation ledger
                // (ResolutionContext.ChosenX, threaded by ActivatedAbility.
                // ResolveAsync). The xValueProvider closure is kept ONLY as an
                // optional override for shape/unit tests; when supplied it wins,
                // otherwise prod reads the chosen X (was always 0 before GAP 2).
                var x = xValueProvider?.Invoke() ?? ctx.ChosenX ?? 0;

                // RE-SOURCE — "this creature" is the ability's LIVE source
                // (ResolutionContext.Source: the bearer when re-homed by Agatha,
                // Steel Hellkite itself otherwise). Fall back to `card` only on
                // the context-less legacy/shape path. The victim set is the one
                // tracked for THAT source.
                var sweepSource = ctx.Source ?? card;
                var victims = combatVictimsBySource.TryGetValue(sweepSource, out var v)
                    ? v
                    : EmptyVictims;

                ResolveDestroySweep(owner, x, ctx.Game?.AllPlayers, victims);
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{X}") },
            effects: new IEffect[] { sweepEffect },
            sorcerySpeed: true,
            rebindSafe: true));

        return card;
    }

    private static readonly HashSet<Player> EmptyVictims = new();

    // --- Combat-damage-victim tracker (CR 510.1c / 700.5) -----------------
    // RE-SOURCE-SAFE: keyed by the damage-SOURCE permanent (e.Source), not the
    // captured Steel Hellkite, so the "damaged by this creature this turn"
    // linkage is per-source and a re-homed (Agatha) ability reads the bearer's
    // own victim set off ResolutionContext.Source.
    private static void TrackCombatVictim(
        CombatDamageDealtEvent e,
        Dictionary<Permanent, HashSet<Player>> combatVictimsBySource)
    {
        if (e.Source is null) return;
        if (e.Amount <= 0) return;

        Player? victim = null;

        // Damage to a creature / planeswalker → its controller.
        // CombatDamageDealtEvent.Target is ICard? (null when the target is
        // a player — see the dual-ctor on the event).
        if (e.Target is ICard targetCard)
        {
            victim = targetCard.Controller;
        }
        // Damage to a player → read TargetPlayer off the base
        // DamageDealtEvent (set by the Player-target ctor).
        else if (e.TargetPlayer is { } victimPlayer)
        {
            victim = victimPlayer;
        }

        if (victim == null) return;

        if (!combatVictimsBySource.TryGetValue(e.Source, out var set))
        {
            set = new HashSet<Player>();
            combatVictimsBySource[e.Source] = set;
        }
        set.Add(victim);
    }

    // --- {X}: destroy sweep (CR 701.7b) -----------------------------------
    private static void ResolveDestroySweep(
        Player owner,
        int x,
        IReadOnlyList<Player>? allPlayers,
        HashSet<Player> combatVictims)
    {
        if (combatVictims.Count == 0) return;

        var players = allPlayers ?? (IReadOnlyList<Player>)new[] { owner };

        foreach (var p in players)
        {
            if (p == null) continue;
            DestroyMatchingPermanents(p, x, combatVictims);
        }
    }

    private static void DestroyMatchingPermanents(Player p, int x, HashSet<Player> combatVictims)
    {
        // Snapshot — we mutate the battlefield list inside the loop.
        // Mirror Engineered Explosives / Blast Zone pattern.
        // CR (Scryfall oracle): "each NONLAND permanent" — lands are excluded
        // (a land's mv is 0, so without this filter X=0 would sweep lands too).
        var victims = p.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(c => !c.HasType(CardType.Land))
            .Where(c => c.Controller != null && combatVictims.Contains(c.Controller))
            .Where(c => c.ManaCostValue.TotalValue == x)
            .ToList();

        foreach (var v in victims)
        {
            // CR 701.7b — destroyed permanents go to their owner's
            // graveyard. Fall back to the iterated player when Owner is
            // null so shape-only tests still surface the destruction.
            var victimOwner = v.Owner ?? p;
            p.Zones.Battlefield.RemoveCard(v);
            victimOwner.Zones.Graveyard.AddCard(v);
            v.SetZone(ZoneType.Graveyard);
        }
    }
}
