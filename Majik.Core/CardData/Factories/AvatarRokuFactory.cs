using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avatar Roku — the back face of the transforming Saga
/// The Legend of Roku // Avatar Roku (Avatar: The Last Airbender).
///
/// Legendary Creature — Avatar 4/4. Oracle text:
///   "Firebending 4 (Whenever this creature attacks, add {R}{R}{R}{R}. This
///    mana lasts until end of combat.)
///   {8}: Create a 4/4 red Dragon creature token with flying and
///    firebending 4."
///
/// ## Implemented
/// - 4/4 Legendary <see cref="Creature"/> — Avatar, red (red comes from the
///   factory's explicit colour stamp — the card has no printed mana cost on
///   the back face, so the colour is asserted directly).
/// - <see cref="MdfcState"/> attached (front = "The Legend of Roku",
///   back = "Avatar Roku") pre-flipped to the back face — Avatar Roku only
///   ever exists as the transformed (back) face on the battlefield
///   (CR 712.4). <see cref="TheLegendOfRokuFactory"/>'s chapter III builds
///   this permanent when the Saga transforms.
/// - <b>Firebending 4</b> — a NEW attack-triggered keyword (CR 508.1f). When
///   this creature attacks it adds {R}{R}{R}{R} to its controller's pool. The
///   "this mana lasts until end of combat" rider is modelled as a one-shot
///   <see cref="EndOfCombat"/> <see cref="StepStartedEvent"/> subscription
///   that removes up to that many red mana still floating (the engine empties
///   the pool only at Cleanup, so without this hook the firebending mana would
///   incorrectly survive into the postcombat main / end step). See
///   <see cref="AttachFirebending"/>.
/// - <b>{8}: create a 4/4 red Dragon token with flying + firebending 4</b> —
///   an <see cref="ActivatedAbility"/> (CR 602) that mints a 4/4 red Dragon
///   token (<see cref="TokenFactory"/>) with Flying and the same firebending-4
///   attack trigger attached.
///
/// ## Deferred (v1 gaps)
/// - <b>Stack-driven firebending trigger</b>: the firebending mana is added
///   when the attack trigger resolves; the per-pip "until end of combat"
///   accounting removes red mana in bulk (the pool has no per-slot provenance,
///   same posture as Arena of Glory's haste-mana side-channel).
/// - <b>Mana spent constraint</b>: real firebending mana can only be spent
///   during combat; v1 simply expires it at end of combat (a strict superset
///   of the printed timing in this engine's coarse pool model).
/// </summary>
[CardName("Avatar Roku")]
public static class AvatarRokuFactory
{
    public const string FrontName = "The Legend of Roku";
    public const string CardName = "Avatar Roku";
    public const int Power = 4;
    public const int Toughness = 4;
    public const int FirebendingAmount = 4;
    public const string DragonTokenCost = "{8}";

    /// <summary>Construct Avatar Roku with no live runtime wiring. The
    /// firebending attack trigger and the {8} activated ability are attached
    /// structurally for shape tests; without a trigger manager the firebending
    /// trigger is not registered and without a bus the firebending mana is not
    /// auto-expired.</summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>Construct Avatar Roku with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the {8}
    /// Dragon-token ETB through <see cref="ZoneService"/>.</param>
    /// <param name="eventBus">Optional event bus — drives the firebending
    /// "until end of combat" mana expiry on the controller's
    /// <see cref="PhaseStateType.EndOfCombat"/> step.</param>
    /// <param name="triggers">Optional trigger manager — registers the
    /// firebending attack trigger (and the Dragon token's firebending
    /// trigger).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 712.4 — Avatar Roku is a 4/4 Legendary Creature — Avatar.
        var card = new Creature(
            name: CardName,
            manaCost: "",
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Avatar });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 105 — the back face is red; the printed mana cost is empty so
        // stamp the colour explicitly (same posture as token colour stamping).
        card.SetTokenColors(new[] { ManaColor.Red });

        // CR 712 — this face only exists as the transformed back face.
        card.MdfcState = new MdfcState(FrontName, CardName);
        if (!card.MdfcState.IsBackFace) card.MdfcState.Transform();

        // Firebending 4 — attack trigger adding {R}{R}{R}{R} until end of combat.
        AttachFirebending(card, owner, FirebendingAmount, eventBus, triggers);

        // {8}: create a 4/4 red Dragon token with flying + firebending 4.
        AddDragonTokenAbility(card, owner, zoneService, eventBus, triggers);

        return card;
    }

    /// <summary>
    /// Attach <b>Firebending N</b> to <paramref name="creature"/> — a NEW
    /// attack-triggered keyword: "Whenever this creature attacks, add N {R}.
    /// This mana lasts until end of combat." (CR 508.1f / CR 106.1).
    ///
    /// The mana is added to the controller's pool when the attack trigger
    /// resolves. The "lasts until end of combat" rider is modelled with a
    /// one-shot <see cref="PhaseStateType.EndOfCombat"/> subscription that
    /// removes up to N red mana still floating in the pool (clamped to what's
    /// present so only the unspent firebending mana is removed). This is the
    /// minimal correct end-of-combat mana duration — the engine otherwise
    /// empties the pool only at Cleanup (CR 500.4), so without this the mana
    /// would survive past combat. Shares the one-shot subscribe/unsubscribe
    /// shape used by Arena of Glory and Light Up the Stage.
    /// </summary>
    public static void AttachFirebending(
        Creature creature,
        Player owner,
        int amount,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(owner);

        var redMana = ManaCost.Parse(new string('R', Math.Max(0, amount)));

        var firebendEffect = new Effect(
            $"Firebending {amount}: add {amount} red mana until end of combat",
            () =>
            {
                var controller = creature.Controller ?? owner;
                controller.AddManaToPool(redMana);

                if (eventBus == null || amount <= 0) return;

                // CR 500.4 — "this mana lasts until end of combat." Remove up
                // to `amount` red mana when the controller's end-of-combat step
                // begins (one-shot). Clamp to red currently floating so only
                // the unspent firebending mana is removed.
                Action<StepStartedEvent>? handler = null;
                handler = e =>
                {
                    if (e.StepType != PhaseStateType.EndOfCombat) return;
                    if (!ReferenceEquals(e.Player, controller)) return;

                    var removable = Math.Min(amount, controller.ManaPool.Red);
                    if (removable > 0)
                        controller.PayMana(ManaCost.Parse(new string('R', removable)));

                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        var attackTrigger = new TriggeredAbility(
            source: creature,
            controller: owner,
            condition: Triggers.OnAttackSelf(creature),
            effects: new IEffect[] { firebendEffect },
            activeZones: new[] { ZoneType.Battlefield });

        creature.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    /// <summary>
    /// Attach "{8}: Create a 4/4 red Dragon creature token with flying and
    /// firebending 4." (CR 602). The minted Dragon carries a
    /// <see cref="KeywordAbility"/>("Flying") and the same firebending-4
    /// attack trigger via <see cref="AttachFirebending"/>.
    /// </summary>
    public static void AddDragonTokenAbility(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        var makeDragon = new Effect(
            "Avatar Roku: create a 4/4 red Dragon token with flying + firebending 4",
            () =>
            {
                var controller = card.Controller ?? owner;
                var dragon = TokenFactory.CreateOnBattlefield(
                    new TokenFactory.TokenSpec(
                        Name: "Dragon",
                        Power: 4,
                        Toughness: 4,
                        Subtypes: new[] { CardSubtype.Dragon },
                        Keywords: new[] { "Flying" },
                        Colors: new[] { ManaColor.Red }),
                    controller,
                    zoneService);

                // The token also has firebending 4.
                AttachFirebending(dragon, controller, FirebendingAmount, eventBus, triggers);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ManaCost.Parse(DragonTokenCost)) },
            effects: new IEffect[] { makeDragon });

        card.AddAbility(ability);
    }
}
