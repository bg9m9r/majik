using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stensia Masquerade (Shadows over Innistrad,
/// {2}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Attacking creatures you control have first strike.
///    Whenever a Vampire you control deals combat damage to a player, put a
///    +1/+1 counter on it.
///    Madness {2}{R} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Shape source
///
/// Plain <see cref="Enchantment"/> built inline (no printed supertypes /
/// subtypes — like <see cref="HardenedScalesFactory"/>). Madness {2}{R} is
/// already engine-intrinsic via <see cref="Majik.Core.Keywords.MadnessCatalog"/>
/// (which lists this card's madness cost) routed through the discard
/// replacement bus — the factory does NOT re-wire madness, only the two
/// printed abilities of the enchantment body.
///
/// ## Implemented (v1)
///
/// - <b>"Attacking creatures you control have first strike." (CR 613.1f,
///   CR 702.7)</b>: a Layer-6 keyword-granting ANTHEM over a DYNAMIC creature
///   set via <see cref="GrantAbilityToGroupStaticEffect"/> (the same group-grant
///   machinery Chromatic Lantern / Enduring Vitality use), wired by a
///   <see cref="GrantAbilityToGroupLifecycle"/>. Unlike a single-target attached
///   aura boost, this is a static keyword anthem scoped by the LIVE combat
///   predicate: the membership filter is "every <see cref="Creature"/> this
///   card's controller controls THAT IS CURRENTLY ATTACKING"
///   (<see cref="CombatMembershipRegistry.IsAttacking"/> via the per-game
///   <see cref="CombatMembershipRegistryProvider"/> — the same combat-aware
///   ambient surface Eiganjo / regeneration shields read). The granted ability
///   is a <c>"First strike"</c> <see cref="KeywordAbility"/> marker, which
///   <see cref="ContinuousEffectsService.Compute"/> bakes into the bearer's
///   computed <c>Keywords</c> set, so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> returns true
///   for an attacker while it is in combat. Membership is recomputed on every
///   layer pass (CR 611.2c): a creature gains first strike the instant it is
///   declared as an attacker and loses it when it leaves combat (CR 508.4 /
///   511.3) — no zone move is required, because every <c>Compute</c> call
///   re-runs the group grant's <c>Sync</c>.
///
/// - <b>"Whenever a Vampire you control deals combat damage to a player, put a
///   +1/+1 counter on it." (CR 603.1 / CR 510)</b>: a per-instance
///   <see cref="CombatDamageDealtEvent"/> handler that matches a combat-damage
///   event whose source is a <see cref="Creature"/> with the
///   <see cref="CardSubtype.Vampire"/> subtype, controlled by Stensia
///   Masquerade's controller, dealing damage TO A PLAYER (not a creature /
///   planeswalker). On a match it puts one +1/+1 counter ON THAT VAMPILE — the
///   "it" of the printed text is the damage-dealing Vampire, not Stensia
///   Masquerade — via <see cref="CountersService.Add"/> (CR 122.1, published as
///   a <see cref="CounterAddedEvent"/> so downstream payoffs chain).
///   <see cref="EventBus.Publish"/> dispatches on the STATIC generic type, so
///   combat damage (published as <see cref="CombatDamageDealtEvent"/>) reaches
///   this subscriber; non-combat damage (the base
///   <see cref="DamageDealtEvent"/>) correctly does NOT (the printed condition
///   is "deals COMBAT damage").
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only (no live continuous effect, no
///   trigger subscription). Suitable for identity / dispatch tests.
/// - <see cref="Create(Player, ContinuousEffectsService)"/> — the production
///   effects-aware overload the source generator's instance-swap dispatch
///   matches (<c>NamedCardFactory.CreateGeneratedWithEffects</c>). Wires the
///   Layer-6 attacking-creatures first-strike anthem AND the Vampire
///   combat-damage trigger against the live service + its event bus.
///
/// ## v1 simplifications
///
/// - The +1/+1 counter placement passes a null <see cref="ReplacementBus"/>
///   (none is exposed through <see cref="ContinuousEffectsService"/>), so a
///   Hardened Scales / Doubling Season rewrite (CR 614) of THIS counter is not
///   modelled; the counter is still placed and publishes
///   <see cref="CounterAddedEvent"/>. Same posture as the other effects-aware
///   factories routed through <see cref="ContinuousEffectsService.EventBus"/>.
/// </summary>
[CardName("Stensia Masquerade")]
public static class StensiaMasqueradeFactory
{
    public const string CardName = "Stensia Masquerade";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>+1/+1 counters placed per Vampire combat-damage-to-player
    /// event (CR 122.1).</summary>
    public const int CountersPerHit = 1;

    /// <summary>
    /// Construct Stensia Masquerade with no live continuous-effects service (the
    /// shape / dispatcher path). The first-strike anthem is NOT registered (no
    /// service), but the Vampire combat-damage <see cref="TriggeredAbility"/>
    /// marker IS attached so factory-shape / dispatcher / trigger-wiring-audit
    /// tests see it. With no <see cref="ContinuousEffectsService.EventBus"/> the
    /// trigger is inert (the marker's effect closure is only driven through the
    /// live TriggerManager auto-bind in production).
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (<c>NamedCardFactory.CreateGeneratedWithEffects</c>
    /// requires this exact <c>Create(Player, ContinuousEffectsService)</c>
    /// signature). When <paramref name="effects"/> is supplied the Layer-6
    /// "Attacking creatures you control have first strike" keyword anthem is
    /// registered against the live service; the Vampire combat-damage →
    /// +1/+1-counter <see cref="TriggeredAbility"/> marker is attached on BOTH
    /// paths (in production it fires via the TriggerManager that auto-binds a
    /// card's <see cref="ITriggeredAbility"/>s when it enters the battlefield).
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        var eventBus = effects?.EventBus;

        // ----------------------------------------------------------------
        // "Attacking creatures you control have first strike." (CR 613.1f /
        // CR 702.7). A Layer-6 keyword anthem over the DYNAMIC set of
        // currently-attacking creatures the controller controls
        // (AttackingCreaturesKeywordAnthemEffect). Its AppliesTo predicate reads
        // the live per-game combat registry (CR 508.4), so a creature gains first
        // strike the instant it is declared as an attacker and loses it when it
        // leaves combat — no zone move is needed, and (unlike a
        // GrantAbilityToGroupStaticEffect marker) the keyword surfaces in the
        // SAME Compute pass because the effect adds it directly to the working
        // Keywords set in Apply. The effect is gated by its own IsActive() check
        // (source on the battlefield), so it self-revokes when Stensia leaves
        // play (CR 611.2c) — no explicit unregister needed (same posture as the
        // LordStaticEffect anthem family, e.g. Empyrean Eagle).
        // ----------------------------------------------------------------
        effects?.Register(new AttackingCreaturesKeywordAnthemEffect(card, "First strike"));

        // ----------------------------------------------------------------
        // "Whenever a Vampire you control deals combat damage to a player, put a
        //  +1/+1 counter on it." (CR 603.1 / CR 510). "it" = the Vampire that
        //  dealt the damage (NOT Stensia Masquerade). Wired as a real
        //  TriggeredAbility marker so the prod TriggerManager (which auto-binds a
        //  card's ITriggeredAbilities on ETB) fires it; same posture as
        //  SengirVampireFactory's damage-linked marker. The triggering Vampire is
        //  captured by the condition predicate when it matches and read by the
        //  effect on resolution — combat damage is dealt and its triggers placed
        //  one event at a time (CR 510.4 / 603.3), so the captured reference is
        //  the just-matched event's source.
        // ----------------------------------------------------------------
        Creature? lastVampire = null;

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                // Only combat damage dealt TO A PLAYER (CR 510 — to a player, not
                // a creature / planeswalker).
                if (e.TargetPlayer == null) return false;
                if (e.Source is not Creature vampire) return false;
                // CR 205.3 — a Vampire …
                if (!vampire.HasSubtype(CardSubtype.Vampire)) return false;
                // … the controller controls (CR 109.5 — "you control") …
                if (!ReferenceEquals(vampire.Controller, card.Controller)) return false;
                // … still on the battlefield (CR 603.10 — "it").
                if (vampire.Zone != ZoneType.Battlefield) return false;

                lastVampire = vampire;
                return true;
            }),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: put a +1/+1 counter on the Vampire that dealt combat damage",
                    () =>
                    {
                        var vampire = lastVampire;
                        if (vampire == null || vampire.Zone != ZoneType.Battlefield) return;
                        // CR 122.1 — put a +1/+1 counter on that Vampire. Routed
                        // through CountersService.Add (publishes CounterAddedEvent
                        // so downstream payoffs chain). No ReplacementBus is
                        // exposed through ContinuousEffectsService, so a Hardened
                        // Scales / Doubling Season rewrite of THIS counter is not
                        // modelled (v1).
                        CountersService.Add(
                            vampire, CounterType.PlusOnePlusOne, CountersPerHit,
                            replacements: null, eventBus);
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);

        return card;
    }
}
