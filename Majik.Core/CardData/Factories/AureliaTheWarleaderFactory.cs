using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aurelia, the Warleader (Gatecrash, {2}{R}{R}{W}{W}).
/// Legendary Creature — Angel 3/4. Oracle text (verified against Scryfall):
///   "Flying, vigilance, haste
///    Whenever Aurelia attacks for the first time each turn, untap all
///    creatures you control. After this phase, there is an additional combat
///    phase."
///
/// ## Implementation
///
/// The base shape (name, Legendary supertype, Creature, Angel subtype,
/// {2}{R}{R}{W}{W}, 3/4) is materialised from the embedded JSON definition
/// (<c>aurelia-the-warleader.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three keyword markers and the
/// first-attack-each-turn trigger are layered on here (the JSON
/// <c>AbilityDefinition</c> schema doesn't express attack triggers — same
/// posture as <see cref="LyraDawnbringerFactory"/> /
/// <see cref="AdelineResplendentCatharFactory"/>).
///
/// - <b>Flying (CR 702.9)</b>, <b>Vigilance (CR 702.21)</b>,
///   <b>Haste (CR 702.10)</b> — <see cref="KeywordAbility"/> markers so
///   <c>ICard.Abilities</c> reflects the printed line and the combat / evasion
///   / summoning-sickness pipelines read them off the keyword set.
///
/// - <b>"Whenever Aurelia attacks for the FIRST TIME each turn" (CR 603.2 /
///   508.1f)</b> — an <see cref="AttackersDeclaredEvent"/> trigger gated to
///   "this card's controller is the attacking player AND this card is among the
///   declared attackers", further gated to the first matching attack each turn
///   via a boxed once-per-turn cell reset on each <see cref="TurnStartedEvent"/>
///   (CR 603.2 — "for the first time each turn"). The cell is set the first time
///   the trigger resolves, so the additional combat Aurelia grants (where she
///   attacks a second time the same turn) does NOT re-trigger — exactly the
///   intended single extra combat.
///
/// On resolution:
///   1. <b>"untap ALL creatures you control" (CR 701.20a)</b> — every
///      <see cref="Creature"/> the controller controls, INCLUDING Aurelia
///      herself (the printed text says "all", not "all other" — contrast
///      <see cref="CombatCelebrantFactory"/> which untaps only OTHER creatures).
///      Aurelia has vigilance so she is untapped already, but untapping her is a
///      no-op and the loop is uniform.
///   2. <b>"After this phase, there is an additional combat phase" (CR 506.4)</b>
///      — enqueue a combat-ONLY grant (<c>followedByMainPhase: false</c>) on the
///      per-game <see cref="AdditionalCombatRegistryProvider"/> queue
///      <see cref="TurnDriver"/> drains after the current combat. No additional
///      main phase follows.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + keywords only (the
///   <see cref="NamedCardFactory"/> dispatch target). The attack trigger is
///   attached for observability; the first-attack gate is in-memory so it works
///   without an event bus (it just never resets to a new turn).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully wired:
///   the trigger is registered with the <see cref="TriggerManager"/> and the
///   once-per-turn gate resets on each <see cref="TurnStartedEvent"/>.
/// </summary>
[CardName("Aurelia, the Warleader")]
public static class AureliaTheWarleaderFactory
{
    public const string CardName = "Aurelia, the Warleader";
    public const string Slug = "aurelia-the-warleader";

    /// <summary>Granted keywords — CR 702.9 / 702.21 / 702.10.</summary>
    public const string Flying = "Flying";
    public const string Vigilance = "Vigilance";
    public const string Haste = "Haste";

    /// <summary>Construct Aurelia with no live runtime wiring (the dispatch
    /// target). Keywords are attached; the attack trigger is attached but the
    /// first-attack gate never resets (no event bus).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>Construct Aurelia with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the first-attack trigger is
    /// registered so an <see cref="AttackersDeclaredEvent"/> by the controller
    /// lands it on the stack.</param>
    /// <param name="eventBus">When supplied, the once-per-turn "first time each
    /// turn" gate resets on each <see cref="TurnStartedEvent"/> (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Angel, {2}{R}{R}{W}{W}, 3/4). No abilities in the JSON —
        // keywords + the attack trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 / 702.21 / 702.10 — Flying, vigilance, haste keyword markers.
        card.AddAbility(new KeywordAbility(Flying, card, owner));
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));
        card.AddAbility(new KeywordAbility(Haste, card, owner));

        // CR 603.2 — "for the first time each turn." Boxed once-per-turn cell
        // shared by the resolve body (sets it the first time the trigger fires)
        // and the TurnStartedEvent reset below.
        var attackedThisTurn = new bool[] { false };

        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "Whenever Aurelia attacks" (CR 508.1f) — only when this card's
            // controller is the attacking player AND this card is among the
            // declared attackers. "For the first time each turn" (CR 603.2) is
            // re-checked at resolution so a re-declare can't slip past the gate.
            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
            if (!e.Combat.Attackers.Any(a => ReferenceEquals(a?.Creature, card))) return false;
            capturedCombat = e.Combat;
            return true;
        });

        var effect = new Effect(
            $"{CardName}: on first attack each turn, untap all creatures you control + an additional combat phase",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                if (combat == null) return;

                // CR 603.2 — only the FIRST attack each turn. The additional
                // combat Aurelia grants (a second attack this turn) is gated out
                // here, so it yields exactly one extra combat.
                if (attackedThisTurn[0]) return;
                attackedThisTurn[0] = true;

                var controller = card.Controller ?? owner;

                // "untap ALL creatures you control" (CR 701.20a) — including
                // Aurelia herself (printed "all", not "all other"). Untapping an
                // already-untapped creature is a no-op.
                foreach (var c in controller.Zones.Battlefield.GetCards()
                             .OfType<Creature>().ToList())
                {
                    if (c.IsTapped) c.Untap();
                }

                // "After this phase, there is an additional combat phase."
                // CR 506.4 — enqueue a combat-ONLY grant (no following main
                // phase) on the per-game queue TurnDriver drains.
                AdditionalCombatRegistryProvider.Current.EnqueueAdditional(
                    followedByMainPhase: false);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — the trigger functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 603.2 — reset the "first time each turn" gate at the start of each
        // turn.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => attackedThisTurn[0] = false);
        }

        return card;
    }
}
