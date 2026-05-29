using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Cottage (Wilds of Eldraine "Restless"
/// creature-land cycle, black/green member — sibling of
/// <see cref="RestlessSpireFactory"/>).
/// Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {B} or {G}.
///    {2}{B}{G}: This land becomes a 4/4 black and green Horror creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, create a Food token and exile up to one
///    target card from a graveyard."
///
/// Shares the manland animate shape used by <see cref="RestlessSpireFactory"/>
/// / the rest of the cycle: unconditional ETB-tapped (CR 614.1c), two mana
/// abilities (one per colour, CR 605.1), and a {2}{B}{G} animate-until-EOT
/// <see cref="ActivatedAbility"/> whose resolution registers a
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add Creature + Horror
/// subtype; no granted keywords) and a <see cref="ManlandCycleBecomesPTEffect"/>
/// (Layer 7b — set base P/T 4/4), both flagged
/// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the two colour mana abilities) is
/// materialised from the embedded JSON definition (<c>restless-cottage.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider, the
/// animate ability, and the attack trigger are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses none of them yet (same
/// posture as <see cref="RestlessSpireFactory"/>).
///
/// ## What is unique to Restless Cottage vs the rest of the cycle
/// The land has an intrinsic triggered ability printed on the land (CR 508.1f):
/// "Whenever this land attacks, create a Food token and exile up to one target
/// card from a graveyard." It is attached to the land unconditionally and
/// registered with the supplied <see cref="TriggerManager"/> up front. A land
/// can only attack while it is the animated 4/4 creature (CR 508.1a), so the
/// trigger is unreachable until the controller activates the {2}{B}{G} ability
/// the same turn. The trigger carries a single 0..1 "target card in a
/// graveyard" <see cref="TargetRequest"/> ("up to one"); on resolution it
/// always creates a Food token (CR 701.* token creation) via
/// <see cref="TokenFactory.CreateFood"/> and — if a legal graveyard target was
/// chosen and is still in a graveyard (CR 608.2b illegal-target check) —
/// exiles it (CR 701.21) via <see cref="OracleSpellBinder.MoveToExile"/>
/// (same graveyard-exile shape as <see cref="ClingToDustFactory"/>).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Black/green colour identity of the animated form</b> — the engine has
///   no Layer-5 colour-set primitive yet (same gap as Raging Ravine / Restless
///   Spire). Recorded only in the effect-name string. The Horror subtype and
///   4/4 body DO apply via Compute.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 4/4 is recorded for inspection but doesn't surface for combat
///   resolution yet.
/// - <b>Attack-trigger reachability</b> — <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent.Attacker"/>
///   is typed <see cref="Creature"/>, but a Restless Cottage runtime instance
///   is always a <see cref="Land"/>; production never passes the land as the
///   event's Attacker, so the printed trigger is observationally equivalent to
///   one reachable only while animated. The Food + exile effect is wired and
///   correct; only the production fire-path is the shared cycle gap. It runs
///   when driven manually.
/// </summary>
[CardName("Restless Cottage")]
public static class RestlessCottageFactory
{
    public const string CardName = "Restless Cottage";
    public const string Slug = "restless-cottage";
    public const int AnimatedPower = 4;
    public const int AnimatedToughness = 4;

    /// <summary>
    /// Construct Restless Cottage with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, <see cref="ZoneService"/>, or
    /// <see cref="TriggerManager"/> wired. The two mana abilities (from JSON) +
    /// the animate ability + the attack trigger shape are attached so the card
    /// surface is complete; the layer effects are not registered, the
    /// ETB-tapped replacement is omitted, the Food token bypasses ZoneService,
    /// and the attack trigger is not auto-registered (its effect still runs
    /// when driven manually). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Restless Cottage with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the intrinsic attack trigger is
    /// registered with. May be null — the trigger is still attached to the
    /// land's ability list but won't fire from the event bus.</param>
    /// <param name="zoneService">Zone service threaded into the Food token's
    /// creation so its ETB CardMovedEvent fires. May be null.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {B} / {T}: Add {G} mana abilities). The ETB-tapped rider,
        // the animate ability, and the attack trigger are layered on below —
        // none is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {2}{B}{G}: This land becomes a 4/4 black and green Horror creature
        // until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2). See class doc for the black/green
        // colour v1 posture.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} black and green Horror creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Horror subtype. No granted
                // keywords (the body is a vanilla 4/4). Printed Land type
                // stays ("It's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Horror },
                    extraTypes: null));

                // Layer 7b — set base P/T 4/4.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{B}{G}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Intrinsic attack trigger (printed on the land, CR 508.1f):
        //   "Whenever this land attacks, create a Food token and exile up to
        //    one target card from a graveyard."
        //
        // Attached unconditionally (the trigger is printed on the land
        // itself). A land can only attack while it's the animated 4/4
        // (CR 508.1a), so the trigger is unreachable until the {2}{B}{G}
        // ability resolves the same turn.
        //
        // Targeting: a single 0..1 "target card in a graveyard" request
        // ("up to one" — optional, CR 115.1a). On resolution it always
        // creates a Food token, then exiles the chosen card if one was
        // chosen and is still in a graveyard (CR 608.2b illegal-target
        // check at resolution, mirroring Cling to Dust).
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var attackEffect = new Effect(
            $"{CardName}: create a Food token and exile up to one target card from a graveyard",
            () =>
            {
                var controller = land.Controller ?? owner;

                // Always create a Food token (CR 701 token creation). This
                // half of the trigger is not targeted, so it resolves
                // regardless of whether an exile target was chosen.
                TokenFactory.CreateFood(controller, zoneService);

                // Exile the chosen graveyard card, if any ("up to one").
                if (attackTrigger == null
                    || attackTrigger.ChosenTargets.Count == 0
                    || attackTrigger.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (attackTrigger.ChosenTargets[0][0] is not ICard card) return;

                // CR 608.2b — illegal-target check at resolution. The card
                // must still be in a graveyard.
                if (card.Zone != ZoneType.Graveyard) return;

                OracleSpellBinder.MoveToExile(card); // CR 701.21
            });

        attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in a graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
