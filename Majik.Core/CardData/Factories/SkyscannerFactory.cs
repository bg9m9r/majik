using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skyscanner (Fifth Dawn / many reprints, {3}).
/// Artifact Creature — Thopter 1/1. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, draw a card."
///
/// The card's base shape (name, Creature + Artifact card types, Thopter
/// subtype, {3}, 1/1) is materialised from the embedded JSON definition
/// (<c>skyscanner.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="PilgrimsEyeFactory"/> (the structurally identical Artifact
/// Creature — Thopter whose JSON carries both card types). Flying + the ETB
/// draw are layered on here because the JSON schema doesn't express keywords
/// or triggered abilities.
///
/// ## Implemented
/// - 1/1 Creature — Thopter at {3} with both Artifact AND Creature card types
///   (CR 205.2a), via the JSON <c>["Creature", "Artifact"]</c> type list so
///   artifact-matters effects (Affinity, metalcraft) see it.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker — the combat-block validator reads the keyword off the card's
///   abilities (same shape as <see cref="PilgrimsEyeFactory"/>).
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>:
///   "When this creature enters, draw a card." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — no intervening-if
///   (CR 603.4 does not apply). Resolution routes through
///   <see cref="Fx.DrawCards"/>(controller, 1) so the replacement bus
///   (Dredge / draw-replacement effects) fires per-draw (CR 614) and an
///   empty library stamps the SBA loss flag (CR 704.5b) without crashing.
///   The controller closure re-resolves at execute time (<c>card.Controller
///   ?? owner</c>) so blink / control-change scenarios draw for the correct
///   player. Active only from the battlefield (CR 603.6a).
/// </summary>
[CardName("Skyscanner")]
public static class SkyscannerFactory
{
    public const string CardName = "Skyscanner";
    public const string Slug = "skyscanner";
    public const int DrawAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Skyscanner with no live wiring. The ETB trigger is attached
    /// for shape observability (not registered with any
    /// <see cref="TriggerManager"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Skyscanner with an optional <see cref="TriggerManager"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding enter-the-battlefield event lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Thopter subtype, {3}, 1/1). Flying + the ETB draw are
        // layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker only; the combat-block validator
        // reads the keyword off the card's abilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1 / CR 603.6a.
        //   "When this creature enters, draw a card."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply). Routed through Fx.DrawCards so the replacement bus +
        // empty-library SBA flag fire correctly (CR 614 / CR 704.5b). The
        // controller closure re-resolves at execute time so blink /
        // control-change scenarios draw for the correct player.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: draw {DrawAmount} card (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, DrawAmount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
