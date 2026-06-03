using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Usher of the Fallen (Kaldheim, {W}). Creature —
/// Spirit Warrior 2/1. Oracle text (verified against Scryfall):
///   "Boast — {1}{W}: Create a 1/1 white Human Warrior creature token.
///    (Activate only if this creature attacked this turn and only once each
///    turn.)"
///
/// ## Boast keyword (CR 702.135)
///
/// Boast is an activated-ability keyword: the printed cost on one side of a
/// colon, the effect on the other, plus the built-in restriction "Activate only
/// if this creature attacked this turn and only once each turn" (CR 702.135b/c).
/// The whole ability — cost, effect, and the two-part activation gate — is built
/// by the reusable <see cref="BoastAbility.Build"/> helper, which wires the gate
/// onto the existing <see cref="ActivatedAbility"/> <c>canActivateCheck</c> seam.
///
/// The per-turn cap is resolved through
/// <see cref="BoastAbility.ControllerCapResolver"/> so Birgi, God of
/// Storytelling's "Creatures you control can boast twice during each of your
/// turns rather than once" static raises Usher's cap from 1 to 2 when the
/// controller controls a Birgi.
///
/// ## Implemented (v1)
/// - 2/1 Creature — Spirit Warrior at {W}.
/// - Boast — {1}{W}: create a 1/1 white Human Warrior creature token, gated on
///   "attacked this turn" + "only once each turn" (cap honours Birgi's static).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — the <see cref="NamedCardFactory"/> dispatch
///   target. The Boast ability is mounted but, without a live
///   <see cref="IEventBus"/>, its "attacked this turn" cell never flips, so the
///   gate stays closed (same shape-only posture as
///   <see cref="CombatCelebrantFactory.Create(Player)"/>).
/// - <see cref="Create(Player, IEventBus?, ZoneService?)"/> — fully wired: the
///   gate observes <see cref="AttackersDeclaredEvent"/> /
///   <see cref="Domain.DomainEvents.AbilityActivatedEvent"/> /
///   <see cref="TurnStartedEvent"/> on the supplied bus, and the token enters
///   through the supplied <see cref="ZoneService"/> so its ETB
///   <see cref="CardMovedEvent"/> fires.
///
/// ## Deferred (v1 gaps, isolated)
/// - Live-bus Boast wiring is only present on the
///   <see cref="Create(Player, IEventBus?, ZoneService?)"/> overload; the
///   single-arg dispatch path leaves the gate shape-only (same posture as
///   Combat Celebrant / Glorybringer's exert).
/// </summary>
[CardName("Usher of the Fallen")]
public static class UsherOfTheFallenFactory
{
    public const string CardName = "Usher of the Fallen";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const string BoastCost = "{1}{W}";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Dispatch target — shape only (Boast gate inert without a bus).</summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, zones: null);

    /// <summary>Construct Usher of the Fallen with optional live services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the Boast gate observes
    /// attack / activation / turn-start events so it functions in a real
    /// match.</param>
    /// <param name="zones">When supplied, the Human Warrior token enters
    /// through the zone service so its ETB <see cref="CardMovedEvent"/>
    /// fires.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            CardName, PrintedManaCost, Power, Toughness,
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Warrior });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.135 — "Boast — {1}{W}: Create a 1/1 white Human Warrior
        // creature token." The cap honours Birgi's "boast twice" static.
        var boastEffect = new Effect(
            $"{CardName}: Boast — create a 1/1 white Human Warrior creature token",
            () => CreateHumanWarriorToken(card.Controller ?? owner, zones));

        var boast = BoastAbility.Build(
            card,
            BoastCost,
            new IEffect[] { boastEffect },
            eventBus,
            perTurnCap: BoastAbility.ControllerCapResolver(card));

        card.AddAbility(boast);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 WHITE Human Warrior creature token under
    /// <paramref name="controller"/>'s control. The printed token is white (not
    /// colourless), so the spec carries <see cref="ManaColor.White"/>.
    /// </summary>
    public static Creature CreateHumanWarriorToken(Player controller, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Human Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior },
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
