using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Named-card factory for Insidious Roots (Duskmourn: House of Horror,
/// {B}{G}). Enchantment. Oracle text (verified against Scryfall 2026-06-23):
///   "Creature tokens you control have "{T}: Add one mana of any color."
///    Whenever one or more creature cards leave your graveyard, create a 0/1
///    green Plant creature token, then put a +1/+1 counter on each Plant you
///    control."
///
/// The base shape (name, Enchantment type, {B}{G}) is materialised from the
/// embedded JSON definition (<c>insidious-roots.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the group mana-grant and the leaves-graveyard trigger are layered on here
/// (the trigger's effect — create a Plant + counter every Plant — has no JSON
/// effect-def, so it is hand-built; same JSON-backed-identity +
/// code-attached-behaviour posture as <see cref="EnduringVitalityFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>"Creature tokens you control have '{T}: Add one mana of any color.'"
///   (CR 613.1f)</b>: a Layer-6 group ability-grant via
///   <see cref="GrantAbilityToGroupStaticEffect"/>, wired by
///   <see cref="GrantAbilityToGroupLifecycle"/> — the SAME machinery Chromatic
///   Lantern / Enduring Vitality use, differing only in the scope predicate
///   (every Creature that is a token AND that this card's controller controls,
///   rather than every Land / every Creature). Each member is granted five
///   fresh single-colour <see cref="ManaAbility"/> instances (CR 605.1a — "any
///   color" = five distinct single-colour mana abilities); live membership is
///   recomputed as tokens enter / leave / change control (CR 611.2c), and the
///   granted mana abilities surface through <see cref="EffectiveManaAbilities"/>
///   so each creature token taps for any colour.
///
/// - <b>"Whenever one or more creature cards leave your graveyard, …"
///   (CR 603.2 — leaves-the-zone trigger)</b>: an
///   <see cref="EventTriggerCondition{T}"/> over
///   <see cref="Majik.Core.Events.CardMovedEvent"/> with the predicate
///   "FromZone == Graveyard &amp;&amp; moved card's owner == this trigger's
///   controller &amp;&amp; card is a Creature" — byte-identical to the
///   <c>card_leaves_your_graveyard</c> JSON trigger (cardTypes [Creature])
///   that Dredger's Insight uses, but with a bespoke effect body. The "one or
///   more … cards" batch wording (CR 603.3b) collapses to a single trigger per
///   <see cref="Majik.Core.Events.CardMovedEvent"/> in v1 (same batching
///   posture as Dredger's Insight). On resolution: (1) create a 0/1 green Plant
///   creature token (CR 111.4 — colour stamped green; <see cref="TokenFactory"/>),
///   then (2) put a +1/+1 counter (CR 122) on each Plant the controller controls
///   — INCLUDING the just-created token, matching the "then" ordering on the
///   card.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real "any color" colour choice</b>: the granted mana ability is the
///   five-single-colour expansion (CR 605.1a) the rest of the codebase uses
///   for "add one mana of any color" (Chromatic Lantern / Enduring Vitality);
///   the bot's mana picker selects whichever single colour it needs.
/// </summary>
[CardName("Insidious Roots")]
public static class InsidiousRootsFactory
{
    public const string CardName = "Insidious Roots";
    public const string Slug = "insidious-roots";

    /// <summary>The token created by the leaves-graveyard trigger.</summary>
    public const string TokenName = "Plant";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Insidious Roots with no live runtime services. The
    /// leaves-graveyard trigger is attached for shape inspection (NOT registered
    /// with a <see cref="TriggerManager"/>, so it does not fire on the bus); no
    /// live group mana-grant. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to for pure card-shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, effects: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (<c>NamedCardFactory.CreateGeneratedWithEffects</c>
    /// requires this exact <c>Create(Player, ContinuousEffectsService)</c>
    /// signature). Wires the Layer-6 "Creature tokens you control" group mana
    /// grant against the live service AND registers the leaves-graveyard
    /// triggered ability with the ambient <see cref="TriggerManager"/> so it
    /// fires on the bus. Without this overload the routed build would fall back
    /// to <see cref="Create(Player)"/> and drop both the group grant and the
    /// live trigger in production.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment type,
        // {B}{G}). The JSON carries no abilities — the group grant + the
        // leaves-graveyard trigger are layered on below.
        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // "Whenever one or more creature cards leave your graveyard, create a
        //  0/1 green Plant creature token, then put a +1/+1 counter on each
        //  Plant you control." (CR 603.2 — leaves-the-zone trigger.)
        //
        // Predicate mirrors the card_leaves_your_graveyard JSON trigger
        // (cardTypes [Creature]) used by Dredger's Insight: a CardMovedEvent
        // FROM the graveyard, of a card the trigger's controller owns, that is
        // a Creature card. CR 603.3b "one or more" batch wording collapses to
        // one trigger per move in v1 (same posture as Dredger's Insight).
        // ----------------------------------------------------------------
        var leavesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.FromZone != ZoneType.Graveyard) return false;
                // "Your graveyard" — the controller of this trigger's source.
                var triggerController = card.Controller;
                if (triggerController is null
                    || !ReferenceEquals(e.Card.Owner, triggerController))
                {
                    return false;
                }
                return e.Card.HasType(CardType.Creature);
            }),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: create a 0/1 green Plant, then +1/+1 on each Plant you control",
                    () => CreatePlantThenCounterPlants(card)),
            },
            // CR 603.6d / 700.4 — the trigger lives on the enchantment, which
            // stays on the battlefield, so battlefield-only active zone is
            // correct (the moving card is unrelated to this source).
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(leavesTrigger);

        if (effects != null)
        {
            card.ActiveEffects = effects;

            // CR 603 — register the leaves-graveyard trigger with the live
            // TriggerManager so it actually fires on CardMovedEvent. The
            // source never crosses a zone boundary on this grant, so the
            // manager's auto-bind on CardMovedEvent never sees it — register
            // explicitly (same posture as the hand-rolled trigger factories).
            TriggerManagerRegistry.Get()?.RegisterTriggeredAbility(leavesTrigger);

            // CR 613.1f — "Creature tokens you control have '{T}: Add one mana
            // of any color.'" Same Layer-6 group-grant machinery as Chromatic
            // Lantern / Enduring Vitality, scoped to every creature TOKEN this
            // card's controller controls. Live membership; the granted mana
            // abilities surface through EffectiveManaAbilities. Prefer an
            // explicit whole-battlefield gatherer (so a stolen token you
            // control but an opponent owns is enumerated — CR 110.2 / 700.6),
            // falling back to the controller's own battlefield (shape tests).
            var players = effects.PlayersProvider;
            var membership = players != null
                ? BattlefieldGroupGatherer.WholeBattlefield(players)
                : (Func<IEnumerable<Permanent>>)(() => ControllerBattlefield(card));

            var lifecycle = new GrantAbilityToGroupLifecycle(
                card,
                effects,
                effects.EventBus,
                scope: p => p is Creature
                            && p.IsToken
                            && ReferenceEquals(p.Controller, card.Controller),
                abilityFactory: member => BuildAnyColorMana(member, card),
                membershipProvider: membership);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Resolve the leaves-graveyard trigger: create a 0/1 green Plant creature
    /// token under the controller's control (CR 111.4), then put a +1/+1
    /// counter (CR 122) on EACH Plant the controller controls — including the
    /// just-created token, matching the card's "then" ordering. Exposed for
    /// direct invocation by tests.
    /// </summary>
    public static void CreatePlantThenCounterPlants(Enchantment card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var controller = card.Controller;
        if (controller is null) return;

        // CR 111.4 — a 0/1 green Plant creature token. Colour stamped green.
        // Route token creation through the controller's live ZoneService when
        // one is registered so CardMovedEvent fires (the group mana-grant +
        // any ETB triggers pick the token up); raw-zone fallback otherwise.
        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: 0,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Plant },
            Keywords: null,
            Colors: new[] { ManaColor.Green });

        TokenFactory.CreateOnBattlefield(
            spec, controller, ZoneServiceRegistry.Get(controller));

        // CR 122 — "then put a +1/+1 counter on each Plant you control." The
        // just-created token is itself a Plant the controller controls, so it
        // gets a counter too (becoming 1/2). Read the live battlefield fresh.
        foreach (var plant in controller.Zones.Battlefield.GetCards()
                     .OfType<Permanent>()
                     .Where(p => p.Zone == ZoneType.Battlefield
                                 && p.HasSubtype(CardSubtype.Plant)))
        {
            Fx.PlaceCounter(plant, CounterType.PlusOnePlusOne, 1);
        }
    }

    /// <summary>
    /// CR 605.1a — "Add one mana of any color" modelled as five single-colour
    /// <see cref="ManaAbility"/> instances. Each is sourced on the granted token
    /// <paramref name="member"/> and controlled by Insidious Roots' current
    /// controller, with the implicit {T} self-tap baked into the cost.
    /// </summary>
    private static IReadOnlyList<IAbility> BuildAnyColorMana(Permanent member, Enchantment source)
    {
        var controller = member.Controller ?? source.Controller
            ?? throw new InvalidOperationException(
                "Cannot grant any-colour mana ability: no controller set.");
        return new IAbility[]
        {
            new ManaAbility(member, controller, ManaCost.Parse("W")),
            new ManaAbility(member, controller, ManaCost.Parse("U")),
            new ManaAbility(member, controller, ManaCost.Parse("B")),
            new ManaAbility(member, controller, ManaCost.Parse("R")),
            new ManaAbility(member, controller, ManaCost.Parse("G")),
        };
    }

    /// <summary>
    /// Live candidate set for the group grant: every permanent on Insidious
    /// Roots' controller's battlefield. The <c>scope</c> predicate further
    /// filters to creature tokens the controller controls.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Enchantment source)
    {
        var controller = source.Controller;
        if (controller is null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
