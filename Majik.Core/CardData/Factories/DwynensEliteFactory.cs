using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dwynen's Elite (Magic Origins, {1}{G}). Creature —
/// Elf Warrior 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, if you control another Elf, create a 1/1
///    green Elf Warrior creature token."
///
/// The card's base shape (name, Creature, Elf/Warrior subtypes, {1}{G}, 2/2)
/// is materialised from the embedded JSON definition (<c>dwynens-elite.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single ETB triggered ability
/// is layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express token-creation effects or intervening-if conditions, so it lives in
/// the factory (same posture as <see cref="BladeSplicerFactory"/>, whose ETB
/// also mints a token from code over a JSON-backed shell).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Warrior at {1}{G}.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a <see cref="CardMovedEvent"/>
///   filtered to (this card, ToZone = Battlefield).
/// - <b>Intervening-if (CR 603.4)</b>: "if you control another Elf". The
///   condition is checked TWICE per CR 603.4 — once when the event would put
///   the ability on the stack, and again as the ability resolves. v1 evaluates
///   the predicate at resolve time inside the effect body (same posture as
///   <see cref="RecklessBushwhackerFactory"/>'s "if its surge cost was paid"
///   intervening-if): the resolve body counts the Elves the controller controls
///   on the battlefield, EXCLUDING Dwynen's Elite itself ("another Elf" — CR
///   109.5 "you" + an explicit self-exclusion), and only mints the token if at
///   least one other Elf is present. If no other Elf is controlled, the body
///   no-ops.
/// - <b>Token (CR 111 / 111.4)</b>: a 1/1 GREEN Elf Warrior creature token
///   under Dwynen's Elite's controller via <see cref="CreateElfWarriorToken"/>.
///   The printed token is green (not colourless), so the spec carries
///   <see cref="ManaColor.Green"/>. Same token-mint plumbing as
///   <see cref="BladeSplicerFactory.CreatePhyrexianGolemToken"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Two-check intervening-if on the stack</b>: the first check (gate onto
///   the stack) is collapsed into the single resolve-time read. Observationally
///   equivalent for this card — the only state read is "do I control another
///   Elf", and the resolve-time read is the rules-authoritative one (CR 603.4 —
///   if the condition is false on resolution the ability does nothing). Same
///   shape as <see cref="RecklessBushwhackerFactory"/>.
/// - <b>Token ETB CardMovedEvent</b>: without a <see cref="ZoneService"/>
///   threaded in, the Elf Warrior token enters via the raw zone branch and does
///   NOT publish <see cref="CardMovedEvent"/>. Same gap as
///   <see cref="BladeSplicerFactory"/> without zones.
/// </summary>
[CardName("Dwynen's Elite")]
public static class DwynensEliteFactory
{
    public const string CardName = "Dwynen's Elite";
    public const string Slug = "dwynens-elite";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Dwynen's Elite with its ETB trigger attached to the card shape
    /// but NOT registered with a <see cref="TriggerManager"/> (no
    /// <see cref="ZoneService"/> wiring). Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Dwynen's Elite with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Elf Warrior token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so <see cref="CardMovedEvent"/>
    /// publishes for any zone-change subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with the
    /// bus so the corresponding <see cref="CardMovedEvent"/> lands the ability on
    /// the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf/Warrior subtypes, {1}{G}, 2/2). The JSON carries no abilities —
        // the ETB token trigger is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + intervening-if CR 603.4.
        //   "When this creature enters, if you control another Elf, create a
        //    1/1 green Elf Warrior creature token."
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: if you control another Elf, create a 1/1 green Elf Warrior token",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 603.4 intervening-if — read at resolution: does the
                // controller control ANOTHER Elf (one that isn't this card)?
                if (!ControlsAnotherElf(controller, card)) return;

                CreateElfWarriorToken(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 603.4 — true iff <paramref name="controller"/> controls an
    /// Elf permanent on the battlefield OTHER than <paramref name="self"/>.
    /// "Another" excludes the source itself by reference identity.
    /// </summary>
    public static bool ControlsAnotherElf(Player controller, ICard self)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Elf));
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 GREEN Elf Warrior creature token
    /// under <paramref name="controller"/>'s control. The printed token is
    /// green (not colourless), so the spec carries
    /// <see cref="ManaColor.Green"/>. Same token-mint plumbing as
    /// <see cref="BladeSplicerFactory.CreatePhyrexianGolemToken"/>.
    /// </summary>
    public static Creature CreateElfWarriorToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Elf Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior },
            // CR 105 / CR 111.4 — printed "1/1 green Elf Warrior creature token".
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
