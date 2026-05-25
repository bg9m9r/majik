using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sai, Master Thopterist (Aether Revolt, {1}{U}).
///
/// Legendary Creature — Human Artificer 1/4. Oracle text:
///   "Whenever you cast an artifact spell, create a 1/1 colorless Thopter
///    artifact creature token with flying.
///    {2}, Sacrifice two artifacts: Draw a card."
///
/// Sai is the Modern artifact-deck's value engine — Affinity / Hardened
/// Scales / Modular shells churn cheap artifact spells into flyers, and
/// the {2} + sac-two-artifacts draw activation converts surplus token
/// bodies (its own Thopters, Ravager-stripped Worker shells, Cranial
/// Plating'd Memnites) into raw card advantage. The activation cost
/// reads its own Thopters as fuel — a feedback loop with Modular
/// proliferators (Arcbound Worker / Arcbound Stinger) and self-creating
/// artifact engines (Sword of the Meek, Thopter Foundry).
///
/// ## Implementation
///
/// - 1/4 <see cref="Creature"/> — Human Artificer, Legendary, mana cost
///   {1}{U}. Owner / controller wired. Legendary supertype enforced via
///   the existing legend-rule SBA path (CR 704.5j) — same shape as
///   <see cref="SramSeniorEdificerFactory"/>.
///
/// - <b>Artifact-cast trigger (CR 603.1)</b>: fires off
///   <see cref="SpellCastEvent"/> with predicate
///   <c>spell.Controller == this card's controller</c> AND
///   <c>spell.Card.HasType(CardType.Artifact)</c> (CR 205.3 — Artifact
///   spells are spells whose card has the Artifact card type, includes
///   Artifact Creatures per CR 301.1). Effect: mint a 1/1 colourless
///   <see cref="CardSubtype.Thopter"/> creature token with Flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>, then additively
///   stamp <see cref="CardType.Artifact"/> so the resulting token reports
///   Artifact + Creature — Thopter (CR 111.1; same multi-type pattern as
///   <see cref="WhirlerVirtuosoFactory"/>'s Thopter token /
///   <see cref="AnimationModuleFactory"/>'s Servo). The trigger is
///   registered with the supplied <see cref="TriggerManager"/> when
///   present; structurally attached either way for shape inspection.
///
/// - <b>{2}, Sacrifice two artifacts: Draw a card (CR 602.1)</b>: wired as
///   an <see cref="ActivatedAbility"/> with two costs:
///   <see cref="ManaCostCost"/>("{2}") + <see cref="SacrificeTwoArtifactsCost"/>
///   (excludes Sai herself — Sai is not an artifact, so the exclusion is
///   a posture-clean no-op; the printed wording is "two artifacts" with
///   no self-reference). Resolution draws one card via
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> — routes through
///   the controller's <see cref="DrawCardIntent"/> bus when one is
///   attached (CR 614, Dredge / Sylvan Library / etc.).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both abilities are
///   attached to the card; the cast trigger is NOT registered (no
///   trigger manager supplied). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/>
///   — fully wired. Cast trigger registered when
///   <paramref name="triggers"/> is supplied; <paramref name="zoneService"/>
///   threaded into <see cref="TokenFactory.CreateOnBattlefield"/> so token
///   ETB publishes a <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Draw-replacement target</b>: the printed text says "draw a card"
///   unconditionally. Replacement effects attached to the controller
///   already intercept this via the
///   <see cref="Majik.Core.Effects.DrawCardIntent"/> bus inside
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>.
/// - <b>Sacrifice target prompting</b>: <see cref="SacrificeTwoArtifactsCost"/>
///   falls back to the first two eligible artifacts on the controller's
///   battlefield when no targets are supplied (deterministic v1) — same
///   gap as <see cref="SacrificeAnArtifactCost"/>. Agent-driven target
///   prompting is the next step.
/// </summary>
[CardName("Sai, Master Thopterist")]
public static class SaiMasterThopteristFactory
{
    public const string CardName = "Sai, Master Thopterist";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 4;
    public const string ActivationCost = "{2}";
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const string ThopterTokenName = "Thopter";

    /// <summary>
    /// Construct Sai, Master Thopterist with no live wiring. The
    /// cast-trigger and the {2}-sac-two activated ability are both
    /// attached for shape observability; the trigger is NOT registered
    /// (no trigger manager supplied). Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Sai, Master Thopterist with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not consumed directly here; reserved for
    /// future LTB / lifecycle hooks.</param>
    /// <param name="triggers">TriggerManager for the artifact-cast
    /// trigger. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="zoneService">Zone service threaded into
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so the Thopter
    /// token's ETB publishes a CardMovedEvent. May be null — token
    /// enters via the raw zone branch (no event published, same posture
    /// as <see cref="WhirlerVirtuosoFactory"/>'s single-arg path).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Artificer });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Artifact-cast trigger — CR 603.1.
        //   "Whenever you cast an artifact spell, create a 1/1 colorless
        //    Thopter artifact creature token with flying."
        //
        // Predicate:
        //   - Spell controller == this card's controller ("you cast",
        //     CR 109.5).
        //   - Spell card has CardType.Artifact (CR 205.3 — covers both
        //     non-creature Artifacts and Artifact Creatures, per CR 301.1).
        //
        // Effect:
        //   - Mint a 1/1 colourless Thopter creature token with Flying
        //     via TokenFactory; additively stamp CardType.Artifact (CR
        //     111.1 — Thopter tokens are Artifact Creatures). Same
        //     posture as Whirler Virtuoso's Thopter mint.
        //
        // Active only while Sai is on the battlefield (activeZones gate).
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            return e.Spell.Card.HasType(CardType.Artifact);
        });

        var tokenEffect = new Effect(
            $"{CardName}: create 1/1 colourless Thopter token (flying) on artifact cast",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateThopterToken(controller, zoneService);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "{2}, Sacrifice two artifacts: Draw a card."
        //
        // Costs:
        //   - ManaCostCost("{2}") — generic two.
        //   - SacrificeTwoArtifactsCost(excludeSource: card) — Sai is
        //     not an Artifact, so the exclusion is a posture-clean no-op
        //     (defends against future printings / theoretical type-bend
        //     effects that briefly stamp Artifact onto Sai).
        //
        // Effect:
        //   - Fx.DrawCards(controller, 1). Routes through the
        //     DrawCardIntent replacement bus.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                new SacrificeTwoArtifactsCost(excludeSource: card),
            },
            effects: new IEffect[] { drawEffect });

        card.AddAbility(drawAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.1 / 111.6 — mint one 1/1 colourless Thopter Artifact
    /// Creature token with Flying under <paramref name="controller"/>'s
    /// control. Mirrors <see cref="WhirlerVirtuosoFactory"/>'s mint
    /// posture: TokenFactory shell stamps Creature; Flying via Keywords;
    /// Artifact additively stamped post-build.
    /// </summary>
    public static Creature CreateThopterToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: ThopterTokenName,
            Power: ThopterPower,
            Toughness: ThopterToughness,
            Subtypes: new[] { CardSubtype.Thopter },
            Keywords: new[] { "Flying" },
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 111.1 — Thopter tokens are Artifact Creatures. TokenFactory
        // shell stamps Creature only; layer Artifact on additively
        // (mirrors Whirler Virtuoso / Animation Module's Servo).
        token.AddCardType(CardType.Artifact);

        return token;
    }
}
