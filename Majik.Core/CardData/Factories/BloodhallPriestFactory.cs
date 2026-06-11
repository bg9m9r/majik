using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodhall Priest (Eldritch Moon, {2}{B}{R}).
/// Creature — Vampire Cleric 4/4. Oracle text (verified against Scryfall):
///   "Whenever this creature enters or attacks, if you have no cards in hand,
///    this creature deals 2 damage to any target.
///    Madness {1}{B}{R} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Shape source
/// Card identity (name, {2}{B}{R}, 4/4, Creature — Vampire Cleric) is loaded
/// from <c>Majik.Core/CardData/Cards/bloodhall-priest.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The enters-or-attacks burn trigger is
/// attached in code below.
///
/// ## Implemented (v1)
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack) with a
///   hellbent intervening-if (CR 603.4)</b>: two <see cref="TriggeredAbility"/>
///   instances — one gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>,
///   one on <see cref="Triggers.OnAttackSelf"/> (same dual-trigger scaffold as
///   <see cref="OverlordOfTheBoilerbilgesFactory"/>). Each carries a single
///   1..1 "any target" <see cref="TargetRequest"/> and an intervening-if that
///   re-checks "you have no cards in hand" — the controller's hand is empty —
///   both when the trigger would be put on the stack (CanBePutOnStack) and on
///   resolution (CR 603.4), cribbed from <see cref="AsylumVisitorFactory"/>'s
///   hellbent gate. On resolution each reads its chosen target and routes
///   through <see cref="Fx.DealDamageAny"/> so all three legal target classes
///   resolve correctly: Player → life loss (CR 119.3), Creature → marked damage
///   (CR 120.3), Planeswalker → loyalty removal (CR 306.7). Illegal-on-
///   resolution targets fail silently (CR 608.2b).
///
/// ## Madness {1}{B}{R} (CR 702.35) — intrinsic, no factory wiring
/// Madness is handled engine-wide via <c>MadnessCatalog</c> (name → cost) +
/// the central discard funnel <c>Fx.DiscardCard</c>: a discarded catalogued
/// card is routed to exile and offered for its madness cost automatically.
/// "Bloodhall Priest" is catalogued, so the Madness line needs no factory code.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + triggers attached for observability;
///   nothing registered on a trigger manager.
/// - <see cref="Create(Player, TriggerManager?)"/> — triggers registered so the
///   bus drives them automatically.
/// </summary>
[CardName("Bloodhall Priest")]
public static class BloodhallPriestFactory
{
    public const string CardName = "Bloodhall Priest";
    public const string Slug = "bloodhall-priest";

    /// <summary>Damage dealt by the enters-or-attacks trigger.</summary>
    public const int DamageAmount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Bloodhall Priest with no trigger-manager wiring. The
    /// enters-or-attacks triggers are attached for shape observability. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Bloodhall Priest with optional <see cref="TriggerManager"/>
    /// wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers register
    /// so the matching events land their abilities on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Vampire + Cleric subtypes, {2}{B}{R}, 4/4). No abilities in JSON —
        // the enters-or-attacks burn trigger is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ETB trigger — CR 603.1.
        var etbTrigger = BuildDamageTrigger(
            card, owner,
            Triggers.OnEnterBattlefieldSelf(card),
            $"{CardName}: enters — if hellbent, deal {DamageAmount} damage to any target");
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = BuildDamageTrigger(
            card, owner,
            Triggers.OnAttackSelf(card),
            $"{CardName}: attacks — if hellbent, deal {DamageAmount} damage to any target");
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Build one enters-or-attacks triggered ability: a single 1..1 "any
    /// target" <see cref="TargetRequest"/> whose effect deals
    /// <see cref="DamageAmount"/> damage to the chosen target via
    /// <see cref="Fx.DealDamageAny"/> (CR 115.3 / CR 608.2b — gated per target
    /// shape). The intervening-if (CR 603.4) re-checks "you have no cards in
    /// hand" — the live controller's hand is empty — both when the trigger
    /// would be put on the stack and on resolution.
    /// </summary>
    private static TriggeredAbility BuildDamageTrigger(
        Creature card, Player owner, ITriggerCondition condition, string label)
    {
        TriggeredAbility? ability = null;

        // CR 603.4 — "if you have no cards in hand". Read the live controller
        // so a control change carries the gate. Re-checked at queue time
        // (CanBePutOnStack) and again on resolution.
        bool Hellbent()
        {
            var controller = card.Controller ?? owner;
            return !controller.Zones.Hand.GetCards().Any();
        }

        var effect = new Effect(label, () =>
        {
            if (ability == null) return;
            // CR 603.4 — re-check the intervening-if on resolution.
            if (!Hellbent()) return;
            if (ability.ChosenTargets.Count == 0) return;
            if (ability.ChosenTargets[0].Count == 0) return;

            var target = ability.ChosenTargets[0][0];
            Fx.DealDamageAny(target, DamageAmount); // CR 608.2b — gated per shape
        });

        ability = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            interveningIf: Hellbent,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        return ability;
    }
}
