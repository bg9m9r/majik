using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mutable Explorer (Universes Beyond / FINAL FANTASY,
/// {2}{G}). Creature — Shapeshifter, 1/1.
///
/// Oracle text:
///   "Changeling (This card is every creature type.)
///    When this creature enters, create a tapped Mutavault token.
///    (It's a land with '{T}: Add {C}' and '{1}: This token becomes a 2/2
///     creature with all creature types until end of turn. It's still a land.')"
///
/// ## Implementation
///
/// - 1/1 Shapeshifter with mana cost {2}{G}. Colour identity green
///   (derived from the {G} pip per CR 202.2c).
/// - <b>Changeling (CR 702.73)</b> — the card is every creature type
///   in every zone. v1 modelling:
///     1. The printed <see cref="Card.Subtypes"/> set is stamped with
///        the engine's currently-enumerated creature subtypes
///        (sourced from <see cref="MutavaultAnimateEffect.EveryCreatureType"/>
///        so the changeling list and Mutavault's animate list stay in lockstep
///        — when the enum grows, both pick up the new subtype with no
///        per-card edits) plus the printed
///        <see cref="CardSubtype.Shapeshifter"/> base type. This is the
///        "every creature type" observable equivalent the engine relies on
///        for tribal-lord interactions (Goblin Chieftain, Cavern of Souls
///        naming a tribe, etc.) — same v1 simplification as
///        <see cref="MutavaultAnimateEffect"/>.
///     2. A <see cref="KeywordAbility"/> marker with keyword "Changeling"
///        is attached so downstream consumers (UI, future
///        Changeling-aware enumerations) can detect the printed keyword
///        without scanning the subtype list.
///   The simplification differs from the Mutavault path in that the
///   subtypes are recorded as printed (not via a Layer 4 continuous
///   effect), matching CR 702.73a: "Each object with the changeling ability
///   is each creature type. This ability works everywhere, even outside the
///   game." That static-everywhere posture is exactly what stamping the
///   printed list models — no layer registration needed.
/// - <b>ETB triggered ability (CR 603.1 / 603.6a)</b>: "When this
///   creature enters, create a tapped Mutavault token." Unconditional
///   self-ETB via <see cref="Triggers.OnEnterBattlefieldSelf"/>. Resolution
///   calls <see cref="MutavaultFactory.CreateAsToken"/> with
///   <c>tapped: true</c>, threading the same continuous-effects service
///   and zone service the parent was given so the token's animate ability
///   wires identically to a printed Mutavault. Controller closure
///   re-resolves at execute time so blink / control-change scenarios
///   create the token under the correct player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger is attached
///   to the card for shape inspection but not registered with any
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, ZoneService?, TriggerManager?)"/>
///   — fully wired. The ETB trigger is registered with
///   <paramref name="triggers"/> so CardMovedEvent on the bus routes it
///   to the stack; the resulting Mutavault token's animate ability is
///   registered against the supplied continuous-effects service when its
///   {1} ability resolves.
///
/// ## Design notes
/// Changeling + the printed Mutavault-token oracle is the same pair
/// pattern as Chameleon Colossus / Mirror Entity (Changeling + body) plus
/// a token-creation rider, structurally closest to the "create a
/// Treasure token" ETB family (Goldspan Dragon, Smothering Tithe) — the
/// difference is the token is a Land, so the token construction routes
/// through <see cref="MutavaultFactory.CreateAsToken"/> instead of
/// <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield"/>.
/// </summary>
[CardName("Mutable Explorer")]
public static class MutableExplorerFactory
{
    public const string CardName = "Mutable Explorer";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Mutable Explorer with no live wiring. ETB trigger is
    /// attached but not registered with any
    /// <see cref="TriggerManager"/>; token creation in the ETB closure
    /// still resolves but without the continuous-effects / zone services
    /// the resulting Mutavault token's animate ability won't register
    /// continuous effects. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, zones: null, triggers: null);

    /// <summary>
    /// Construct Mutable Explorer with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so CardMovedEvent on the bus routes it to the stack
    /// (the standard ETB wiring). The continuous-effects and zone
    /// services thread into the ETB closure so the spawned Mutavault
    /// token's animate ability registers correctly when activated.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 702.73a — Changeling: this card is every creature type.
        // Stamp the engine's currently-modelled creature subtype set on
        // the printed body so HasSubtype(Goblin), HasSubtype(Elf), etc.
        // all return true everywhere (battlefield + every other zone),
        // plus the printed Shapeshifter base type. Sourced from
        // MutavaultAnimateEffect.EveryCreatureType so the changeling list
        // and Mutavault's animate list stay in lockstep.
        var subtypes = new List<CardSubtype> { CardSubtype.Shapeshifter };
        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            if (st == CardSubtype.Shapeshifter) continue; // dedupe
            subtypes.Add(st);
        }

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: subtypes);

        card.SetOwner(owner);
        card.SetController(owner);

        // Changeling keyword marker (CR 702.73). Observational — the
        // subtype stamping above is what drives tribal-lord interactions;
        // this marker is for UI / future Changeling-aware enumerations
        // (e.g. "this creature has Changeling" predicates).
        card.AddAbility(new KeywordAbility("Changeling", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / 603.6a).
        //   "When this creature enters, create a tapped Mutavault token."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply). The token construction routes through
        // MutavaultFactory.CreateAsToken with tapped: true so the token
        // enters the battlefield tapped and wires its own animate ability
        // against the supplied ContinuousEffectsService.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: create a tapped Mutavault token (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                MutavaultFactory.CreateAsToken(
                    controller: controller,
                    effects: effects,
                    zones: zones,
                    tapped: true);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
