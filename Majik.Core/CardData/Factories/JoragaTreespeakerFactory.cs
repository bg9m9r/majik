using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;
using IEffect = Majik.Core.Abilities.IEffect;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Joraga Treespeaker (Rise of the Eldrazi, {G}).
///
/// Creature — Elf Druid, printed P/T 1/1. Oracle text (verified against
/// Scryfall):
///   "Level up {1}{G} ({1}{G}: Put a level counter on this. Level up only as
///    a sorcery.)
///    LEVEL 1-4
///    1/2
///    {T}: Add {G}{G}.
///    LEVEL 5+
///    1/4
///    Elves you control have '{T}: Add {G}{G}.'"
///
/// First leveler implemented — pays down the level-up keyword subsystem
/// (CR 702.87 Level up / CR 711 leveler cards / CR 107.8 level symbols).
///
/// ## Implementation — declarative, on existing primitives
///
/// Three printed behaviours, each layered onto an EXISTING engine seam:
///
/// 1. <b>Level up {1}{G}</b> (CR 702.87) — a sorcery-speed
///    <see cref="ActivatedAbility"/> ({1}{G} <see cref="ManaCostCost"/>,
///    <c>sorcerySpeed: true</c> so <see cref="Rules.ActionValidator"/> gates
///    it to the controller's main phase with an empty stack — CR 702.87b /
///    307.5) whose resolution places one <see cref="CounterType.Level"/>
///    counter on this creature via <see cref="Services.CountersService.Add"/>
///    (so Doubling Season / replacement rewrites and the post-commit
///    <see cref="CounterAddedEvent"/> are honoured — CR 121.2 / 614).
///    Mirrors the sorcery-speed activated-ability shape of
///    <see cref="StormchasersTalentFactory"/>.
///
/// 2. <b>Level-band base P/T</b> (CR 107.8 / 711) — a single
///    <see cref="LevelBandEffect"/> (Layer 7b set-base) whose bands are
///    <c>{LEVEL 1-4} → 1/2</c> and <c>{LEVEL 5+} → 1/4</c>. The level-0 band
///    (no counters) is the printed 1/1 — the effect simply stays inactive
///    until the first counter lands. The live level count is read each
///    <see cref="ContinuousEffectsService.Compute(Permanent)"/> pass, so the
///    band switches the instant a counter crosses a threshold (CR 122.6 /
///    711.2). Same Layer-7b set-base seam as
///    <see cref="BecomesPTEffect"/>, gated on the counter band instead of a
///    fixed value.
///
/// 3. <b>Level-band abilities</b> (CR 107.8 / 613.1f — each band static
///    grants the band's abilities while in range; non-cumulative per band):
///    <list type="bullet">
///      <item><b>{LEVEL 1-4} — {T}: Add {G}{G}</b>: a self
///        <see cref="ManaAbility"/> granted via a
///        <see cref="GrantAbilityEffect"/> whose target selector returns this
///        creature only while its level is in <c>[1, 4]</c> (and null
///        otherwise, so the grant lifts when it levels to 5 — CR 107.8). The
///        granted mana ability materialises on
///        <see cref="Card.Abilities"/>, so
///        <see cref="EffectiveManaAbilities"/> surfaces it.</item>
///      <item><b>{LEVEL 5+} — "Elves you control have '{T}: Add {G}{G}.'"</b>:
///        a <see cref="GrantAbilityToGroupStaticEffect"/> whose membership
///        scope is "Elf you control" AND level &gt;= 5, granting each Elf
///        (including Joraga itself, an Elf) a {T}: Add {G}{G} mana ability.
///        Live membership recomputes as Elves enter / leave and as the band
///        switches (CR 611.2c / 107.8). Mirrors
///        <see cref="ChromaticLanternFactory"/>'s group-grant wiring.</item>
///    </list>
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The level-up activated
///   ability is attached structurally (its effect places a counter via the
///   <see cref="Services.CountersService.Add"/> direct fallthrough when
///   executed). No band P/T effect, no band ability grants (no
///   continuous-effects service). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, ReplacementBus?)"/>
///   — fully wired. The level-band P/T effect registers; the level-up
///   ability's counter placement routes through the replacement bus +
///   publishes <see cref="CounterAddedEvent"/>; both band ability grants are
///   wired to the layer system (lifecycle-tracked as Joraga enters / leaves
///   the battlefield).
///
/// ## Notes
///
/// - Summoning sickness for the granted {T} mana ability is the engine's job
///   (CR 302.6 / 605.3a), not encoded here. The granted mana ability carries
///   a <c>!IsTapped</c> gate (mirrors <see cref="LlanowarTribeFactory"/>).
/// - "Level up only as a sorcery" (CR 702.87b) is enforced via the
///   <c>sorcerySpeed</c> rider on the activated ability — the same timing
///   gate every other "activate only as a sorcery" ability uses.
/// </summary>
[CardName("Joraga Treespeaker")]
public static class JoragaTreespeakerFactory
{
    public const string CardName = "Joraga Treespeaker";
    public const string PrintedManaCost = "{G}";
    public const string LevelUpCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>CR 107.8 — {LEVEL 1-4} band bounds.</summary>
    public const int Band1Min = 1;
    public const int Band1Max = 4;

    /// <summary>CR 107.8 — {LEVEL 5+} band lower bound.</summary>
    public const int Band2Min = 5;

    /// <summary>The {T}: Add {G}{G} mana produced by both band abilities.</summary>
    private const string ManaProduced = "{G}{G}";

    /// <summary>
    /// CR 107.8 — Joraga Treespeaker's level bands (P/T half). {LEVEL 1-4}
    /// is 1/2; {LEVEL 5+} is 1/4. Level 0 is the printed 1/1.
    /// </summary>
    public static readonly IReadOnlyList<LevelBandEffect.Band> Bands = new[]
    {
        new LevelBandEffect.Band(Band1Min, Band1Max, 1, 2),
        new LevelBandEffect.Band(Band2Min, int.MaxValue, 1, 4),
    };

    /// <summary>
    /// Construct Joraga Treespeaker with no live layer wiring. The level-up
    /// activated ability is attached structurally; the band P/T + band
    /// ability grants are NOT registered (no continuous-effects service).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, eventBus: null, replacements: null);

    /// <summary>
    /// Construct a fully-wired Joraga Treespeaker.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the band P/T effect and
    /// band ability grants register against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB lifecycle tracking of
    /// the group grant, and for the level-up counter's post-commit
    /// <see cref="CounterAddedEvent"/>. May be null.</param>
    /// <param name="replacements">ReplacementBus the level-up counter
    /// placement routes through (Doubling Season et al. — CR 614). May be
    /// null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // 1. Level up {1}{G} — CR 702.87. Sorcery-speed activated ability;
        //    resolution places one level counter on this creature.
        // ----------------------------------------------------------------
        var levelUpEffect = new Effect(
            $"{CardName}: put a level counter on self",
            () => Majik.Core.Services.CountersService.Add(
                card, CounterType.Level, 1, replacements, eventBus));

        var levelUp = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(LevelUpCost) },
            effects: new IEffect[] { levelUpEffect },
            sorcerySpeed: true);
        card.AddAbility(levelUp);

        if (continuousEffects == null)
        {
            return card;
        }

        // ----------------------------------------------------------------
        // 2. Level-band base P/T — CR 107.8 / 711. Layer 7b set-base.
        // ----------------------------------------------------------------
        continuousEffects.Register(new LevelBandEffect(card, Bands));

        // ----------------------------------------------------------------
        // 3a. {LEVEL 1-4} self ability — {T}: Add {G}{G}. Granted while the
        //     level is in [1, 4]; the selector returns null outside that band
        //     so the grant lifts at level 5 (CR 107.8 — bands non-cumulative).
        // ----------------------------------------------------------------
        var band1Grant = new GrantAbilityEffect(
            source: card,
            targetSelector: () => IsInBand1(card) ? card : null,
            abilityFactory: bearer => BuildSelfMana(bearer));
        continuousEffects.Register(band1Grant);

        // ----------------------------------------------------------------
        // 3b. {LEVEL 5+} group ability — "Elves you control have
        //     '{T}: Add {G}{G}.'" Granted to every Elf the controller
        //     controls (including Joraga, an Elf) while the level is >= 5.
        //     Registered directly with the layer system (mirrors the band-1
        //     GrantAbilityEffect and PeltCollectorTrampleEffect): the
        //     effect's own battlefield gate + the band-5 scope predicate
        //     drive grant/revoke each Compute, so no event-bus lifecycle is
        //     required for correctness; membership is recomputed live as
        //     Elves enter / leave (CR 611.2c) and as the band switches.
        // ----------------------------------------------------------------
        continuousEffects.Register(new GrantAbilityToGroupStaticEffect(
            source: card,
            scope: p => IsInBand2(card)
                && p is Creature c
                && c.HasSubtype(CardSubtype.Elf)
                && ReferenceEquals(p.Controller, card.Controller),
            abilityFactory: member => new IAbility[] { BuildGroupMana(member) },
            membershipProvider: () => ControllerBattlefield(card)));

        return card;
    }

    /// <summary>CR 107.8 — is the source currently in {LEVEL 1-4}?</summary>
    public static bool IsInBand1(Creature card)
    {
        var level = card.Counters.Count(CounterType.Level);
        return level >= Band1Min && level <= Band1Max;
    }

    /// <summary>CR 107.8 — is the source currently in {LEVEL 5+}?</summary>
    public static bool IsInBand2(Creature card) =>
        card.Counters.Count(CounterType.Level) >= Band2Min;

    /// <summary>CR 605.1 — {T}: Add {G}{G} on the leveler itself.</summary>
    private static ManaAbility BuildSelfMana(Permanent bearer) =>
        new(
            source: bearer,
            controller: bearer.Controller
                ?? throw new InvalidOperationException(
                    "Cannot grant mana ability: no controller set."),
            manaGenerated: ManaCost.Parse(ManaProduced),
            canActivateCheck: () => bearer is Creature c && !c.IsTapped);

    /// <summary>CR 605.1 — {T}: Add {G}{G} granted to an Elf the controller
    /// controls (the {LEVEL 5+} anthem).</summary>
    private static ManaAbility BuildGroupMana(Permanent member) =>
        new(
            source: member,
            controller: member.Controller
                ?? throw new InvalidOperationException(
                    "Cannot grant mana ability: no controller set."),
            manaGenerated: ManaCost.Parse(ManaProduced),
            canActivateCheck: () => member is Creature c && !c.IsTapped);

    /// <summary>
    /// Live candidate set for the {LEVEL 5+} group grant: every permanent on
    /// Joraga's controller's battlefield. The <c>scope</c> predicate further
    /// filters to Elves the controller controls while in band 2.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Creature card)
    {
        var controller = card.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
