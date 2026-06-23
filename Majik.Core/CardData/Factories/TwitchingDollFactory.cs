using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twitching Doll (Duskmourn: House of Horror, {1}{G}).
///
/// Artifact Creature — Spider Toy 2/2. Oracle text (verified Scryfall
/// 2026-06-23):
///   "{T}: Add one mana of any color. Put a nest counter on this creature.
///    {T}, Sacrifice this creature: Create a 2/2 green Spider creature token
///    with reach for each counter on this creature. Activate only as a
///    sorcery."
///
/// ## Why it gets its own factory
/// Combines two primitives the engine already supports — the
/// <see cref="CoalitionRelicFactory"/> "{T}: Add one mana of any color" WUBRG
/// mana-ability fan (here each slot ALSO puts a nest counter, an allowed
/// mana-ability side effect — CR 605.1a: a mana ability may have additional
/// effects so long as it could add mana and doesn't target) and the
/// <see cref="RatchetBombFactory"/> "{T}, Sacrifice: …" snapshot-before-sac
/// shape, combined with the <see cref="TwinSilkSpiderFactory"/> 2/2-green-
/// Spider-with-reach token spec and the <see cref="KrenkoMobBossFactory"/>
/// "create N tokens" loop.
///
/// ## Implemented (v1)
/// - 2/2 <see cref="Creature"/> that is also an <see cref="Artifact"/>
///   (types <c>["Artifact","Creature"]</c>), subtypes Spider + Toy, at the
///   printed cost {1}{G}. Base shape comes from the embedded JSON definition
///   (<c>twitching-doll.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>"{T}: Add one mana of any color. Put a nest counter on this creature."</b>
///   — five <see cref="ManaAbility"/> instances (one per WUBRG, the
///   Coalition-Relic / Arcane-Signet modal-color shape; the activator picks a
///   color by picking the matching slot, CR 605.1 — mana abilities don't use
///   the stack). Each is gated on the Doll being on the battlefield and
///   untapped; each runs an <c>additionalCostPayer</c> that places one
///   <see cref="CounterType.Nest"/> counter (CR 122) as part of the same atomic
///   mana-ability activation. Adding a counter is a permitted mana-ability side
///   effect (CR 605.1a — it neither targets nor is a loyalty ability).
/// - <b>"{T}, Sacrifice this creature: Create a 2/2 green Spider creature token
///   with reach for each counter on this creature. Activate only as a
///   sorcery."</b> — an <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>
///   and the <c>sorcerySpeed</c> rider (CR 117.1a / 307.5 — enforced by the
///   <c>ActionValidator</c> gate). Mirrors Ratchet Bomb's snapshot posture:
///   the counter count ("each counter on this creature" — counters of ANY
///   type, of which only nest counters are ever placed) is snapshotted BEFORE
///   the sacrifice, because once the Doll leaves the battlefield its counters
///   cease to exist (CR 121.2). It then mints that many 2/2 green Spider
///   tokens with reach (CR 111 / 111.4) under its controller, via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only dispatcher path. Abilities are
///   attached for shape observability; tokens land via raw zone moves (no
///   <see cref="ZoneService"/>), the self-sacrifice publishes nothing. Suitable
///   for shape / <see cref="NamedCardFactory"/> dispatch tests.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — the effects-aware
///   overload the production <c>GameFacade</c> routed build dispatches to
///   (Ratchet-Bomb / Festival-Crasher pattern); threads the event bus so the
///   sacrifice publishes <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
/// </summary>
[CardName("Twitching Doll")]
public static class TwitchingDollFactory
{
    public const string CardName = "Twitching Doll";
    public const string Slug = "twitching-doll";
    public const int TokenPower = 2;
    public const int TokenToughness = 2;
    private const string ReachKeyword = "Reach";

    private static readonly string[] Wubrg = { "W", "U", "B", "R", "G" };

    /// <summary>
    /// Construct Twitching Doll with no live wiring. Abilities are attached for
    /// shape observability; the sac ability's tokens land via raw zone moves and
    /// the sacrifice publishes nothing. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to. Threads <c>effects.EventBus</c> into the
    /// self-sacrifice cost so paying it publishes
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Token-ETB triggers
    /// are a v1 gap (the routed build exposes no <c>ZoneService</c> — same
    /// posture as Krenko / Ratchet Bomb).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var doll = (Creature)CardDefinitionFactory.Build(definition, owner);

        var eventBus = effects?.EventBus;

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Put a nest counter on this
        // creature. (CR 605.1a — mana ability; doesn't use the stack.) Five
        // ManaAbility instances (one per WUBRG); the activator selects a color
        // by picking the matching slot, the same modal-color shape Coalition
        // Relic / Arcane Signet use. Each slot's additionalCostPayer places
        // one nest counter — a permitted mana-ability side effect (CR 605.1a:
        // it doesn't target and could add mana). {T} is the default
        // ManaAbility cost — gated on the Doll being on the battlefield and
        // untapped.
        // ----------------------------------------------------------------
        foreach (var color in Wubrg)
        {
            doll.AddAbility(new ManaAbility(
                source: doll,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => doll.Zone == ZoneType.Battlefield
                                        && !doll.IsTapped,
                additionalCostPayer: _ =>
                {
                    if (doll.Zone != ZoneType.Battlefield) return;
                    doll.Counters.Add(CounterType.Nest, 1); // CR 122
                }));
        }

        // ----------------------------------------------------------------
        // {T}, Sacrifice this creature: Create a 2/2 green Spider creature
        // token with reach for each counter on this creature. Activate only as
        // a sorcery. (CR 602 — activated ability; CR 117.1a / 307.5 —
        // sorcery-speed rider; CR 111 / 111.4 — token creation.)
        //
        // "each counter on this creature" = counters of ANY type (only nest
        // counters are ever placed here). Snapshot the count BEFORE the
        // sacrifice — once the Doll is in the graveyard its Counters bag is
        // gone (CR 121.2). Sacrifice payment is a no-op stub at the engine
        // cost level (Ratchet Bomb posture); the effect closure moves the Doll
        // to its owner's graveyard so visible state matches CR 701.16, routing
        // through Fx.Sacrifice when a bus is wired so the resolve-only path
        // publishes PermanentSacrificedEvent (CR 701.16a) exactly once.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: create a 2/2 green Spider token with reach for each counter",
            () =>
            {
                var controller = doll.Controller ?? owner;

                // Snapshot before the sacrifice (CR 121.2).
                var count = doll.Counters.All.Values.Sum();

                if (doll.Zone == ZoneType.Battlefield)
                {
                    if (eventBus != null)
                    {
                        Fx.Sacrifice(doll, controller, eventBus);
                    }
                    else
                    {
                        controller.Zones.Battlefield.RemoveCard(doll);
                        controller.Zones.Graveyard.AddCard(doll);
                        doll.SetZone(ZoneType.Graveyard);
                    }
                }

                // Tokens land via the null-zones raw battlefield path — token-
                // ETB triggers are a v1 gap here (same posture as Krenko / the
                // ContinuousEffectsService routed build, which exposes no
                // ZoneService).
                for (int i = 0; i < count; i++)
                {
                    CreateSpiderToken(controller, zones: null);
                }
            });

        doll.AddAbility(new ActivatedAbility(
            source: doll,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(doll),
                AdditionalCost.Sacrifice(doll, eventBus),
            },
            effects: new IEffect[] { sacEffect },
            sorcerySpeed: true));

        return doll;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 2/2 green Spider creature token with
    /// reach under <paramref name="controller"/>'s control. Mirrors
    /// <see cref="TwinSilkSpiderFactory.CreateSpiderToken"/> (2/2 instead of
    /// 1/2) so "green Spider with reach" minting stays uniform across sources.
    /// </summary>
    public static Creature CreateSpiderToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Spider",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Spider },
            // CR 702.17 — the token itself has reach.
            Keywords: new[] { ReachKeyword },
            // CR 105.2a / CR 111.4 — printed "2/2 green Spider creature token".
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
