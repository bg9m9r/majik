using System;
using System.Collections.Generic;
using System.Linq;
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
/// Named-card factory for Dwynen, Gilt-Leaf Daen (Magic Origins, {2}{G}{G}).
/// Legendary Creature — Elf Warrior 3/4. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "Reach (This creature can block creatures with flying.)
///    Other Elf creatures you control get +1/+1.
///    Whenever Dwynen attacks, you gain 1 life for each attacking Elf you control."
///
/// The mono-G Elf tribal lord — a Reach body, a tribal anthem, and an
/// attack-trigger lifegain engine on one card. The base shape (name, Legendary
/// Creature — Elf Warrior, {2}{G}{G}, 3/4, Reach) is materialised from the
/// embedded JSON definition (<c>dwynen-gilt-leaf-daen.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two non-keyword abilities are
/// layered on in C# (the JSON <c>AbilityDefinition</c> schema does not express a
/// tribal anthem nor a scaling attack trigger) — same posture as
/// <see cref="ImperiousPerfectFactory"/> (anthem) and
/// <see cref="SvyelunOfSeaAndSkyFactory"/> / <see cref="AtarkaWorldRenderFactory"/>
/// (attack trigger).
///
/// ## Implemented (v1)
///
/// ### Reach (CR 702.9 / 509.1b)
/// The "can block creatures with flying" keyword rides on the JSON definition
/// (<c>"keywords": ["Reach"]</c>) and is materialised as a
/// <see cref="KeywordAbility"/> marker by <see cref="CardDefinitionFactory"/> —
/// same path as every other plain keyword on a JSON-defined creature
/// (Bloodthirsty Conqueror's Flying/Deathtouch).
///
/// ### "Other Elf creatures you control get +1/+1." (CR 613.7c — Layer 7c P/T)
/// Wired via <see cref="LordStaticEffect"/> with
/// <c>matchingSubtype: Elf, power: 1, toughness: 1, includeSelf: false,
/// opponentsOnly: false, allPlayers: false</c> — controller-scoped (opponents'
/// Elves are unaffected per CR 109.5) and <c>includeSelf: false</c> honours the
/// printed "Other". Identical shape to <see cref="ElvishArchdruidFactory"/> /
/// <see cref="ImperiousPerfectFactory"/>. The effect's
/// <see cref="ContinuousEffect.IsActive"/> gates on the source being on the
/// battlefield, so the buff lifts on LTB / flicker.
///
/// ### "Whenever Dwynen attacks, you gain 1 life for each attacking Elf you control." (CR 508.1f / 119)
/// A <see cref="TriggeredAbility"/> over <see cref="Triggers.OnAttackSelf"/> —
/// the per-attacker self-trigger keyed on Dwynen itself (NOT "a creature you
/// control attacks", so the trigger fires once, when Dwynen is declared as an
/// attacker — CR 508.1f). On resolution the effect re-reads the live attacker
/// set via the supplied <c>attackingCreaturesSource</c> closure (same
/// production-wiring shape as <see cref="AtarkaWorldRenderFactory"/> — there is
/// no live <c>ICurrentCombatProvider</c> yet), counts the attacking Elves the
/// controller controls (Dwynen is an Elf, so it counts itself), and the
/// controller gains that much life via <see cref="Player.GainLife"/> (CR 119.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat-attackers provider</b>: production callers must wire the
///   <c>attackingCreaturesSource</c> closure manually. Once
///   <c>ICurrentCombatProvider</c> ships, this factory will read the live
///   attackers off the provider directly — same posture as
///   <see cref="AtarkaWorldRenderFactory"/> and every other count-the-attackers
///   factory in this repo. When the closure is null the lifegain effect is a
///   no-op (the trigger still fires, but gains 0 life).
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/> stays
///   on the <see cref="ContinuousEffectsService"/> across zone changes; its
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Dwynen isn't on
///   the battlefield so the anthem lifts correctly (same posture as
///   <see cref="ElvishArchdruidFactory"/> / <see cref="ImperiousPerfectFactory"/>).
/// </summary>
[CardName("Dwynen, Gilt-Leaf Daen")]
public static class DwynenGiltLeafDaenFactory
{
    public const string CardName = "Dwynen, Gilt-Leaf Daen";
    public const string Slug = "dwynen-gilt-leaf-daen";

    /// <summary>
    /// Construct Dwynen with no live runtime services. Suitable for card-shape /
    /// dispatcher tests — Reach is materialised from the JSON, the +1/+1 anthem
    /// is NOT registered (no layers service), and the attack-trigger is attached
    /// for inspection but not registered with a bus and has no live attackers
    /// source (so on resolution it gains 0 life). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct a fully-wired Dwynen, Gilt-Leaf Daen.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Elf creatures you control get +1/+1" <see cref="LordStaticEffect"/>
    /// against. May be null — no live anthem.</param>
    /// <param name="triggers">TriggerManager to register the attack-trigger
    /// against. May be null — the trigger is attached for inspection but does not
    /// fire on a live bus.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list. Called at trigger resolution to count attacking
    /// Elves the controller controls. May be null — the lifegain effect then
    /// gains 0 life (same posture as <see cref="AtarkaWorldRenderFactory"/>).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature — Elf
        // Warrior, {2}{G}{G}, 3/4, Reach). Reach is materialised as a
        // KeywordAbility marker by CardDefinitionFactory; the two non-keyword
        // abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Other Elf creatures you control get +1/+1." — CR 613.7c (Layer 7c
        // P/T) + CR 109.5 (controller scope). allPlayers: false → opponents'
        // Elves aren't pumped; includeSelf: false honours the printed "Other".
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // "Whenever Dwynen attacks, you gain 1 life for each attacking Elf you
        // control." (CR 508.1f — per-attacker self trigger; CR 119.3 — life
        // gain.) OnAttackSelf keys on Dwynen itself, so the trigger fires once,
        // when Dwynen is declared as an attacker. On resolution the effect
        // re-reads the live attacker set, counts the attacking Elves the
        // controller controls (Dwynen is an Elf — it counts itself, no "other"
        // qualifier), and the controller gains that much life.
        // ----------------------------------------------------------------
        var lifegainEffect = new Effect(
            $"{CardName}: gain 1 life for each attacking Elf you control (CR 508.1f / 119.3)",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var controller = card.Controller ?? owner;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                int attackingElves = attackers.Count(a =>
                    a != null
                    && a.HasSubtype(CardSubtype.Elf)
                    && ReferenceEquals(a.Controller, controller));

                if (attackingElves > 0)
                {
                    controller.GainLife(attackingElves);
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
