using System.Linq;
using System.Threading.Tasks;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Iridescent Vinelasher (Bloomburrow, {B}).
///
/// Creature — Lizard Assassin 1/2. Oracle text (Scryfall, verified 2026-06-23):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Landfall — Whenever a land you control enters, this creature deals 1
///    damage to target opponent."
///
/// The base shape (name, Creature, Lizard + Assassin subtypes, {B}, 1/2) is
/// materialised from the embedded JSON definition (<c>iridescent-vinelasher.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Offspring and the landfall trigger
/// are layered on here — the JSON <c>AbilityDefinition</c> schema expresses
/// neither the Offspring keyword/ETB nor a landfall trigger (same posture as
/// <see cref="HiredClawFactory"/> / <see cref="PlatedGeopedeFactory"/>).
///
/// ## Offspring {2} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem, identical to
/// <see cref="ManifoldMouseFactory"/> / <see cref="PawpatchRecruitFactory"/>:
/// <see cref="OffspringAbility.Attach"/> registers the ETB token-copy trigger
/// (CR 702.169b — when this creature enters, if its Offspring cost was paid,
/// create a 1/1 token copy of it), and a <see cref="KeywordAbility"/> marker
/// exposes the keyword on the scan surface. The caller layers
/// <see cref="BuildOffspringCost"/> onto the cast via
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c> when the
/// caster chooses to pay the optional {2} (CR 702.169a — drains {2} and stamps
/// <see cref="Card.WasOffspringPaid"/>); declining simply omits it.
///
/// ## Landfall — deal 1 damage to target opponent (CR 603.1 / 603.6a / 702.142)
///
/// A landfall <see cref="TriggeredAbility"/> firing on a
/// <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to "a land entering
/// the battlefield under the controller's control" via the shared
/// <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same predicate as
/// <see cref="SteppeLynxFactory"/> / Plated Geopede / Hedron Crab). Unlike the
/// self-pump landfall creatures, this one carries a 1..1 "target opponent"
/// <see cref="TargetRequest"/> (CR 115.1) and on resolution deals 1 damage to a
/// target opponent (CR 119.3) via <see cref="Fx.DealDamage"/> — the same
/// target-opponent damage shape as <see cref="HiredClawFactory"/>'s attack
/// trigger. The target is read off the trigger's
/// <see cref="TriggeredAbility.ChosenTargets"/> (the prod async trigger-drain
/// prompts the controller's agent), falling back to the first live opponent off
/// <see cref="ContextOpponents.Of"/> at resolution (CR 102.1) — no captured
/// build-time resolver, so the damage is never inert on the prod routed build.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the landfall trigger + Offspring ETB for inspection but does
///   not register them with a bus. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing.
/// </summary>
[CardName("Iridescent Vinelasher")]
public static class IridescentVinelasherFactory
{
    public const string CardName = "Iridescent Vinelasher";
    public const string Slug = "iridescent-vinelasher";
    public const string OffspringCostText = "{2}";

    /// <summary>Landfall damage dealt to the target opponent (CR 119.3).</summary>
    public const int DamageAmount = 1;

    /// <summary>CR 702.169 — the Offspring additional cost ({2}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>
    /// Construct Iridescent Vinelasher with no live <see cref="TriggerManager"/>
    /// wiring. Offspring + the landfall trigger are attached for shape inspection
    /// but not registered with a bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Iridescent Vinelasher. When <paramref name="triggers"/> is
    /// supplied the Offspring ETB trigger and the landfall trigger are registered
    /// so the centralised event pump queues them automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Lizard +
        // Assassin subtypes, {B}, 1/2). The JSON carries no abilities — Offspring
        // and the landfall trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(card, triggers);

        // CR 702.169 — keyword marker (the "{cost}" rider rides on the
        // OffspringAdditionalCost the caller layers onto the cast).
        card.AddAbility(new KeywordAbility("Offspring", card, owner, arg: 2));

        // Landfall — deal 1 damage to target opponent.
        BuildLandfallTrigger(card, owner, triggers);

        return card;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);

    // --- Landfall — deal 1 damage to target opponent (CR 603.6a / 119.3) -----

    private static void BuildLandfallTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        // CR 603.1 / 603.6a / CR 702.142 — "Landfall — Whenever a land you
        // control enters, this creature deals 1 damage to target opponent."
        // Predicate shared with Steppe Lynx / Plated Geopede / Hedron Crab.
        // Unlike the self-pump landfall creatures, this carries a 1..1 "target
        // opponent" request and deals damage to that opponent on resolution.
        TriggeredAbility? landfallTrigger = null;
        var damageEffect = new Effect(
            $"{CardName}: landfall — deal {DamageAmount} damage to target opponent",
            rc =>
            {
                // CR 119.3 — damage to a player reduces their life total;
                // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8). The
                // target is read off the trigger's ChosenTargets (the prod async
                // trigger-drain prompts the agent), falling back to the first
                // live opponent off ContextOpponents.Of — never a captured
                // build-time resolver, so it is live on the prod routed build.
                var opponent = ResolveTargetOpponent(landfallTrigger, card, owner, rc);
                if (opponent != null) Fx.DealDamage(opponent, DamageAmount);
                return ValueTask.CompletedTask;
            });

        landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);
    }

    private static Player? ResolveTargetOpponent(
        TriggeredAbility? landfallTrigger,
        Creature card,
        Player owner,
        ResolutionContext rc)
    {
        var controller = card.Controller ?? owner;

        // CR 115 — honour an explicit target if the trigger was dispatched with
        // one (ChosenTargets[0][0] is the agent-picked opponent).
        if (landfallTrigger != null
            && landfallTrigger.ChosenTargets.Count > 0
            && landfallTrigger.ChosenTargets[0].Count > 0
            && landfallTrigger.ChosenTargets[0][0] is Player chosenPlayer
            && !ReferenceEquals(chosenPlayer, controller))
        {
            return chosenPlayer;
        }

        // CR 102.1 — fall back to the first live opponent read off the
        // resolution context (no captured resolver — live on the prod build).
        return ContextOpponents.Of(rc, controller).FirstOrDefault();
    }
}
