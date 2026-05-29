using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scourge of the Skyclaves (Zendikar Rising,
/// {1}{B}). Creature — Demon, printed power/toughness "*/*".
///
/// ## Card text (Scryfall verified)
/// "Kicker {4}{B}
///  When you cast this spell, if it was kicked, each player loses half their
///  life, rounded up.
///  Scourge of the Skyclaves's power and toughness are each equal to 20
///  minus the highest life total among players."
///
/// ## Base shape
/// Name / Creature / Demon / {1}{B} are materialised from the embedded JSON
/// definition (<c>scourge-of-the-skyclaves.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="KroxaTitanFactory"/>. JSON ships <c>power/toughness = 0</c> as
/// the seed; the Layer 7a CDA overwrites it on every Compute.
///
/// ## Implemented (v1)
/// - <b>CDA power/toughness (CR 604.3 / 613.2 Layer 7a)</b>:
///   "Scourge's power and toughness are each equal to 20 minus the highest
///   life total among players." Modeled with the established
///   <see cref="CdaPowerToughnessEffect"/> primitive (shared with
///   <see cref="DeathsShadowFactory"/> / Tarmogoyf, PR #173). The Death's
///   Shadow shape, but the life lookup spans EVERY player rather than just
///   the controller — the highest life among them feeds <c>20 - highest</c>.
///   Clamped to <c>[0, 20]</c>: highest life ≥ 20 floors to 0 (the
///   0-toughness SBA — CR 704.5f — kills it in real play unless something
///   else lifts P/T), and the printed 20 cap covers the all-players-at-or-
///   below-0 edge. The all-players list arrives via
///   <paramref name="allPlayersResolver"/> (same shape as
///   <see cref="AshiokDreamRenderFactory"/>). ETB/LTB register/unregister
///   the CDA off <see cref="CardMovedEvent"/>, mirroring Death's Shadow.
///
/// - <b>Kicker (CR 702.33)</b>: shipped as a real
///   <see cref="KickerAdditionalCost"/> via <see cref="BuildAdditionalCost"/>
///   (same primitive Burst Lightning / Goblin Bushwhacker use). Caller
///   layers the cost onto the cast; on payment the cost stamps
///   <see cref="Card.WasKicked"/> = true. Registered in
///   <see cref="Players.Agents.KickerAltCostProbe.DefaultLookup"/> so the
///   bot's kicker probe recognises Scourge as a {4}{B}-kicker card.
///
/// - <b>Cast trigger (CR 603.3 / 603.4 / 702.33b)</b>:
///   "When you cast this spell, if it was kicked, each player loses half
///   their life, rounded up." A cast trigger (CR 603.3) keyed on
///   <see cref="SpellCastEvent"/> for this card — same shape as Cascade
///   (<see cref="ArdentPleaFactory"/>), <c>ActiveZones = { Stack }</c> so it
///   is live while Scourge is on the stack as a spell. The "if it was
///   kicked" rider is an intervening-if (CR 603.4) on
///   <see cref="Card.WasKicked"/>: not-kicked → the trigger never goes on the
///   stack. On resolution every player (CR 102.1 / "each player") loses half
///   their current life rounded up (CR 119.3; ceil(life / 2)). All players
///   are read from <paramref name="allPlayersResolver"/>.
///
/// ## Notes
/// - "Half their life, rounded up" is computed against each player's life AT
///   resolution (CR 608.2), one independent loss per player. A player at 0 or
///   negative life loses nothing (ceil(≤0 / 2) ≤ 0 → <see cref="Fx.LoseLife"/>
///   no-ops). Life loss is NOT damage (CR 119.3) — unpreventable.
/// </summary>
[CardName("Scourge of the Skyclaves")]
public static class ScourgeOfTheSkyclavesFactory
{
    public const string CardName = "Scourge of the Skyclaves";
    public const string Slug = "scourge-of-the-skyclaves";

    /// <summary>CR 702.33 — printed Kicker cost: {4}{B}.</summary>
    public const string KickerCostText = "{4}{B}";

    /// <summary>CR 613.2 — the CDA subtracts the highest life from 20.</summary>
    public const int CdaBase = 20;

    /// <summary>
    /// Construct Scourge with no live wiring. Both abilities (the cast
    /// trigger and a shape-only CDA marker) are attached for inspection; the
    /// bodies no-op cleanly without resolvers / services. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, allPlayersResolver: null, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Scourge with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at
    /// evaluation time. The CDA reads the highest life among these; the cast
    /// trigger applies the half-life loss to each. Null → the CDA seeds 0/0
    /// and the cast trigger body no-ops (shape path).</param>
    /// <param name="effects">Continuous-effects service the CDA registers
    /// against on ETB. Pass null for shape-only P/T.</param>
    /// <param name="eventBus">Event bus for ETB/LTB CDA tracking. May be
    /// null — the CDA's battlefield gate covers correctness.</param>
    /// <param name="triggers">TriggerManager — when supplied the cast
    /// trigger is registered so a matching <see cref="SpellCastEvent"/> lands
    /// it on the stack automatically.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Demon, {1}{B}, seed 0/0). The CDA below defines the real P/T.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CDA power/toughness — CR 604.3 / 613.2 Layer 7a.
        //   "Scourge's power and toughness are each equal to 20 minus the
        //    highest life total among players."
        // Death's Shadow shape, but the life lookup spans every player.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new ScourgeCdaLifecycle(card, allPlayersResolver, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.3 / 603.4 / 702.33b.
        //   "When you cast this spell, if it was kicked, each player loses
        //    half their life, rounded up."
        // Keyed on SpellCastEvent for this card (same shape as Cascade);
        // ActiveZones = { Stack } so it is live while Scourge is on the
        // stack as a spell. The "if it was kicked" rider is an
        // intervening-if (CR 603.4) on Card.WasKicked.
        // ----------------------------------------------------------------
        var castEffect = new Effect(
            $"{CardName}: each player loses half their life, rounded up",
            () =>
            {
                // CR 603.4 — second-pass intervening-if. Defensive re-check
                // at resolution mirrors Goblin Bushwhacker's posture.
                if (!card.WasKicked) return;

                var players = allPlayersResolver?.Invoke();
                if (players == null) return; // shape path — no players wired.

                // CR 608.2 — snapshot the loss per player at resolution; each
                // is an independent life-loss (CR 119.3, not damage).
                foreach (var player in players)
                {
                    if (player == null) continue;
                    Fx.LoseLife(player, HalfRoundedUp(player.LifeTotal));
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>(
                (e, _) => ReferenceEquals(e.Spell.Card, card)),
            effects: new IEffect[] { castEffect },
            // CR 603.4 — queue-time intervening-if. False (not kicked) = the
            // trigger doesn't go on the stack at all.
            interveningIf: () => card.WasKicked,
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// CR 702.33 — construct Scourge's kicker rider ({4}{B}) for the supplied
    /// <paramref name="card"/> instance. Layer the returned cost onto the
    /// cast to pay the kicker (same wiring shape as Goblin Bushwhacker).
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// CR 613.2 — Scourge's CDA value: <c>clamp(20 - highest life among
    /// players, 0, 20)</c>. Highest life ≥ 20 floors to 0; the printed 20 cap
    /// covers the all-players-at-or-below-0 edge. An empty / null player list
    /// yields the printed 20 (no life to subtract).
    /// </summary>
    public static int ComputePT(IReadOnlyList<int> lifeTotals)
    {
        // CR 613.2 — "highest life total among players." With no players the
        // CDA reduces to its printed 20 (nothing to subtract).
        var highest = (lifeTotals == null || lifeTotals.Count == 0)
            ? 0
            : lifeTotals.Max();

        var value = CdaBase - highest;
        if (value < 0) return 0;
        if (value > CdaBase) return CdaBase;
        return value;
    }

    /// <summary>
    /// "Half their life, rounded up" — ceil(life / 2) for life &gt; 0; 0 for
    /// life ≤ 0 (a player at or below 0 loses nothing). Integer-only ceiling
    /// via <c>(life + 1) / 2</c>.
    /// </summary>
    public static int HalfRoundedUp(int life)
    {
        if (life <= 0) return 0;
        return (life + 1) / 2;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Scourge's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Scourge enters the
    /// battlefield, unregisters when it leaves. Mirrors Death's Shadow's
    /// lifecycle binder, but the P/T evaluator reads the highest life among
    /// ALL players via the supplied resolver.
    /// </summary>
    private sealed class ScourgeCdaLifecycle
    {
        private readonly Creature _source;
        private readonly Func<IReadOnlyList<Player>>? _allPlayersResolver;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public ScourgeCdaLifecycle(
            Creature source,
            Func<IReadOnlyList<Player>>? allPlayersResolver,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _allPlayersResolver = allPlayersResolver;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.SubscribeAll(_handler);
            Sync();
        }

        private void OnEvent(GameEvent e)
        {
            if (e is not CardMovedEvent moved) return;
            if (!ReferenceEquals(moved.Card, _source)) return;
            Sync();
        }

        private int Evaluate()
        {
            var players = _allPlayersResolver?.Invoke();
            if (players == null || players.Count == 0)
            {
                return ScourgeOfTheSkyclavesFactory.ComputePT(Array.Empty<int>());
            }

            var lives = players.Where(p => p != null).Select(p => p.LifeTotal).ToList();
            return ScourgeOfTheSkyclavesFactory.ComputePT(lives);
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => Evaluate(),
                    toughnessOf: _ => Evaluate());
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
