using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cabal Therapist (Modern Horizons, {B}).
///
/// Creature — Horror 1/1. Oracle text (verified against Scryfall 2026-06-02):
///   "Menace
///    At the beginning of your first main phase, you may sacrifice a creature.
///    When you do, choose a nonland card name, then target player reveals their
///    hand and discards all cards with that name."
///
/// ## The reflexive "you may [do X]; when you do, …" trigger (CR 603.2.2)
///
/// This is the SACRIFICE-cost sibling of the mana-rider reflexive trigger
/// ("you may pay {1}{C}. If you do, …" — Eldrazi Obligator, closed earlier).
/// The shape is: an OPTIONAL non-mana action (sacrifice a creature) on a
/// turn-based trigger whose <i>later clause</i> ("When you do, …") only fires
/// when the optional action is actually performed. We model it as a single
/// first-main-phase <see cref="TriggeredAbility"/>; at resolution the
/// controller's agent is prompted yes/no, and only on "yes" (and an actual
/// sacrifice) does the reflexive reveal-and-discard run.
///
/// ## Implemented (v1)
/// - Identity ({B} Creature — Horror 1/1) materialised from the embedded JSON
///   definition (<c>cabal-therapist.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="WeddingAnnouncementFactory"/> / <see cref="FlamebladeAdeptFactory"/>.
/// - <b>Menace</b> (CR 702.110) — a <see cref="KeywordAbility"/> marker consumed
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> at
///   block-declaration time.
/// - <b>First-main-phase trigger (CR 603.1)</b> scoped to the controller's own
///   pre-combat main step via <see cref="Triggers.OnStepBegin"/> with
///   <see cref="Majik.Core.StateMachine.StepStateType.PreCombatMain"/>.
///   Battlefield-only (CR 113.6). At resolution:
///     1. <b>"you may sacrifice a creature"</b> — prompt the controller's agent
///        yes/no. On "yes", pick a creature they control (their own creatures
///        only, including the Therapist itself — CR 701.16) via
///        <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> and sacrifice it
///        through <see cref="Fx.Sacrifice(ICard,Player,IEventBus)"/> (publishes
///        the CR 701.16 <see cref="PermanentSacrificedEvent"/>). A decline OR no
///        creatures to sacrifice skips the reflexive clause entirely.
///     2. <b>"When you do, choose a nonland card name, then target player
///        reveals their hand and discards all cards with that name."</b> Only
///        runs when a creature was actually sacrificed (CR 603.2.2). The chosen
///        target player (read off <see cref="TriggeredAbility.ChosenTargets"/>,
///        slot 0) reveals their hand (one <see cref="CardRevealedEvent"/> per
///        card via <see cref="RevealHelper.RevealHand"/>, CR 701.16), then every
///        card matching the chosen name is moved hand → graveyard (CR 701.16a).
///
/// ## Deferred (v1 gaps)
/// - <b>Reflexive trigger as a separate stack object</b>: CR 603.2.2 puts the
///   "When you do, …" clause on the stack as its OWN triggered ability the next
///   time a player would receive priority — so an opponent gets a response
///   window between the sacrifice and the reveal/discard. v1 resolves both in
///   the same trigger resolution (no intervening window). This is the same
///   posture the mana-rider sibling (Eldrazi Obligator) takes for its "If you
///   do, …" continuation, and is faithful for the common line (no opponent has
///   a relevant instant-speed response that changes the reveal/discard outcome).
/// - <b>"Choose a nonland card name" agent prompt</b>: the engine has no
///   card-name picker on <see cref="IPlayerAgent"/> (same queue as Pithing
///   Needle, Cavern of Souls, Cabal Therapy). The chosen name is supplied by a
///   caller-threaded <see cref="Func{Player, String}"/> <c>nameSelector</c>;
///   the single-arg dispatcher path leaves it empty (a null / empty name
///   matches nothing, so the discard is a defensive no-op rather than sweeping
///   nameless tokens). The "nonland" restriction is likewise the selector's
///   responsibility (matches the Cabal Therapy / Pithing Needle posture).
/// - <b>Target-player choice</b>: "target player" is chosen as the trigger goes
///   on the stack (CR 603.3d). The fully-wired overload threads a
///   <c>targetResolver</c> + the controller picks via the normal target system;
///   in tests the chosen player is set directly on
///   <see cref="TriggeredAbility.ChosenTargets"/>. The single-arg shape path
///   wires no resolver (pure-shape), like <see cref="CabalTherapyFactory"/>.
/// </summary>
[CardName("Cabal Therapist")]
public static class CabalTherapistFactory
{
    public const string CardName = "Cabal Therapist";
    public const string Slug = "cabal-therapist";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Cabal Therapist with no live wiring — Menace + the
    /// first-main-phase trigger are attached for shape / dispatcher tests, but
    /// the trigger is not registered with a <see cref="TriggerManager"/> and no
    /// event bus / name selector is threaded (the reflexive discard no-ops at
    /// resolution without them). Identical posture to
    /// <see cref="WeddingAnnouncementFactory.Create(Player)"/>.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, nameSelector: null, targetResolver: null);

    /// <summary>
    /// Construct a fully-wired Cabal Therapist.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Bus used to publish the
    /// <see cref="PermanentSacrificedEvent"/> on a real sacrifice and the
    /// per-card <see cref="CardRevealedEvent"/> on the reveal. May be null — the
    /// sacrifice still moves the creature to the graveyard, just without the
    /// reveal/sacrifice events.</param>
    /// <param name="triggers">When supplied, the first-main-phase trigger is
    /// registered so a PreCombatMain <see cref="Majik.Core.Events.StepStartedEvent"/>
    /// for the controller queues it on the stack.</param>
    /// <param name="nameSelector">Resolves the chosen nonland card name at
    /// resolution (CR 700.2). Returning null / empty matches nothing.</param>
    /// <param name="targetResolver">Maps a chosen target object (off
    /// <see cref="TriggeredAbility.ChosenTargets"/>) to the live game object —
    /// supplied by the caller's <see cref="Majik.Core.Game.GameContext"/>. When
    /// null the chosen slot is read as-is (tests set the live Player directly).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<Player, string?>? nameSelector,
        Func<object, object>? targetResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Creature — Horror, {B}, 1/1) from the embedded JSON
        // def. The JSON carries no abilities — Menace + the reflexive trigger
        // are layered on here (the JSON AbilityDefinition schema expresses
        // neither the Menace keyword line nor the reflexive sacrifice rider).
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.110 — Menace keyword marker.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // First-main-phase reflexive trigger — CR 603.1 / CR 603.2.2.
        //   "At the beginning of your first main phase, you may sacrifice a
        //    creature. When you do, choose a nonland card name, then target
        //    player reveals their hand and discards all cards with that name."
        // ----------------------------------------------------------------
        var triggerEffect = new Effect(
            $"{CardName}: may sacrifice a creature; when you do, name + discard",
            async ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller ?? owner;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent == null) return; // no decision-maker → "you may" defaults to declining.

                // 1. "you may sacrifice a creature" (CR 601.2b — an optional
                //    action; the rest of the ability is gated behind taking it).
                var fodder = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Cast<ICard>()
                    .ToList();
                if (fodder.Count == 0) return; // nothing to sacrifice → the "may" can't be taken.

                var wantsTo = await agent
                    .ChooseYesNoAsync(ctx.Game, "Sacrifice a creature?", CardName, ctx.Ct)
                    .ConfigureAwait(false);
                if (!wantsTo) return;

                var chosen = await agent
                    .ChooseFromBattlefieldAsync(controller, fodder, BotIntent.None, ctx.Ct)
                    .ConfigureAwait(false);
                if (chosen is not Creature sacrificed) return;

                // CR 701.16 — sacrifice the chosen creature. Use the
                // event-publishing overload when a bus is present so the
                // PermanentSacrificedEvent fires (aristocrat payoffs observe it).
                if (eventBus != null)
                {
                    Fx.Sacrifice(sacrificed, controller, eventBus);
                }
                else
                {
                    Fx.Sacrifice(sacrificed);
                }

                // 2. "When you do, …" — the reflexive clause (CR 603.2.2). Only
                //    runs because a creature was actually sacrificed above.
                //    Read the chosen target player off ChosenTargets slot 0.
                if (ctx.ChosenTargets.Count == 0 || ctx.ChosenTargets[0].Count == 0) return;
                var rawTarget = ctx.ChosenTargets[0][0];
                var resolved = targetResolver != null ? targetResolver(rawTarget) : rawTarget;
                if (resolved is not Player victim) return;

                // CR 701.16 — "target player reveals their hand."
                RevealHelper.RevealHand(eventBus, victim, CardName);

                // CR 701.16a — "discards all cards with that name." A null /
                // empty name matches nothing (defensive — no nameless sweep).
                var name = nameSelector?.Invoke(controller);
                if (string.IsNullOrEmpty(name)) return;

                var matches = victim.Zones.Hand.GetCards()
                    .Where(c => string.Equals(c.Name, name, StringComparison.Ordinal))
                    .ToList();
                foreach (var match in matches)
                {
                    victim.Zones.Hand.RemoveCard(match);
                    victim.Zones.Graveyard.AddCard(match);
                    match.SetZone(ZoneType.Graveyard);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.StepStateType.PreCombatMain),
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
