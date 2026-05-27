using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Solemnity (Hour of Devotion, {2}{W}).
///
/// Enchantment. Oracle text:
///   "Players can't get counters. Permanents enter the battlefield without
///    counters. If a counter would be put on a permanent or player, it
///    isn't."
///
/// ## Implemented (v1)
///
/// Two CR 614 replacement effects, both registered when a
/// <see cref="ReplacementBus"/> is supplied:
///
/// 1. <see cref="SolemnityCounterAddReplacement"/> on
///    <see cref="CounterAddIntent"/> — global "no permanent counters"
///    cap. When Solemnity is on the battlefield, every counter-placement
///    intent routed through <see cref="Services.CountersService.Add"/> is
///    rewritten to <c>Amount = 0</c>. Returning a zero-amount intent
///    rather than <c>null</c> matches the existing replacement family's
///    shape; <see cref="Services.CountersService.Add"/>'s post-replacement
///    <c>Amount &lt;= 0</c> guard then short-circuits the commit AND the
///    post-commit <c>CounterAddedEvent</c> publish — Solemnity's "it isn't"
///    silences both the counter and any "Whenever counters are put on..."
///    trigger riders. Vizier of Remedies-shape (Vizier rewrites -1/-1
///    placements to 0; Solemnity is the strictly-broader variant covering
///    every CounterType).
///
/// 2. <see cref="SolemnityEntersWithCountersReplacement"/> on
///    <see cref="ZoneMoveIntent"/> — "permanents enter the battlefield
///    without counters". Strips
///    <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> back to 0 on
///    every ETB intent while Solemnity is on the battlefield. Covers
///    Strangleroot Geist (printed enters-with-counters), Triskelion,
///    Walking Ballista, Hangarback Walker, and the Modular / Reinforce
///    families that stamp ETB counters through the same intent. Direct-
///    add factory paths that mutate <c>Permanent.Counters</c> bypass the
///    bus today — those will be silenced once they migrate to
///    <see cref="Services.CountersService.Add"/> (tracked by the same
///    Hardened Scales retrofit).
///
/// ## CR alignment
/// - <b>CR 614.1b</b>: "instead" replacement — rewriting Amount to 0 is
///   the canonical "would happen — it doesn't" shape.
/// - <b>CR 614.6</b>: replacements are only active while the printed
///   source is in the right zone. Both replacements self-gate on
///   <c>_source.Zone == Battlefield</c>, so blink / bounce / destroy
///   removes the silencing without needing an explicit deregister
///   (same pattern as <see cref="SoulScarMageDamageReplacement"/>,
///   <see cref="HardenedScalesEtbReplacement"/>).
/// - <b>CR 122 (counters on objects)</b>: Solemnity is type-agnostic —
///   every counter type on every permanent kind is silenced (+1/+1,
///   -1/-1, charge, loyalty placed on PWs by ETB or activated abilities,
///   indestructibility from Heroic Intervention-style adds, etc.).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only. No replacements
///   registered. Suitable for dispatcher / structural tests. Mirrors
///   <see cref="HardenedScalesFactory.Create(Player)"/>.
/// - <see cref="Create(Player, ReplacementBus?)"/> — both replacements
///   are registered when the bus is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Players-can't-get-counters</b>: poison / energy / experience
///   counter placement on <see cref="Player"/> happens via
///   <see cref="Player.AddPoisonCounters"/> / <c>GainEnergy</c> /
///   similar direct mutators — none of those route through the
///   <see cref="ReplacementBus"/> today. Solemnity's permanent-side
///   coverage is complete; the player-side clause is silently a no-op
///   until player-counter placement gets a <c>PlayerCounterAddIntent</c>
///   primitive. Tracked as a follow-up (same shape as the
///   <see cref="CounterAddIntent"/> retrofit Hardened Scales depends on).
/// - <b>Direct-add factory call sites</b>: factories that call
///   <c>permanent.Counters.Add(...)</c> directly (Champion of the Parish
///   ETB-trigger, Arcbound Ravager modular self-bump, Walking Ballista
///   {X} activation, etc.) bypass the bus and aren't silenced until they
///   migrate to <see cref="Services.CountersService.Add"/>. Same retrofit
///   gap Hardened Scales documents — Solemnity inherits the migration
///   coverage as it lands.
/// - <b>Loyalty counters on planeswalker ETB</b>: planeswalkers enter
///   with their printed starting loyalty (CR 306.5b). At v1 the engine
///   stamps starting loyalty directly on the card rather than through
///   <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/>, so the printed
///   "enters without counters" rider doesn't yet zero a planeswalker's
///   starting loyalty (it would make every planeswalker an immediate
///   SBA death — Solemnity infamously breaks planeswalkers in paper).
///   Surfacing starting loyalty through the ETB-counters intent is the
///   right retrofit but lives outside this card's scope.
/// </summary>
[CardName("Solemnity")]
public static class SolemnityFactory
{
    public const string CardName = "Solemnity";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>
    /// Construct a Solemnity card with no live wiring. Shape-only —
    /// suitable for dispatcher / structural tests.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct a Solemnity card with optional replacement-bus wiring.
    /// When <paramref name="replacements"/> is supplied, both the
    /// "no counters on permanents" (<see cref="SolemnityCounterAddReplacement"/>)
    /// and "permanents enter without counters"
    /// (<see cref="SolemnityEntersWithCountersReplacement"/>) clauses are
    /// registered so they fire on every matching intent while Solemnity
    /// is on the battlefield.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<CounterAddIntent>(new SolemnityCounterAddReplacement(card));
            replacements.Register<ZoneMoveIntent>(new SolemnityEntersWithCountersReplacement(card));
        }

        return card;
    }
}

/// <summary>
/// CR 614 replacement: while Solemnity is on the battlefield, every
/// <see cref="CounterAddIntent"/> routed through
/// <see cref="Services.CountersService.Add"/> is rewritten to
/// <c>Amount = 0</c>. The Vizier of Remedies shape, broadened from "no
/// -1/-1 on creatures you control" to "no counters of any type on any
/// permanent" — Solemnity is global, symmetric, and type-agnostic.
///
/// Returning <c>Amount = 0</c> (rather than <c>null</c>) keeps the same
/// shape as the rest of the replacement family;
/// <see cref="Services.CountersService.Add"/>'s post-replacement guard
/// short-circuits the commit and the <c>CounterAddedEvent</c> publish —
/// so "Whenever one or more counters are put on..." trigger riders also
/// stay silent (CR 603.6 — the event only fires on a successful commit).
/// </summary>
public sealed class SolemnityCounterAddReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Enchantment _source;

    public SolemnityCounterAddReplacement(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Solemnity instance this replacement is keyed to.</summary>
    public Enchantment Source => _source;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — only active while Solemnity is on the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;
        // Already-zeroed intents — nothing to silence.
        if (intent.Amount <= 0) return false;
        return true;
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = 0 };
}

/// <summary>
/// CR 614 replacement: while Solemnity is on the battlefield, every
/// permanent's ETB <see cref="ZoneMoveIntent"/> has its
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> reset to 0.
/// Covers Strangleroot Geist, Triskelion, Walking Ballista
/// ({X}-counters via <see cref="Effects.EntersWithCountersReplacement"/>),
/// Hangarback Walker, and the Modular / Reinforce families that funnel
/// printed ETB counters through the same intent.
///
/// Stripping <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> rather
/// than returning <c>null</c> keeps the rest of the ETB pipeline running
/// — the card still enters, EntersTapped flags survive, and only the
/// counter-stamping rider on the post-land path is silenced.
/// </summary>
public sealed class SolemnityEntersWithCountersReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Enchantment _source;

    public SolemnityEntersWithCountersReplacement(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Solemnity instance this replacement is keyed to.</summary>
    public Enchantment Source => _source;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — only active while Solemnity is on the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Battlefield) return false;
        if (intent.PlusOneCountersOnEnter <= 0) return false;
        return true;
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { PlusOneCountersOnEnter = 0 };
}
