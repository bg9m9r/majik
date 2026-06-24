using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abuelo, Ancestral Echo (Duskmourn: House of Horror,
/// {1}{W}{U}).
///
/// Legendary Creature — Spirit 2/2. Oracle text (verified against Scryfall):
///   "Flying, ward {2}
///    {1}{W}{U}: Exile another target creature or artifact you control. Return
///    it to the battlefield under its owner's control at the beginning of the
///    next end step."
///
/// The base shape (name, Legendary Creature — Spirit, {1}{W}{U}, 2/2) is
/// materialised from the embedded JSON definition (<c>abuelo-ancestral-echo.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying + ward {2} keyword markers
/// and the activated blink ability are layered on here (the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or this
/// activated-ability + delayed-trigger shape).
///
/// ## Implemented (v1)
///
/// - 2/2 Legendary <see cref="Creature"/> — Spirit, {1}{W}{U}. Color identity
///   white/blue (derived from the {W}{U} pips per CR 202.2c). Mana value 3
///   (CR 202.3).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> for evasion in
///   the combat validator. Same wiring shape as
///   <see cref="MischievousMysticFactory"/>.
/// - <b>Ward {2} (CR 702.21)</b>: <see cref="KeywordAbility"/> marker — same
///   posture as <see cref="AbolethSpawnFactory"/> (Ward {1}). The
///   spell/ability-cost-counter Ward consultation surface is the engine-wide
///   keyword-marker posture; the marker is what existing Ward wiring keys off.
/// - <b>Activated blink ability (CR 602.1 / CR 603.7 / CR 701.21)</b>:
///   "{1}{W}{U}: Exile another target creature or artifact you control. Return
///   it to the battlefield under its owner's control at the beginning of the
///   next end step."
///
///   Wired as an <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   of {1}{W}{U} and a single 1-of target request. The target gatherer offers
///   creatures and artifacts the controller controls, excluding Abuelo itself
///   ("another" — CR 115.5b). On resolution the chosen permanent is exiled
///   (owner-routed zone moves so LTB events fire), and — when a
///   <see cref="TriggerManager"/> is wired — a
///   <see cref="DelayedTriggeredAbility"/> is registered that returns it to its
///   owner's battlefield on the first <see cref="StepStartedEvent"/> with
///   <c>StepType == End</c> after the activation resolved (CR 603.7). Same
///   delayed-end-step blink pattern as <see cref="FlickerwispFactory"/> and
///   <see cref="CharmingPrinceFactory"/>'s mode 2; the only structural twist vs.
///   those ETB triggers is that this is an <em>activated</em> ability rather
///   than a triggered one.
///
///   Key rules distinctions:
///   - "creature or artifact you control" — controller-scoped (CR 109.5), and
///     the type filter accepts <see cref="Creature"/> OR <see cref="Artifact"/>
///     (an Artifact Creature qualifies under either clause). Re-checked at
///     resolution (CR 608.2b).
///   - "another" — distinct object from Abuelo itself (CR 115.5b).
///   - "under its owner's control" (CR 108.3 / CR 614) — the return routes
///     through the <em>owner's</em> zones, not necessarily the controller's, so
///     a control-swapped permanent goes back to its true owner.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The activated ability is
///   attached for shape inspection; with no <see cref="TriggerManager"/> the
///   exile still happens on resolution but no delayed return is registered.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired;
///   the delayed end-step return is registered on <paramref name="triggers"/>.
/// </summary>
[CardName("Abuelo, Ancestral Echo")]
public static class AbueloAncestralEchoFactory
{
    public const string CardName = "Abuelo, Ancestral Echo";
    public const string Slug = "abuelo-ancestral-echo";

    /// <summary>CR 602.1 — the blink activation mana cost.</summary>
    public const string ActivationManaCost = "{1}{W}{U}";

    /// <summary>CR 702.21 — printed Ward cost: {2}.</summary>
    public const string WardCost = "{2}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Abuelo with no live wiring. The activated ability is attached
    /// for shape inspection; with no <see cref="TriggerManager"/> the exile
    /// resolves but no delayed end-step return is registered. Suitable for
    /// dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Abuelo with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the activated ability's
    /// delayed end-step return is registered so the exiled permanent comes back
    /// (CR 603.7).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus. Currently unused by Abuelo's body
    /// (the delayed return keys off the TriggerManager); accepted for wiring
    /// symmetry with sibling blink factories. May be null.</param>
    /// <param name="triggers">TriggerManager the delayed end-step return
    /// registers with. Without one the blink exiles but never returns. May be
    /// null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature — Spirit, {1}{W}{U}, 2/2). The JSON carries no abilities —
        // Flying + Ward {2} + the activated blink are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Block restrictions enforced by CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.21 — Ward {2} keyword marker. Same posture as Aboleth Spawn
        // (Ward {1}) — the marker is what existing Ward wiring keys off.
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        AddBlinkActivatedAbility(card, owner, triggers);

        return card;
    }

    // ------------------------------------------------------------------
    // Activated blink ability — CR 602.1 / CR 603.7 / CR 701.21.
    //   "{1}{W}{U}: Exile another target creature or artifact you control.
    //    Return it to the battlefield under its owner's control at the
    //    beginning of the next end step."
    // ------------------------------------------------------------------
    private static void AddBlinkActivatedAbility(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        ActivatedAbility? ability = null;

        var blinkEffect = new Effect(
            $"{CardName}: exile another target creature/artifact you control; return at next end step (CR 603.7)",
            () =>
            {
                if (ability == null) return;
                ExecuteExileAndDelayedReturn(ability, card, owner, triggers);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationManaCost) },
            effects: new IEffect[] { blinkEffect },
            targetRequests: new[]
            {
                // "another target creature or artifact you control" —
                // controller-scoped (CR 109.5); a permanent that is a Creature
                // OR an Artifact qualifies; Abuelo itself is excluded
                // ("another" — CR 115.5b).
                new TargetRequest(
                    Description: "another target creature or artifact you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(p => p is Creature || p.HasType(CardType.Artifact))
                        .Where(p => ReferenceEquals(p.Controller, card.Controller ?? owner))
                        .Where(p => !ReferenceEquals(p, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Execute the blink: exile the chosen creature/artifact, then register a
    /// delayed end-step triggered ability that returns it to its owner's
    /// battlefield (CR 603.7).
    ///
    /// "under its owner's control" — the return routes through
    /// <c>target.Owner</c>'s zones (CR 108.3 / CR 614), which may differ from
    /// the current controller if the permanent was control-swapped.
    /// </summary>
    private static void ExecuteExileAndDelayedReturn(
        ActivatedAbility ability,
        Creature source,
        Player owner,
        TriggerManager? triggers)
    {
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // CR 608.2b — resolution-time legality re-checks.
        if (target.Zone != ZoneType.Battlefield) return;
        if (ReferenceEquals(target, source)) return;           // "another"

        // "creature or artifact you control" — re-check the type + control
        // predicate at resolution (CR 608.2b / 109.5).
        if (target is not Creature && !target.HasType(CardType.Artifact)) return;
        var controller = source.Controller ?? owner;
        if (!ReferenceEquals(target.Controller, controller)) return;

        var targetOwner = target.Owner ?? controller;

        // CR 701.21 — Exile. Owner-routed zone moves so LTB events fire.
        targetOwner.Zones.Battlefield.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);

        // CR 603.7 — register a delayed end-step return.
        // Skipped when no TriggerManager is wired (shape-only tests).
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var returnEffect = new Effect(
            $"{CardName}: return exiled permanent to owner's battlefield at next end step (CR 603.7)",
            () =>
            {
                // CR 111.8 — tokens cease to exist when they leave the
                // battlefield; guard defensively so a token blink no-ops
                // rather than crashing (same posture as Flickerwisp).
                if (target.Zone != ZoneType.Exile) return;

                // "under its owner's control" (CR 108.3) — route through the
                // owner's zones; correctly handles a control-swapped permanent.
                targetOwner.Zones.Exile.RemoveCard(target);
                targetOwner.Zones.Battlefield.AddCard(target);
                target.SetZone(ZoneType.Battlefield);
                target.SetController(targetOwner);   // owner's control
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }
}
