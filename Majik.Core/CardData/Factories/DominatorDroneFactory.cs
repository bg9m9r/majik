using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dominator Drone (Battle for Zendikar, {2}{B}).
///
/// Creature — Eldrazi Drone 3/2 (colorless — Devoid). Oracle text (verified
/// against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)
///    When this creature enters, if you control another colorless creature,
///    each opponent loses 2 life."
///
/// The card's base shape (name, Creature, Eldrazi + Drone subtypes, {2}{B},
/// 3/2) is materialised from the embedded JSON definition
/// (<c>dominator-drone.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Devoid, the Ingest combat
/// trigger, and the ETB intervening-if drain are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express Devoid, combat-damage
/// triggers, or intervening-if ETB drains (same posture as
/// <see cref="RuinationGuideFactory"/> / <see cref="NettleDroneFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Devoid (CR 702.114)</b> — stamped via <see cref="Card.SetDevoid"/> so
///   <see cref="CardColors.GetColors"/> returns empty regardless of the {B}
///   pip, plus a <see cref="KeywordAbility"/> marker for ability-scan
///   discoverability. Same shape as <see cref="RuinationGuideFactory"/>.
/// - <b>Ingest (CR 701.34 / CR 510 / CR 603.1)</b> — "Whenever this creature
///   deals combat damage to a player, that player exiles the top card of
///   their library." A <see cref="TriggeredAbility"/> over
///   <see cref="CombatDamageDealtEvent"/> filtered to this card's instance AND
///   a non-null <see cref="DamageDealtEvent.TargetPlayer"/> (combat damage to
///   a player, not a creature/planeswalker). On resolution the damaged player
///   exiles the top card of their library. The damaged player is captured off
///   the event in the trigger predicate (CR 603.3) then read in the effect —
///   same shape as <see cref="RuinationGuideFactory"/>'s Ingest. Empty library
///   = no-op (CR 120.3).
/// - <b>ETB intervening-if drain (CR 603.1 / CR 603.4 / CR 119.3)</b> — "When
///   this creature enters, if you control another colorless creature, each
///   opponent loses 2 life." A <see cref="TriggeredAbility"/> keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; the
///   <c>interveningIf</c> gate (CR 603.4 — re-checked both as the ability would
///   trigger and again on resolution) asks whether the controller controls
///   ANOTHER colorless creature: any creature on the controller's battlefield,
///   other than Dominator Drone itself, whose
///   <see cref="CardColors.GetColors"/> is empty (CR 105.2c — a colorless
///   object has no color). The "each opponent loses 2 life" body (no targets —
///   global, CR 109.5) reads from the optional <c>opponentResolver</c> closure
///   (same resolver-injection pattern as
///   <see cref="MaraudingBlightPriestFactory"/> / <see cref="NettleDroneFactory"/>
///   — the Player aggregate exposes no opponents list at v1, so the caller
///   threads "each opponent" through). Each opponent loses 2 via
///   <see cref="Player.LoseLife"/>.
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches Devoid + the Ingest
/// trigger + the ETB drain trigger structurally (correct card shape for
/// factory-shape / dispatch tests). The triggers are NOT registered with a
/// <see cref="TriggerManager"/> and the drain has no opponent resolver, so it
/// silently no-ops. Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration without a resolver</b> — same gap as
///   <see cref="MaraudingBlightPriestFactory"/> / <see cref="NettleDroneFactory"/>;
///   <c>Player</c> doesn't expose an opponent list, so the factory leans on a
///   caller-supplied resolver.
/// - <b>Ingest as a first-class keyword</b> — modelled here as the plain
///   combat-damage trigger its reminder text spells out, not a reusable
///   <c>Ingest</c> primitive (the engine has no Ingest registry). The
///   <see cref="KeywordAbility"/> marker is still attached for discoverability.
///   Same posture as <see cref="RuinationGuideFactory"/>.
/// - <b>Colorless-detection via Layer-5 colour changers</b> — the
///   intervening-if gate reads <see cref="CardColors.GetColors"/> (printed /
///   Devoid-aware colour), not <c>GetEffectiveColors()</c>, so a creature
///   turned a colour (or made colorless) by another continuous effect is not
///   reflected. Same caveat as <see cref="RuinationGuideFactory"/>'s colorless
///   anthem gate.
/// </summary>
[CardName("Dominator Drone")]
public static class DominatorDroneFactory
{
    public const string CardName = "Dominator Drone";
    public const string Slug = "dominator-drone";
    public const int Power = 3;
    public const int Toughness = 2;
    public const int LifeLossPerOpponent = 2;

    /// <summary>CR 702.114 — Devoid keyword marker string.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>CR 701.34 — Ingest keyword marker string.</summary>
    public const string IngestKeyword = "Ingest";

    /// <summary>
    /// Construct Dominator Drone with no live wiring. Devoid + the Ingest
    /// combat-damage trigger + the ETB intervening-if drain are attached
    /// structurally; the triggers are NOT registered with a
    /// <see cref="TriggerManager"/> and the drain no-ops (no opponent
    /// resolver). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Dominator Drone. The ETB drain reads "each
    /// opponent" from the live resolution context at resolution
    /// (<see cref="ContextOpponents"/>), so it is correct on the production
    /// routed build (which dispatches the single-arg overload and auto-binds
    /// the triggers via <see cref="TriggerManager.BindCard"/>).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for registration. May be null —
    /// the triggers attach structurally but aren't enrolled.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {2}{B}, 3/2). The JSON carries no
        // abilities — Devoid / Ingest / the ETB drain are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty regardless of the {B} pip; attach the KeywordAbility marker
        // for ability-scan discoverability. Same shape as Ruination Guide.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // CR 701.34 — Ingest marker for ability-scan discoverability. The
        // behaviour itself is the plain combat-damage trigger wired below.
        card.AddAbility(new KeywordAbility(IngestKeyword, card, owner));

        // ----------------------------------------------------------------
        // Ingest — "Whenever this creature deals combat damage to a player,
        // that player exiles the top card of their library." CR 510 /
        // CR 603.1. The damaged player is captured off the event in the
        // predicate (CR 603.3 — the condition is evaluated as the ability
        // would trigger, before it hits the stack) so the resolved effect
        // exiles from the correct library. Empty library = no-op (CR 120.3 —
        // failing to exile from an empty library is not itself a loss).
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var ingestEffect = new Effect(
            $"{CardName}: damaged player exiles the top card of their library (Ingest)",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                var top = victim.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;

                victim.Zones.Library.RemoveCard(top);
                victim.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);
            });

        var ingestTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false; // "to a player" only
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { ingestEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ingestTrigger);
        triggers?.RegisterTriggeredAbility(ingestTrigger);

        // ----------------------------------------------------------------
        // ETB intervening-if drain — "When this creature enters, if you
        // control another colorless creature, each opponent loses 2 life."
        // CR 603.1 (ETB trigger) / CR 603.4 (intervening-if, re-checked as
        // the ability would trigger AND again on resolution) / CR 119.3
        // (life loss) / CR 109.5 ("each opponent" is global, no targets).
        //
        // Intervening-if gate: does the controller control ANOTHER colorless
        // creature? Scan the controller's battlefield for a creature, other
        // than Dominator Drone itself, whose CardColors.GetColors is empty
        // (CR 105.2c — a colorless object has no color). Use GetColors
        // (printed / Devoid-aware) rather than GetEffectiveColors() — see the
        // class xmldoc gap note re: Layer-5 colour changers.
        // ----------------------------------------------------------------
        bool ControlsAnotherColorlessCreature()
        {
            var controller = card.Controller ?? owner;
            return controller.Zones.Battlefield.GetCards().Any(c =>
                !ReferenceEquals(c, card)                          // "another"
                && c.HasType(CardType.Creature)                    // colorless CREATURE
                && CardColors.GetColors(c).Count == 0);            // colorless (CR 105.2c)
        }

        // "Each opponent" is read from the LIVE resolution context — NOT a
        // captured resolver, which was null on the routed prod build and made
        // the drain INERT in real games (resolver-null bug class; mirrors
        // Stormbreath #2540 / Grist #2549).
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {LifeLossPerOpponent} life",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(LifeLossPerOpponent);
                }
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { drainEffect },
            interveningIf: ControlsAnotherColorlessCreature,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
