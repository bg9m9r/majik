using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abzan Falconer (Khans of Tarkir, {2}{W}).
///
/// Creature — Human Soldier 2/3. Oracle text (verified against Scryfall):
///   "Outlast {W} ({W}, {T}: Put a +1/+1 counter on this creature. Outlast
///    only as a sorcery.)
///    Each creature you control with a +1/+1 counter on it has flying."
///
/// The base shape (name, Creature, Human Soldier subtypes, {2}{W}, 2/3) is
/// materialised from the embedded JSON definition (<c>abzan-falconer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours — the
/// Outlast activated ability and the counter-gated team-flying static — are
/// layered on top here (same posture as
/// <see cref="WardenOfTheInnerSkyFactory"/>, whose counter-gated keyword static
/// and sorcery-speed counter-add this cribs directly).
///
/// ## Implemented (v1)
///
/// - 2/3 <see cref="Creature"/> — Human Soldier, {2}{W}, owner/controller wired.
/// - <b>Outlast {W} (CR 702.85)</b>: "{W}, {T}: Put a +1/+1 counter on this
///   creature. Outlast only as a sorcery." There is no Outlast keyword
///   primitive in the engine, so it is expanded to its reminder-text shape: an
///   <see cref="ActivatedAbility"/> whose costs are a
///   <see cref="ManaCostCost"/> {W} plus the self-tap
///   <see cref="AdditionalCost.Tap"/> (CR 118.12 — the {T} symbol), with
///   <see cref="ActivatedAbility.IsSorcerySpeed"/> set true for the "Outlast
///   only as a sorcery" timing rider (CR 702.85b / 117.1a / 307.5). On
///   resolution it puts one +1/+1 counter on the Falconer via
///   <see cref="CountersService.Add"/> (CR 702.85a / 614 — so Hardened Scales /
///   Doubling Season can rewrite the amount and the post-commit
///   <see cref="CounterAddedEvent"/> fires for counters-matter payoffs, and the
///   team-flying static's live read picks up the new counter).
/// - <b>Counter-gated team flying (CR 613.1f / 702.9)</b>: "Each creature you
///   control with a +1/+1 counter on it has flying." Wired as an
///   <see cref="AbzanFalconerFlyingEffect"/> Layer-6 (Abilities) continuous
///   effect that grants Flying to EVERY creature the Falconer's controller
///   controls — including the Falconer itself — that currently has at least one
///   +1/+1 counter on it. The membership filter is a live read of each
///   candidate's counter bag every layer pass, so flying appears the moment a
///   creature gains a +1/+1 counter and lifts when its last +1/+1 counter is
///   removed (CR 121.4 / 122.6). Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The team-flying static is NOT
///   registered (no continuous-effects service). The Outlast ability is attached
///   structurally and resolves correctly (counter placement via the direct
///   <see cref="CountersService.Add"/> fallthrough). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, ReplacementBus?, IEventBus?)"/>
///   — fully wired. The team-flying static registers with the layer system; the
///   Outlast counter placement routes through the replacement bus + publishes
///   <see cref="CounterAddedEvent"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>LTB unregister</b>: the registered <see cref="AbzanFalconerFlyingEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="AbzanFalconerFlyingEffect.IsActive"/> short-circuits when the
///   Falconer isn't on the battlefield so the team grant lifts correctly (same
///   posture as <see cref="WardenOfTheInnerSkyFactory"/>).
/// </summary>
[CardName("Abzan Falconer")]
public static class AbzanFalconerFactory
{
    public const string CardName = "Abzan Falconer";
    public const string Slug = "abzan-falconer";

    /// <summary>CR 702.85 — the Outlast activation mana cost ({W}).</summary>
    public const string OutlastManaCost = "{W}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Abzan Falconer with no live wiring. The team-flying static is
    /// NOT registered (no continuous-effects service); the Outlast activated
    /// ability is attached structurally (it resolves correctly when its effects
    /// are executed directly). Suitable for shape / dispatcher tests. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Abzan Falconer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the counter-gated
    /// team-flying static registers against. Pass null to skip the static
    /// (shape-only).</param>
    /// <param name="replacements">ReplacementBus. When supplied the Outlast
    /// +1/+1 counter placement routes through <see cref="CountersService.Add"/>
    /// so Hardened Scales / Doubling Season can rewrite the count (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied the Outlast counter
    /// placement publishes <see cref="CounterAddedEvent"/> so counters-matter
    /// payoffs (and the team-flying static's live read) pick it up.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Soldier, {2}{W}, 2/3). The JSON carries no abilities — the Outlast
        // activation + team-flying static are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Outlast {W} — CR 702.85.
        //   "{W}, {T}: Put a +1/+1 counter on this creature. Outlast only as
        //    a sorcery."
        // No Outlast keyword primitive yet, so expand to the reminder-text
        // shape: an activated ability with ManaCostCost {W} + the self-tap
        // (CR 118.12), sorcery-speed rider (CR 702.85b / 117.1a / 307.5).
        // Resolution places one +1/+1 counter on the Falconer via
        // CountersService.Add (CR 702.85a / 614).
        // ----------------------------------------------------------------
        var outlastEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on self (Outlast)",
            () => CountersService.Add(
                card, CounterType.PlusOnePlusOne, 1, replacements, eventBus));

        var outlast = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(OutlastManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { outlastEffect },
            // CR 702.85b — "Outlast only as a sorcery."
            sorcerySpeed: true);

        card.AddAbility(outlast);

        // ----------------------------------------------------------------
        // Counter-gated team flying — CR 613.1f / 702.9.
        //   "Each creature you control with a +1/+1 counter on it has flying."
        // Layer-6 continuous effect granting Flying to every creature the
        // controller controls (incl. the Falconer) that has >= 1 +1/+1 counter;
        // only registered when a layers service is available.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new AbzanFalconerFlyingEffect(card));
        }

        return card;
    }
}

/// <summary>
/// CR 613.1f (Layer 6 — ability-adding) / CR 702.9 (Flying) — grants the Flying
/// keyword to every creature controlled by its <see cref="Creature"/> source's
/// controller (INCLUDING the source itself — the oracle reads "Each creature you
/// control", with no "other" qualifier) that currently has at least one +1/+1
/// counter on it. The source's controller scope and each candidate's +1/+1
/// counter count are read live every layer pass (CR 122.1 / 122.6), so flying
/// appears the moment a creature gains a +1/+1 counter and lifts when its last
/// +1/+1 counter is removed. Active only while the source is on the battlefield.
/// </summary>
public sealed class AbzanFalconerFlyingEffect : ContinuousEffect
{
    private readonly Creature _source;

    public AbzanFalconerFlyingEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>Active only while the source is on the battlefield.</summary>
    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    /// <summary>
    /// CR 109.5 — "you control" filters to the source's controller. The
    /// candidate must be a creature that controller controls (the Falconer
    /// itself qualifies — no "other" clause) and have at least one +1/+1
    /// counter on it (CR 122.1).
    /// </summary>
    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        return creature.Counters.Count(CounterType.PlusOnePlusOne) > 0;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Flying");
    }
}
