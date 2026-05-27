using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voice of Resurgence (Dragon's Maze, {G}{W}).
///
/// Creature — Elemental 2/2. Oracle text:
///   "Whenever an opponent casts a spell during your turn or in response to
///    a spell you cast, create a 0/1 green and white Elemental creature
///    token.
///    When Voice of Resurgence dies, create an X/X green and white
///    Elemental creature token, where X is the number of creatures you
///    control."
///
/// ## Implemented (v1)
/// - 2/2 Elemental, mana cost {G}{W}.
/// - <b>Cast trigger (CR 603.1)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> fires whenever an opponent casts a spell
///   AND one of:
///   <list type="bullet">
///     <item>the spell is cast during Voice's controller's turn — detected
///     via the optional <see cref="TurnManager"/> service supplied at
///     construction (<see cref="TurnManager.ActivePlayer"/> matches Voice's
///     controller); OR</item>
///     <item>the controller has any spell on the stack — detected via the
///     optional <see cref="Majik.Core.Stack.Stack"/> service ("in response
///     to a spell you cast"; v1 reads any controller-owned object on the
///     stack, not just the topmost, since responding to a chained spell
///     still counts).</item>
///   </list>
///   On resolution, create a 0/1 green-and-white Elemental creature token
///   under Voice's controller (CR 105 / CR 111.4 — colours stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/>).
/// - <b>Dies trigger (CR 603.6c / 700.4)</b>: When Voice of Resurgence
///   moves Battlefield → Graveyard, create one X/X green-and-white
///   Elemental creature token where X = the count of creatures Voice's
///   controller controls at resolution. Counters are snapshotted at
///   effect-execution time — the count reflects "creatures you control"
///   per CR 109.5 / CR 121 (no "at resolution" rider needed, the effect
///   queries live state when fired). The token is a vanilla Elemental:
///   no triggered abilities of its own, no keywords.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; triggers attached but not
///   registered with a trigger manager. Active-player + stack gates are
///   loose (any opponent spell will satisfy the cast-trigger predicate).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?, TurnManager?, Majik.Core.Stack.Stack?)"/>
///   — full wiring. When <paramref name="turnManager"/> AND
///   <paramref name="stack"/> are both supplied, the cast-trigger predicate
///   enforces the "during your turn OR controller has spell on stack"
///   conjunction precisely. When EITHER is null, the missing half is
///   treated as "matches" (loose posture matching the deferred-services
///   defaults across the rest of the factory family).
///
/// ## Deferred (v1 gaps)
/// - <b>"In response to a spell you cast"</b>: the precise printed text
///   is "in response to a spell you cast" — Voice's trigger condition
///   strictly requires that the opponent's spell be cast while a
///   controller-cast spell is still on the stack waiting to resolve. v1
///   approximates this by checking "controller has any spell on stack" —
///   if the controller has any of their own spells on the stack at the
///   moment the opponent casts, the trigger fires. This over-fires only
///   when the controller's stack-resident object is an ability they
///   activated (not a spell they cast) — flagged for the broader
///   "is-this-a-spell vs ability" stack-introspection retrofit.
/// - <b>Choose-which-creature-counts for X</b>: the dies trigger
///   snapshots the creature count at effect-execution time. The Voice
///   token itself is gone by the time the trigger resolves (it moved
///   Battlefield → Graveyard before the trigger goes on the stack —
///   CR 603.6c / 700.4), so it does not contribute to X. The count is
///   purely whatever creatures the controller has on the battlefield
///   when the trigger resolves.
/// - <b>Token Elemental colour identity</b>: stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/> as Green + White.
///   Hybrid-pip-aware colour-identity surfaces remain a wider deferred
///   gap shared with the rest of the colour system.
/// </summary>
[CardName("Voice of Resurgence")]
public static class VoiceOfResurgenceFactory
{
    public const string CardName = "Voice of Resurgence";
    public const string PrintedManaCost = "{G}{W}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const string TokenName = "Elemental";
    public const int CastTokenPower = 0;
    public const int CastTokenToughness = 1;

    /// <summary>
    /// Construct Voice of Resurgence with no live wiring (shape /
    /// dispatcher path). Both triggered abilities are attached but not
    /// registered with a trigger manager. Without a
    /// <see cref="TurnManager"/> or <see cref="Majik.Core.Stack.Stack"/>
    /// the cast-trigger gate is loose — any opponent's spell will match.
    /// Suitable for shape / unit tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null,
            turnManager: null, stack: null);

    /// <summary>
    /// Construct Voice of Resurgence with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers
    /// (not used directly).</param>
    /// <param name="triggers">TriggerManager for the cast + dies triggers.
    /// May be null — both triggers are still attached to the card shape.</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires (so Soul Warden etc. see the tokens enter).
    /// Pass null for raw zone moves.</param>
    /// <param name="turnManager">Optional turn manager — when supplied,
    /// the cast trigger enforces "during your turn" via
    /// <see cref="TurnManager.ActivePlayer"/>. When null, the active-turn
    /// half of the predicate is loose.</param>
    /// <param name="stack">Optional stack — when supplied, the cast
    /// trigger enforces "in response to a spell you cast" via a live
    /// scan of stack objects for controller ownership. When null, the
    /// in-response half is loose.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        TurnManager? turnManager,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever an opponent casts a spell during your turn or in
        //    response to a spell you cast, create a 0/1 green and white
        //    Elemental creature token."
        //
        // Predicate (per CR 109.5 / CR 603.6a):
        //   (1) spell controller != Voice's controller   (opponent gate)
        //   (2) (turnManager.ActivePlayer == controller) OR
        //       (any stack object's Controller == controller)
        //
        // When turnManager/stack are null, the respective half collapses
        // to "matches" — loose posture for shape tests.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;

            // (1) Opponent-cast gate (CR 109.5 — "an opponent" reads
            //     against the trigger's controller).
            if (ReferenceEquals(e.Spell.Controller, controller)) return false;

            // (2a) "During your turn" — TurnManager.ActivePlayer == controller.
            //      Loose when turnManager is null (treat as matched).
            var duringControllersTurn =
                turnManager == null
                || ReferenceEquals(turnManager.ActivePlayer, controller);

            // (2b) "In response to a spell you cast" — controller has any
            //      object on the stack (v1 approximation — see class
            //      xmldoc for the spell-vs-ability deferred gap).
            //      Loose when stack is null (treat as not matched here so
            //      the disjunction collapses to (2a) only).
            var inResponseToYourSpell =
                stack != null
                && stack.GetAll().Any(obj =>
                    ReferenceEquals(obj.Controller, controller));

            return duringControllersTurn || inResponseToYourSpell;
        });

        var castEffect = new Effect(
            $"{CardName}: create a 0/1 G/W Elemental creature token",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateElementalToken(
                    controller,
                    CastTokenPower,
                    CastTokenToughness,
                    zoneService);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            // CR 603.6a — cast trigger only active while Voice is on the
            // battlefield. Casting Voice itself never triggers it (Voice
            // is on the stack when its own SpellCastEvent fires).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / 700.4.
        //   "When Voice of Resurgence dies, create an X/X green and white
        //    Elemental creature token, where X is the number of creatures
        //    you control."
        //
        // X is snapshotted at effect execution. Voice itself is in the
        // graveyard by then (CR 700.4) so it does not contribute. The
        // token enters fresh with P/T baked from the live count — it is
        // not a *X printed in the cost, so no "cost is X" plumbing is
        // needed (CR 107.3).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: create an X/X G/W Elemental token (X = creatures you control)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var x = CountCreaturesControlled(controller);
                CreateElementalToken(controller, x, x, zoneService);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // activeZones: Battlefield + Graveyard so the trigger still
            // matches after ZoneService stamps card.Zone = Graveyard
            // before publishing the CardMovedEvent (same shape as
            // Wurmcoil Engine).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 — create a single Elemental creature token under
    /// <paramref name="controller"/>. Colours stamped Green + White
    /// (CR 105 / CR 111.4).
    /// </summary>
    public static Creature CreateElementalToken(
        Player controller,
        int power,
        int toughness,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: power,
            Toughness: toughness,
            Subtypes: new[] { CardSubtype.Elemental },
            Keywords: null,
            Colors: new[] { ManaColor.Green, ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }

    /// <summary>
    /// Count Creature permanents that <paramref name="controller"/>
    /// currently controls on the battlefield. Used by the dies trigger
    /// to compute X for the X/X Elemental token (CR 109.5 / CR 121).
    /// Exposed publicly so tests + bots can inspect the live count
    /// without firing the trigger.
    /// </summary>
    public static int CountCreaturesControlled(Player controller)
    {
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Creature)
                && ReferenceEquals(c.Controller, controller));
    }
}
