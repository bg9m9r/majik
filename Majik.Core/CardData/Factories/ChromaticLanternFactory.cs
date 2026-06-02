using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chromatic Lantern (Guilds of Ravnica, {3} Artifact).
///
/// Oracle text (verified against Scryfall):
///   "Lands you control have "{T}: Add one mana of any color."
///    {T}: Add one mana of any color."
///
/// ## Implementation
///
/// - Artifact body / identity + the lantern's OWN "{T}: Add one mana of any
///   color" — five <see cref="ManaAbility"/> instances (one per WUBRG),
///   carried in <c>Majik.Core/CardData/Cards/chromatic-lantern.json</c> as
///   five <c>{ "kind": "mana" }</c> entries. Same any-colour modelling as
///   Manalith (CR 605.1a — "any color" = five distinct single-colour mana
///   abilities).
///
/// - "Lands you control have '{T}: Add one mana of any color.'" — a
///   CR 613.1f Layer-6 group ability-grant via
///   <see cref="GrantAbilityToGroupStaticEffect"/>, wired by
///   <see cref="GrantAbilityToGroupLifecycle"/>. Scope = every Land the
///   lantern's controller controls; each member is granted five fresh
///   single-colour <see cref="ManaAbility"/> instances. Live membership is
///   recomputed as lands enter / leave (CR 611.2c); the granted abilities
///   surface through <see cref="EffectiveManaAbilities"/> so each land taps
///   for any colour.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so the
/// group grant is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces the card
/// with correct identity + the lantern's own any-colour ability but no live
/// group grant — suitable for pure card-shape tests.
/// </summary>
[CardName("Chromatic Lantern")]
public static class ChromaticLanternFactory
{
    public const string CardName = "Chromatic Lantern";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("chromatic-lantern");

    /// <summary>
    /// Creates a Chromatic Lantern with correct identity + its own any-colour
    /// mana ability (no live group grant). Suitable for factory-shape /
    /// naming tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Chromatic Lantern. When <paramref name="effects"/>
    /// is supplied, a <see cref="GrantAbilityToGroupLifecycle"/> is attached so
    /// the Layer-6 group grant registers / unregisters as the lantern enters /
    /// leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null the
    /// lifecycle wiring is silently skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var lantern = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        if (effects != null)
        {
            // CR 613.1f — "Lands you control have '{T}: Add one mana of any
            // color.'" Grant five single-colour mana abilities to every Land
            // the lantern's controller controls; live membership.
            var lifecycle = new GrantAbilityToGroupLifecycle(
                lantern,
                effects,
                eventBus,
                scope: p => p is Land && ReferenceEquals(p.Controller, lantern.Controller),
                abilityFactory: member => BuildAnyColorMana(member, lantern),
                membershipProvider: () => ControllerBattlefield(lantern));
            lifecycle.Attach();
        }

        return lantern;
    }

    /// <summary>
    /// CR 605.1a — "Add one mana of any color" modelled as five single-colour
    /// <see cref="ManaAbility"/> instances. Each is sourced on the granting
    /// land <paramref name="member"/> and controlled by the lantern's current
    /// controller, with the implicit {T} self-tap baked into the cost.
    /// </summary>
    private static IReadOnlyList<IAbility> BuildAnyColorMana(Permanent member, Artifact lantern)
    {
        var controller = member.Controller ?? lantern.Controller
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
    /// Live candidate set for the group grant: every permanent on the
    /// lantern controller's battlefield. The <c>scope</c> predicate further
    /// filters to lands the controller controls.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Artifact lantern)
    {
        var controller = lantern.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
