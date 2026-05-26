using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Liliana, the Last Hope (Eldritch Moon, {1}{B}{B}).
///
/// Legendary Planeswalker — Liliana, starting loyalty 3.
/// Oracle text (Scryfall, verified):
///   "+1: Up to one target creature gets -2/-1 until your next turn.
///    −2: Return up to two target creature cards from your graveyard to
///         your hand.
///    −7: You get an emblem with 'At the beginning of your end step,
///         create two 2/2 black Zombie creature tokens.'"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker — Liliana at {1}{B}{B}, starting loyalty 3
///   (CR 306.1 / CR 205.3j — Liliana planeswalker subtype).
/// - <b>+1: -2/-1 to a creature (CR 606 + CR 613.1f Layer 7c)</b>: when
///   <paramref name="targetCreatureResolver"/> is non-null and a non-null
///   <paramref name="effects"/> service is wired, registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> with (-2, -1) against the
///   first resolved target. The printed duration is "until your next
///   turn" — v1 uses the EOT pipeline (CR 514.2) as a near-miss; the
///   "next-turn" extension is the same deferred surface Wrenn -1 has.
///   No-resolver / no-effects path: legal no-op, loyalty change still
///   applies ("up to one" — CR 700.6).
/// - <b>-2: Return up to two creature cards from controller's graveyard
///   to controller's hand (CR 606 + CR 701.20)</b>: scans controller's
///   graveyard for cards with <see cref="CardType.Creature"/>, picks the
///   first two deterministically (agent-picked target choice is the same
///   gap Wrenn +1 has), and moves them via raw-zone manipulation. Empty
///   graveyard or fewer than two creature cards = legal no-op tail ("up
///   to two").
/// - <b>-7 emblem (CR 114 + CR 603.1)</b>: creates a structural
///   <see cref="Emblem"/> in controller's command zone with a marker
///   ability. When <paramref name="endStepTrigger"/> is wired, the
///   emblem also registers a "beginning of your end step" trigger that
///   creates two 2/2 black Zombie creature tokens under controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Without the trigger
///   service the emblem is structural-only (same posture as Wrenn -7).
///
/// ## Deferred (v1 gaps)
/// - <b>"Until your next turn" duration</b>: modelled as EOT (CR 514.2).
///   The extra step until the controller's next turn-end is the same
///   missing primitive Liliana of the Veil's continuous effects flag.
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> doesn't declare
///   <see cref="TargetRequest"/>s. +1 / -2 pick from supplied resolvers
///   deterministically; agent-driven choice is the same gap Karn /
///   Wrenn / Ugin have.
/// - <b>ZoneService routing</b>: -2 graveyard→hand uses raw zone
///   manipulation, so <see cref="Majik.Core.Events.CardMovedEvent"/>
///   doesn't publish on this path. Same posture as Karn's -3 / Ugin's
///   -X / -10.
/// </summary>
[CardName("Liliana, the Last Hope")]
public static class LilianaTheLastHopeFactory
{
    public const string CardName = "Liliana, the Last Hope";
    public const string Cost = "{1}{B}{B}";
    public const int StartingLoyalty = 3;
    public const int Plus1PowerDelta = -2;
    public const int Plus1ToughnessDelta = -1;
    public const int Minus2ReturnLimit = 2;
    public const int EmblemTokenCount = 2;
    public const int ZombieTokenPower = 2;
    public const int ZombieTokenToughness = 2;

    /// <summary>
    /// Construct Liliana, the Last Hope with no resolvers wired — +1 and
    /// -7 emblem-end-step clauses no-op; -2 still runs (graveyard / hand
    /// are owner-scoped). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetCreatureResolver: null, effects: null,
            endStepTrigger: null);

    /// <summary>
    /// Construct Liliana, the Last Hope.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetCreatureResolver">Returns +1 target candidates
    /// at activation time. v1 picks the first. May be null — +1 clause
    /// no-ops while loyalty change still applies.</param>
    /// <param name="effects">ContinuousEffectsService used to register
    /// the +1 -2/-1 pump (CR 613.1f Layer 7c, EOT expiry via
    /// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>). May be null —
    /// the pump is silently skipped.</param>
    /// <param name="endStepTrigger">TriggerManager used to register the
    /// -7 emblem's beginning-of-end-step zombie-token trigger. May be
    /// null — the emblem is structural-only (matches Wrenn -7 posture).
    /// </param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Creature>>? targetCreatureResolver,
        ContinuousEffectsService? effects,
        TriggerManager? endStepTrigger)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var liliana = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Liliana });

        liliana.SetOwner(owner);
        liliana.SetController(owner);

        // -- +1: Up to one target creature gets -2/-1 until your next
        //    turn. -----------------------------------------------------------
        // CR 606 (loyalty) + CR 613.1f Layer 7c (P/T mod). v1 EOT-only.
        // "Up to one" → empty selection / no resolver = legal no-op tail.
        liliana.AddAbility(new LoyaltyAbility(liliana, +1, () =>
        {
            if (effects == null) return;
            var candidates = targetCreatureResolver?.Invoke();
            if (candidates == null) return;
            var target = candidates.FirstOrDefault();
            if (target == null) return;
            if (target.Zone != ZoneType.Battlefield) return;
            // PumpUntilEndOfTurnEffect requires Target.ActiveEffects to be
            // bound to the same effects service for the modifier to take
            // hold (the service iterates over registered effects, but
            // Compute(this) reads through ActiveEffects). Wire it if the
            // target hasn't been wired yet — matches FoundryStreetDenizen.
            if (target.ActiveEffects == null)
            {
                target.ActiveEffects = effects;
            }
            effects.Register(new PumpUntilEndOfTurnEffect(
                target, Plus1PowerDelta, Plus1ToughnessDelta));
        }));

        // -- -2: Return up to two target creature cards from your
        //    graveyard to your hand. ---------------------------------------
        // CR 606 (loyalty) + CR 701.20 (graveyard → hand). v1 deterministic
        // first-N-creatures-in-graveyard pick. "Up to two" auto-accepted.
        liliana.AddAbility(new LoyaltyAbility(liliana, -2, () =>
        {
            var controller = liliana.Controller ?? owner;
            var picks = controller.Zones.Graveyard.GetCards()
                .Where(c => c.HasType(CardType.Creature))
                .Take(Minus2ReturnLimit)
                .ToList();
            foreach (var p in picks)
            {
                controller.Zones.Graveyard.RemoveCard(p);
                controller.Zones.Hand.AddCard(p);
                p.SetZone(ZoneType.Hand);
            }
        }));

        // -- -7 ultimate: emblem with "At the beginning of your end step,
        //    create two 2/2 black Zombie creature tokens." ------------------
        // CR 114 (emblem) + CR 603.1 (beginning-of-step trigger). When the
        // triggers service is wired the emblem registers an event-trigger
        // on StepStartedEvent (CR 514 — end step) gated on the
        // controller's own end step. Structural-only on the no-triggers
        // path (matches Wrenn -7).
        liliana.AddAbility(new LoyaltyAbility(liliana, -7, () =>
        {
            var controller = liliana.Controller ?? owner;

            // Mint the emblem first so we can use it as the trigger's
            // source — emblems live in the command zone (CR 114) and
            // <see cref="TriggeredAbility"/>'s activeZones gate only
            // applies when Source is an <see cref="ICard"/>. Passing the
            // emblem (a non-card command-zone object) sidesteps the
            // zone-gate entirely, so the trigger fires as long as the
            // emblem exists.
            var emblemAbilities = new List<IAbility>();
            var emblem = new Emblem(
                controller: controller,
                sourceName: $"{CardName} — zombie-tokens emblem",
                abilities: emblemAbilities);
            controller.AddEmblem(emblem);

            if (endStepTrigger != null)
            {
                // CR 603.1 — "At the beginning of your end step" fires on
                // StepStartedEvent where Step is the End step and the
                // controller of the emblem is the active player.
                var condition = new EventTriggerCondition<Majik.Core.Events.StepStartedEvent>(
                    (e, _) => e.StepType == Majik.Core.StateMachine.PhaseStateType.End
                        && ReferenceEquals(e.Player, controller));

                var spawnEffect = new Effect(
                    $"{CardName} emblem: create two 2/2 black Zombie creature tokens",
                    () =>
                    {
                        for (var i = 0; i < EmblemTokenCount; i++)
                        {
                            var spec = new TokenFactory.TokenSpec(
                                Name: "Zombie",
                                Power: ZombieTokenPower,
                                Toughness: ZombieTokenToughness,
                                Subtypes: new[] { CardSubtype.Zombie },
                                // CR 105 / CR 111.4 — black token.
                                Colors: new[] { ManaColor.Black });
                            TokenFactory.CreateOnBattlefield(spec, controller, zones: null);
                        }
                    });

                var endStepAbility = new TriggeredAbility(
                    source: emblem,
                    controller: controller,
                    condition: condition,
                    effects: new IEffect[] { spawnEffect });

                emblemAbilities.Add(endStepAbility);
                endStepTrigger.RegisterTriggeredAbility(endStepAbility);
            }
        }));

        return liliana;
    }
}
