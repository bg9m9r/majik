using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enduring Vitality (Duskmourn: House of Horror,
/// {1}{G}{G}). Enchantment Creature — Elk Glimmer 3/3. Oracle text (verified
/// against Scryfall):
///   "Vigilance
///    Creatures you control have "{T}: Add one mana of any color."
///    When Enduring Vitality dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// The base shape (name, Creature + Enchantment types, Elk + Glimmer subtypes,
/// {1}{G}{G}, 3/3) is materialised from the embedded JSON definition
/// (<c>enduring-vitality.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the Vigilance marker, the group mana-grant, and the dies → return-as-
/// enchantment trigger are layered on here (same JSON-backed-identity +
/// code-attached-behaviour posture as <see cref="EnduringCuriosityFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Vigilance (CR 702.21)</b>: a <see cref="KeywordAbility"/> marker (same
///   marker-keyword posture used for parsed keywords on JSON-backed creatures).
///   The combat-attack path reads this marker so the creature does not tap to
///   attack.
///
/// - <b>"Creatures you control have '{T}: Add one mana of any color.'"
///   (CR 613.1f)</b>: a Layer-6 group ability-grant via
///   <see cref="GrantAbilityToGroupStaticEffect"/>, wired by
///   <see cref="GrantAbilityToGroupLifecycle"/> — the SAME machinery Chromatic
///   Lantern uses for "Lands you control have …", differing only in the scope
///   predicate (every Creature this card's controller controls, INCLUDING
///   Enduring Vitality itself, rather than every Land). Each member is granted
///   five fresh single-colour <see cref="ManaAbility"/> instances (CR 605.1a —
///   "any color" = five distinct single-colour mana abilities); live membership
///   is recomputed as creatures enter / leave / change control (CR 611.2c), and
///   the granted mana abilities surface through
///   <see cref="EffectiveManaAbilities"/> so each creature taps for any colour.
///
/// - <b>Dies → return as an enchantment (CR 603.6c / 700.4 / 701.20 /
///   205.2 / 613.1d)</b>: identical shape to
///   <see cref="EnduringCuriosityFactory"/> — a <see cref="TriggeredAbility"/>
///   over <see cref="Triggers.OnDies"/> with <c>activeZones = {Battlefield,
///   Graveyard}</c> so the trigger survives the death zone-move. On resolution
///   the card is returned from the graveyard to the battlefield under its
///   owner's control (<see cref="Fx.ReturnFromGraveyardToBattlefield"/>) and a
///   captured <c>hasReturned</c> flag flips true, gating a
///   <see cref="Layer4TypeStripEffect"/> that strips
///   <see cref="CardType.Creature"/> ("It's an enchantment. (It's not a
///   creature.)"). The intervening-if "if it was a creature" is satisfied on
///   the first death (still a creature) and fails on a subsequent death once it
///   has already returned as a non-creature enchantment.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real Vigilance combat behaviour</b> is supplied by the combat path
///   reading the Vigilance marker; the factory only attaches the marker.
/// </summary>
[CardName("Enduring Vitality")]
public static class EnduringVitalityFactory
{
    public const string CardName = "Enduring Vitality";
    public const string Slug = "enduring-vitality";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Enduring Vitality with no live runtime services. The Vigilance
    /// marker + the dies trigger are attached for shape inspection (no live
    /// group mana-grant, no type-strip). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to for pure card-shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, zoneService: null, allPlayersProvider: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (<c>NamedCardFactory.CreateGeneratedWithEffects</c>
    /// requires this exact <c>Create(Player, ContinuousEffectsService)</c>
    /// signature). Wires both the Layer-6 "Creatures you control" group mana
    /// grant AND the Layer-4 dies → type-strip against the live service, taking
    /// the event bus + players provider off the service so the grant tracks
    /// creatures entering / leaving / changing control across both battlefields
    /// (CR 110.2 / 611.2c). Without this overload the routed build would fall
    /// back to <see cref="Create(Player)"/> and drop the group grant in
    /// production.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
        => Create(owner, effects, eventBus: effects?.EventBus, zoneService: null, allPlayersProvider: null);

    /// <summary>
    /// Construct Enduring Vitality with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">When supplied, (a) the Layer-6 "Creatures you
    /// control have '{T}: Add one mana of any color.'" group grant is wired via
    /// a <see cref="GrantAbilityToGroupLifecycle"/>, and (b) the
    /// <see cref="Layer4TypeStripEffect"/> backing "It's an enchantment. (It's
    /// not a creature.)" is registered (gated OFF until the dies trigger
    /// returns the card). When null, neither continuous effect is modelled
    /// (shape-only path used by identity / trigger-shape tests).</param>
    /// <param name="eventBus">Bus the group-grant lifecycle subscribes for
    /// <see cref="CardMovedEvent"/> so membership tracks zone moves. May be
    /// null.</param>
    /// <param name="zoneService">When supplied, the dies trigger's graveyard →
    /// battlefield return routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a); raw-zone fallback otherwise.</param>
    /// <param name="allPlayersProvider">Whole-battlefield candidate gatherer for
    /// the group grant (so a creature you control but an opponent owns is
    /// enumerated — CR 110.2 / 700.6). Falls back to the service's
    /// <see cref="ContinuousEffectsService.PlayersProvider"/>, then to the
    /// controller's own battlefield.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        ZoneService? zoneService,
        System.Func<System.Collections.Generic.IEnumerable<Player>?>? allPlayersProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Elk + Glimmer subtypes, {1}{G}{G}, 3/3). The JSON
        // carries no abilities — Vigilance + the group grant + the dies trigger
        // are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.21 — Vigilance. Marker keyword read by the combat-attack path.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // Captured "the card has returned and is now a non-creature
        // enchantment" flag. Flipped true by the dies trigger after the return;
        // read by both the Layer-4 type-strip predicate and the dies trigger's
        // intervening-if re-check.
        var hasReturned = false;

        // ----------------------------------------------------------------
        // "When Enduring Vitality dies, if it was a creature, return it to the
        //  battlefield under its owner's control. It's an enchantment. (It's
        //  not a creature.)" (CR 603.6c / 700.4 / 701.20 / 205.2 / 613.1d).
        // ----------------------------------------------------------------
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: dies — if it was a creature, return it as a (non-creature) enchantment",
                    () => ReturnAsEnchantment(card, zoneService, ref hasReturned)),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        card.AddAbility(diesTrigger);

        if (effects != null)
        {
            // CR 205.2 / 613.1d — Layer-4 type-strip backing "It's an
            // enchantment. (It's not a creature.)" Registered up-front, gated
            // OFF by the captured hasReturned flag, so the card is a normal
            // creature until the dies trigger returns it.
            card.ActiveEffects = effects;
            effects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () => hasReturned));

            // CR 613.1f — "Creatures you control have '{T}: Add one mana of any
            // color.'" Same Layer-6 group-grant machinery as Chromatic Lantern,
            // scoped to every Creature this card's controller controls
            // (including Enduring Vitality itself). Live membership; the granted
            // mana abilities surface through EffectiveManaAbilities. Prefer an
            // explicit whole-battlefield gatherer (so a stolen creature you
            // control but an opponent owns is enumerated — CR 110.2 / 700.6),
            // falling back to the service's players provider, then to the
            // controller's own battlefield (pure card-shape tests).
            var players = allPlayersProvider ?? effects.PlayersProvider;
            var membership = players != null
                ? BattlefieldGroupGatherer.WholeBattlefield(players)
                : (System.Func<System.Collections.Generic.IEnumerable<Permanent>>)(() => ControllerBattlefield(card));

            var lifecycle = new GrantAbilityToGroupLifecycle(
                card,
                effects,
                eventBus,
                scope: p => p is Creature && ReferenceEquals(p.Controller, card.Controller),
                abilityFactory: member => BuildAnyColorMana(member, card),
                membershipProvider: membership);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Resolve the dies trigger: if the card was a creature when it died,
    /// return it from the graveyard to the battlefield under its owner's
    /// control and flip <paramref name="hasReturned"/> so the Layer-4
    /// type-strip engages. Exposed for direct invocation by tests.
    /// </summary>
    public static void ReturnAsEnchantment(
        Creature card,
        ZoneService? zoneService,
        ref bool hasReturned)
    {
        ArgumentNullException.ThrowIfNull(card);

        // CR 603.6c — intervening "if": only return if it was still a creature
        // when it died. Once it has already returned as a (non-creature)
        // enchantment, a subsequent death fails this check, so it stays put.
        if (hasReturned) return;

        // CR 608.2 — the card must still be in the graveyard at resolution.
        if (card.Zone != ZoneType.Graveyard) return;

        var owner = card.Owner;
        if (owner == null) return;

        // CR 701.20 — graveyard → battlefield under its owner's control.
        Fx.ReturnFromGraveyardToBattlefield(card, owner, zoneService);
        if (card.Zone != ZoneType.Battlefield) return;

        // CR 205.2 / 613.1d — from now on "It's an enchantment. (It's not a
        // creature.)" The Layer4TypeStripEffect registered at construction
        // reads this flag and strips the Creature type on every Compute pass.
        hasReturned = true;
    }

    /// <summary>
    /// CR 605.1a — "Add one mana of any color" modelled as five single-colour
    /// <see cref="ManaAbility"/> instances. Each is sourced on the granting
    /// creature <paramref name="member"/> and controlled by Enduring Vitality's
    /// current controller, with the implicit {T} self-tap baked into the cost.
    /// </summary>
    private static IReadOnlyList<IAbility> BuildAnyColorMana(Permanent member, Creature source)
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
    /// Live candidate set for the group grant: every permanent on Enduring
    /// Vitality's controller's battlefield. The <c>scope</c> predicate further
    /// filters to creatures the controller controls.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Creature source)
    {
        var controller = source.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
