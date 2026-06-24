using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Costs;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Splash Lasher (Bloomburrow, {3}{U}).
///
/// Creature — Frog Wizard 3/3. Oracle text (Scryfall, verified 2026-06-24):
///   "Offspring {1}{U} (You may pay an additional {1}{U} as you cast this spell.
///    If you do, when this creature enters, create a 1/1 token copy of it.)
///    When this creature enters, tap up to one target creature and put a stun
///    counter on it. (If a permanent with a stun counter would become untapped,
///    remove one from it instead.)"
///
/// The base shape (name, Creature, Frog + Wizard subtypes, {3}{U}, 3/3) is
/// materialised from the embedded JSON definition (<c>splash-lasher.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Offspring and the ETB tap/stun
/// trigger are layered on here — the JSON <c>AbilityDefinition</c> schema
/// expresses neither the Offspring keyword/ETB nor the tap-and-stun trigger
/// (same posture as <see cref="IridescentVinelasherFactory"/> /
/// <see cref="FloodpitsDrownerFactory"/>).
///
/// ## Offspring {1}{U} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem, identical to
/// <see cref="IridescentVinelasherFactory"/> / <see cref="ManifoldMouseFactory"/>:
/// <see cref="OffspringAbility.Attach"/> registers the ETB token-copy trigger
/// (CR 702.169b — when this creature enters, if its Offspring cost was paid,
/// create a 1/1 token copy of it), and a <see cref="KeywordAbility"/> marker
/// exposes the keyword on the scan surface. The caller layers
/// <see cref="BuildOffspringCost"/> onto the cast via
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c> when the
/// caster chooses to pay the optional {1}{U} (CR 702.169a — drains the cost and
/// stamps <see cref="Card.WasOffspringPaid"/>); declining simply omits it.
///
/// ## ETB — tap up to one target creature + stun counter (CR 603.6a)
///
/// An "when this creature enters" <see cref="TriggeredAbility"/>
/// (<see cref="Triggers.OnEnterBattlefieldSelf"/>) declaring a 0..1
/// <see cref="TargetRequest"/> "up to one target creature" (CR 115.1 — "up to
/// one" is <c>MinTargets = 0</c>, the modal-zero shape Tishana's Tidebinder /
/// Frost Lynx use). Unlike <see cref="FloodpitsDrownerFactory"/>'s ETB, this one
/// is OPTIONAL and NOT opponent-scoped — any creature on the battlefield is a
/// legal target (the printed text is bare "target creature"). On resolution
/// (CR 608.2b legality re-check): if a target was chosen and it is still a
/// creature on the battlefield, it is tapped (CR 701.20) and one
/// <see cref="CounterType.Stun"/> counter is placed on it (CR 122.1c). The stun
/// counter is honoured by the untap-step replacement in
/// <c>TurnDriver.UntapStep</c> (CR 122.1g — same source of truth Floodpits
/// Drowner / Kaito's stun counters read). Choosing no target is a clean no-op
/// (CR 115.1 — "up to one").
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the ETB tap/stun trigger + Offspring ETB for inspection but
///   does not register them with a bus. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing.
/// </summary>
[CardName("Splash Lasher")]
public static class SplashLasherFactory
{
    public const string CardName = "Splash Lasher";
    public const string Slug = "splash-lasher";
    public const string OffspringCostText = "{1}{U}";

    /// <summary>Stun counters placed by the ETB trigger (CR 122.1c).</summary>
    public const int StunCountersPlaced = 1;

    /// <summary>CR 702.169 — the Offspring additional cost ({1}{U}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>
    /// Construct Splash Lasher with no live <see cref="TriggerManager"/> wiring.
    /// Offspring + the ETB tap/stun trigger are attached for shape inspection but
    /// not registered with a bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Splash Lasher. When <paramref name="triggers"/> is supplied the
    /// Offspring ETB trigger and the tap/stun ETB trigger are registered so the
    /// centralised event pump queues them automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Frog +
        // Wizard subtypes, {3}{U}, 3/3). The JSON carries no abilities —
        // Offspring and the ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Offspring {1}{U} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(card, triggers);

        // CR 702.169 — keyword marker (the "{cost}" rider rides on the
        // OffspringAdditionalCost the caller layers onto the cast).
        card.AddAbility(new KeywordAbility("Offspring", card, owner));

        // ETB — tap up to one target creature + put a stun counter on it.
        BuildEtbTrigger(card, owner, triggers);

        return card;
    }

    /// <summary>Build the Offspring {1}{U} additional cost for this spell. Layer
    /// it onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);

    // --- ETB: tap up to one target creature + one stun counter --------------

    private static void BuildEtbTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName} — tap up to one target creature and put a stun counter on it",
            () =>
            {
                if (etbTrigger == null) return;

                var chosen = etbTrigger.ChosenTargets;
                // CR 115.1 — "up to one": the controller may choose no target.
                // Clean no-op.
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal target at resolution = no effect.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.20 — tap. CR 122.1c — put one stun counter on it. The
                // stun counter is honoured by the untap-step replacement
                // (CR 122.1g).
                Fx.Tap(target);
                target.Counters.Add(CounterType.Stun, StunCountersPlaced);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Any creature on the battlefield (the printed text is bare
                    // "target creature" — not opponent-scoped).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);
    }
}
