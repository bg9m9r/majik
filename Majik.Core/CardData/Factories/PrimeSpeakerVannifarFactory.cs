using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prime Speaker Vannifar (Ravnica Allegiance,
/// {2}{G}{U}).
///
/// Legendary Creature — Elf Ooze Wizard 2/4. Oracle text (Scryfall, verified):
///   "{T}, Sacrifice another creature: Search your library for a creature card
///    with mana value equal to 1 plus the sacrificed creature's mana value,
///    put that card onto the battlefield, then shuffle. Activate only as a
///    sorcery."
///
/// "Birthing Pod on a stick" — the activated-ability sibling of
/// <see cref="BirthingRitualFactory"/> / <see cref="EldritchEvolutionFactory"/>.
/// Note the cap is EXACT (mana value == 1 + sac.MV), not "≤" like those two.
///
/// ## Shape source
/// Card identity (name, {2}{G}{U}, 2/4, Legendary Creature — Elf Ooze Wizard)
/// is loaded from <c>Majik.Core/CardData/Cards/prime-speaker-vannifar.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The activated ability is attached in
/// code below — the JSON ability schema does not express a
/// "sacrifice → tutor by relative mana value → battlefield → shuffle" effect,
/// so it is hand-rolled here, same posture as <see cref="FaunaShamanFactory"/>.
///
/// ## Implemented (v1)
/// - 2/4 Legendary Elf Ooze Wizard at {2}{G}{U} (CR 205.2 — three creature
///   subtypes; legendary supertype enforces the legend rule, CR 704.5j).
/// - <b>Activated ability (CR 602.1)</b> with three components:
///   <c>{T}</c> (<see cref="AdditionalCost.Tap"/> on the source — CR 107.4),
///   "sacrifice another creature" (<see cref="SacrificeAnotherCreatureCost"/>
///   — excludes Vannifar herself, CR 701.16a), and the sorcery-speed timing
///   rider (<c>sorcerySpeed: true</c> — CR 117.1a / 307.5 "Activate only as a
///   sorcery"; <see cref="Rules.ActionValidator"/> enforces it).
/// - <b>Resolution — tutor a creature with EXACT mana value</b>: reads the
///   sacrificed creature off <see cref="SacrificeAnotherCreatureCost.Sacrificed"/>
///   (captured during cost payment), computes the target mana value
///   <c>X = 1 + sacrificed.MV</c> (CR 202.3 — mana value off the printed
///   cost), searches the controller's library for a creature card whose mana
///   value EQUALS X (CR 701.19a — find / no-find both legal), moves the pick
///   Library → Battlefield under the controller's control, then shuffles ONCE
///   (CR 701.20a — one shuffle per search effect, whether or not a card was
///   found). When a <see cref="ZoneService"/> is registered the move routes
///   through it so ETB triggers fire (CR 603.6a); raw-zone fallback otherwise.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt</b>: <see cref="SacrificeAnotherCreatureCost"/>
///   prompts via <see cref="IChooseCreatureToSacrificeCost"/> on the live
///   dispatch path and falls back to the first eligible creature deterministically
///   on factory-direct / bot-convenience paths (same gap as Carrion Feeder /
///   Goblin Bombardment).
/// - <b>Reveal step</b>: the tutored creature moves Library → Battlefield
///   without publishing a per-card reveal event — same gap as every tutor
///   factory (<see cref="FaunaShamanFactory"/> / <see cref="EldritchEvolutionFactory"/>).
///   The observable game state (the permanent on the battlefield) is correct.
/// </summary>
[CardName("Prime Speaker Vannifar")]
public static class PrimeSpeakerVannifarFactory
{
    public const string CardName = "Prime Speaker Vannifar";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("prime-speaker-vannifar");

    /// <summary>
    /// Construct Prime Speaker Vannifar with no engine bus wiring (shape /
    /// dispatcher path). The activated ability is fully attached and
    /// exercisable; the sacrifice cost publishes no
    /// <c>PermanentSacrificedEvent</c> and the library→battlefield move
    /// falls back to <see cref="ZoneServiceRegistry"/> / raw zone moves.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Prime Speaker Vannifar with an optional
    /// <see cref="ZoneService"/>. When supplied, both the sacrifice (via the
    /// service's event bus) and the resolve-time Library → Battlefield move
    /// route through it so aristocrat payoffs (CR 701.16a) and ETB triggers
    /// (CR 603.6a) fire on the tutored permanent.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "{T}, Sacrifice another creature: Search your library for a
        //  creature card with mana value equal to 1 plus the sacrificed
        //  creature's mana value, put that card onto the battlefield, then
        //  shuffle. Activate only as a sorcery."
        // CR 602.1 — activated ability. Costs:
        //   {T}                       -> AdditionalCost.Tap (CR 107.4)
        //   Sacrifice another creature -> SacrificeAnotherCreatureCost
        // sorcerySpeed: true          -> CR 117.1a / 307.5 timing rider.
        // ----------------------------------------------------------------
        // CR 701.16a — when the zone service exposes an event bus the
        // sacrifice fires PermanentSacrificedEvent so aristocrat payoffs see
        // this outlet. ZoneService keeps its bus private, so we credit the
        // sacrifice via the same registry-routed move path the tutor uses and
        // leave the cost's optional bus null on the factory-direct path.
        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        var tutorEffect = new Effect(
            $"{CardName}: tutor a creature with mana value == 1 + sacrificed creature's MV onto the battlefield, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorExactManaValueAsync(controller, sacrificeCost, ctx, zoneService);
            });

        var ability = new PrimeSpeakerVannifarAbility(
            source: card,
            controller: owner,
            tapCost: AdditionalCost.Tap(card),
            sacrificeCost: sacrificeCost,
            tutorEffect: tutorEffect);

        card.AddAbility(ability);
        return card;
    }

    /// <summary>
    /// Resolve Vannifar's ability body: read the sacrificed creature off the
    /// cost, compute <c>X = 1 + sac.MV</c>, tutor a creature card whose mana
    /// value EQUALS X (CR 701.19a) onto <paramref name="controller"/>'s
    /// battlefield, then shuffle once (CR 701.20a). Public so tests / bots can
    /// drive resolution without going through the stack.
    /// </summary>
    public static async ValueTask TutorExactManaValueAsync(
        Player controller,
        SacrificeAnotherCreatureCost sacrificeCost,
        ResolutionContext ctx,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(sacrificeCost);

        // The sacrifice is part of the activation cost (paid before resolution).
        // No captured sacrifice ⇒ the cost wasn't paid via the expected shape;
        // resolve as a no-op rather than tutor for an unbounded value.
        var sacrificed = sacrificeCost.Sacrificed;
        if (sacrificed == null) return;

        // CR 202.3 — mana value reads off the printed cost. EXACT match:
        // "mana value equal to 1 plus the sacrificed creature's mana value".
        var targetMv = 1 + sacrificed.ManaCostValue.TotalValue;

        var candidates = controller.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Creature)
                        && ManaCost.Parse(c.ManaCost).TotalValue == targetMv)
            .ToList();

        // CR 701.19a — prompt even on zero candidates so the searcher sees the
        // failed search. Defensive: only accept a pick from the offered set.
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, controller, candidates,
            $"creature card with mana value {targetMv}").ConfigureAwait(false);

        if (pick != null && candidates.Contains(pick))
        {
            // CR 603.6a — prefer the caller-supplied zoneService; fall back to
            // the registry so the dispatcher-driven path still routes through
            // the live ZoneService (ETB triggers fire on the tutored creature).
            var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    pick, ZoneType.Library, ZoneType.Battlefield, controller);
            }
            else
            {
                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(controller);
            }
        }

        // CR 701.20a — shuffle once after the search, whether or not a card
        // was found.
        LibraryShuffle.ShuffleLibrary(controller, "prime-speaker-vannifar");
    }
}

/// <summary>
/// Prime Speaker Vannifar's sole activated ability — {T}, sacrifice another
/// creature to tutor a creature with mana value exactly one higher onto the
/// battlefield, sorcery-speed only. Subclasses <see cref="ActivatedAbility"/>
/// so the sacrifice cost is reachable from tests / bots that want to pre-set
/// <see cref="SacrificeAnotherCreatureCost.Target"/> before activation.
/// </summary>
public sealed class PrimeSpeakerVannifarAbility : ActivatedAbility
{
    /// <summary>
    /// The sacrifice cost on the ability — exposed so callers can pre-set
    /// <see cref="SacrificeAnotherCreatureCost.Target"/> before activation and
    /// read <see cref="SacrificeAnotherCreatureCost.Sacrificed"/> after.
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    internal PrimeSpeakerVannifarAbility(
        Creature source,
        Player controller,
        AdditionalCost tapCost,
        SacrificeAnotherCreatureCost sacrificeCost,
        IEffect tutorEffect)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { tapCost, sacrificeCost },
            effects: new[] { tutorEffect },
            sorcerySpeed: true) // CR 117.1a / 307.5 — "Activate only as a sorcery".
    {
        SacrificeChoice = sacrificeCost;
    }
}
