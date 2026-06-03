using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kemba, Kha Regent (Scars of Mirrodin, {1}{W}{W}).
///
/// Legendary Creature — Cat Cleric 2/4. Oracle text (Scryfall, verified
/// 2026-06-02):
///   "At the beginning of your upkeep, create a 2/2 white Cat creature
///    token for each Equipment attached to Kemba."
///
/// ## Implemented (v1)
///
/// - <b>2/4 Legendary Creature — Cat Cleric, {1}{W}{W}</b>. Base shape
///   loaded from the embedded JSON definition
///   (<see cref="CardDefinitionLoader.FromEmbeddedResource"/>) and built
///   through <see cref="CardDefinitionFactory"/>, same posture as
///   <see cref="AdelineResplendentCatharFactory"/>. No abilities in the
///   JSON — the upkeep trigger is layered on below.
/// - <b>Your-upkeep trigger (CR 603.1 / CR 500.4)</b>: "At the beginning of
///   your upkeep, …". Modelled as a <see cref="TriggeredAbility"/> over
///   <see cref="Majik.Core.Events.StepStartedEvent"/> filtered to the
///   controller's own Upkeep step via
///   <see cref="Triggers.OnStepBegin"/> — same shape as
///   <see cref="SheoldredWhisperingOneFactory"/>'s your-upkeep return.
/// - <b>"create a 2/2 white Cat creature token for each Equipment attached
///   to Kemba" (CR 111.4 / CR 301.5)</b>: on resolution the trigger counts
///   the Equipment currently attached to Kemba
///   (<see cref="CountEquipmentAttachedTo"/> — reads
///   <see cref="Permanent.Attachments"/> filtered to
///   <see cref="CardSubtype.Equipment"/>, the same "what is an Equipment
///   attached to me" relationship Cranial Plating's
///   <see cref="Permanent.AttachedTo"/> walks from the other side) and mints
///   that many 2/2 white Cat tokens via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, int, ZoneService?, ReplacementBus?)"/>.
///   The bus-aware overload routes the count through a
///   <see cref="ReplacementBus"/> when supplied so token doublers (Doubling
///   Season, Parallel Lives, Anointed Procession — CR 616.1c) rewrite the
///   count before any token is minted.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The upkeep trigger is
///   attached for shape inspection (not registered with a
///   <see cref="TriggerManager"/>); token creation uses the raw-zone
///   fallback (no <see cref="ZoneService"/>) and no doubler bus. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?, ReplacementBus?)"/>
///   — fully wired. When <paramref name="triggers"/> is supplied the upkeep
///   trigger is registered so <see cref="Majik.Core.Events.StepStartedEvent"/>
///   auto-queues it; <paramref name="zones"/> routes token creation through
///   <see cref="ZoneService"/> so ETB triggers fire (CR 603.6a);
///   <paramref name="replacements"/> threads token doublers.
///
/// CR rule references: 205.3m (Cat / Cleric subtypes), 301.5 (Equipment
/// attachment), 603.1 / 500.4 (upkeep trigger), 111.4 (token characteristics),
/// 616.1c (token-doubling replacements).
/// </summary>
[CardName(CardName)]
public static class KembaKhaRegentFactory
{
    public const string CardName = "Kemba, Kha Regent";
    public const string Slug = "kemba-kha-regent";

    /// <summary>Token characteristics — 2/2 white Cat (CR 111.4).</summary>
    public const int TokenPower = 2;
    public const int TokenToughness = 2;
    private const string TokenName = "Cat";

    /// <summary>
    /// Construct Kemba with no live runtime wiring (the dispatcher / shape
    /// path). The upkeep trigger is attached for shape observability but is
    /// not registered with a <see cref="TriggerManager"/>; token creation
    /// uses the raw-zone fallback (no <see cref="ZoneService"/>) and no
    /// doubler bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null, replacements: null);

    /// <summary>
    /// Construct Kemba, Kha Regent. When <paramref name="triggers"/> is
    /// supplied the your-upkeep trigger is registered so
    /// <see cref="Majik.Core.Events.StepStartedEvent"/> auto-queues it. When
    /// <paramref name="zones"/> is supplied token creation routes through
    /// <see cref="ZoneService"/> so ETB triggers fire (CR 603.6a). When
    /// <paramref name="replacements"/> is supplied token doublers rewrite the
    /// count before minting (CR 616.1c).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Cat + Cleric, {1}{W}{W}, 2/4). No abilities in the JSON —
        // the upkeep trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------------
        // Your-upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of your upkeep, create a 2/2 white Cat creature
        //    token for each Equipment attached to Kemba."
        // Triggers.OnStepBegin filters StepStartedEvent to the controller's
        // own Upkeep step. On resolution we count Equipment attached to Kemba
        // (CR 301.5) and mint that many 2/2 white Cat tokens (CR 111.4).
        // --------------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: create a 2/2 white Cat token for each Equipment attached to Kemba",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 301.5 — count Equipment currently attached to Kemba.
                var count = CountEquipmentAttachedTo(card);
                if (count <= 0) return; // no Equipment → no tokens (CR 111.4)

                // CR 111.4 — 2/2 white Cat creature token.
                var spec = new TokenFactory.TokenSpec(
                    Name: TokenName,
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Cat },
                    Keywords: null,
                    Colors: new[] { ManaColor.White });

                // Bus-aware overload — token doublers (Doubling Season,
                // Parallel Lives, Anointed Procession; CR 616.1c) rewrite the
                // count before minting when a ReplacementBus is supplied.
                TokenFactory.CreateOnBattlefield(spec, controller, count, zones, replacements);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.StepStateType.Upkeep),
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return card;
    }

    /// <summary>
    /// CR 301.5 — count the Equipment permanents currently attached to
    /// <paramref name="kemba"/>. Walks <see cref="Permanent.Attachments"/>
    /// (the permanents attached TO this one) and filters to
    /// <see cref="CardSubtype.Equipment"/>, so Auras and other non-Equipment
    /// attachments are excluded. Pure helper exposed for tests; mirrors the
    /// closure baked into the live upkeep trigger.
    /// </summary>
    public static int CountEquipmentAttachedTo(Permanent kemba)
    {
        ArgumentNullException.ThrowIfNull(kemba);
        return kemba.Attachments.Count(a => a.HasSubtype(CardSubtype.Equipment));
    }
}
