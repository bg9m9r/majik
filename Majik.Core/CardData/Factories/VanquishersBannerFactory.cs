using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vanquisher's Banner (Ixalan, {5}).
///
/// Artifact. Oracle text:
///   "As Vanquisher's Banner enters the battlefield, choose a creature
///    type."
///   "Creatures you control of the chosen type get +1/+1."
///   "Whenever you cast a creature spell, if it shares a creature type
///    with the chosen type, draw a card."
///
/// ## Implementation
///
/// - <b>ETB type choice</b> (CR 614.12 — "as ~ enters" replacement /
///   choose-mode-on-ETB) — the engine has no ChooseSubtype agent prompt
///   yet, so we follow the
///   <see cref="CavernOfSoulsFactory"/> / <see cref="PithingNeedleFactory"/>
///   pattern: the choice is captured eagerly at factory time via an
///   optional <c>Func&lt;Player, CardSubtype&gt;</c> chooser. v1 stores
///   it on the card via a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
///   so the chosen value doesn't leak as a mutable property on
///   <see cref="Artifact"/>. Retrievable via <see cref="GetChosenType"/>.
///   <see cref="Create(Player)"/> (no chooser) leaves the chosen type
///   <c>null</c> — both downstream effects gate on a non-null type and
///   silently no-op when unresolved. This matches the existing deferral
///   on Cavern of Souls.
///
/// - <b>Static "creatures you control of the chosen type get +1/+1"</b>
///   (CR 613 Layer 7c) — registered via
///   <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: chosenType</c>, <c>power: 1, toughness: 1</c>,
///   <c>includeSelf: true</c> (the Banner itself is an Artifact, not a
///   creature — includeSelf is moot but matches the "no 'other' clause"
///   reading of the printed text), <c>opponentsOnly: false</c> (default
///   "you control" filter). When no type was chosen the static is not
///   registered (no-subtype means no creatures match — saves a wasted
///   registration). Mirrors the
///   <see cref="GoblinChieftainFactory"/> / Lord-of-Atlantis shape.
///
/// - <b>Cast trigger</b> (CR 603.1, fires off <see cref="SpellCastEvent"/>):
///   predicate is
///   <c>spell.Controller == this card's controller</c> AND the spell's
///   card has <see cref="CardType.Creature"/> AND the spell shares a
///   creature subtype with the chosen type. "Shares a creature type with
///   the chosen type" reduces to "the spell card has the chosen subtype"
///   (since the chosen-type pool is a single subtype). Effect draws one
///   card via <see cref="Majik.Core.Primitives.Fx.DrawCards"/> for the
///   Banner's controller. When no type was chosen at factory time the
///   trigger is still attached but the predicate always evaluates false
///   (deterministic no-op for shape-only tests). Same shape as
///   <see cref="SramSeniorEdificerFactory"/>.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No chosen type, no
///   live wiring. Cast trigger attached for inspection but never matches
///   (no type); static effect not registered. Suitable for dispatcher /
///   shape tests.
/// - <see cref="Create(Player, Func{Player, CardSubtype}?, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired. When the chooser is supplied the chosen subtype is
///   captured; the +1/+1 static registers against
///   <paramref name="continuousEffects"/> (if non-null); the cast
///   trigger registers against <paramref name="triggers"/> (if non-null).
///
/// ## Deferred
///
/// - <b>Real ChooseSubtype agent prompt</b> — once
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> grows a
///   ChooseSubtype prompt, the v1 closure chooser can be replaced with
///   an ETB replacement that calls into the agent. Same migration path
///   queued for Cavern of Souls + Pithing Needle.
/// - <b>Control change</b> — both the static and the cast trigger
///   capture the controller at register time. If Vanquisher's Banner
///   changes controllers (e.g. Mind Control on an artifact-via-Karn) the
///   "you control" / "you cast" filters do re-evaluate against
///   <c>card.Controller</c> at trigger-fire time — but the registered
///   <see cref="LordStaticEffect"/> reads the source's
///   <see cref="Permanent.Controller"/> live, so it lifts automatically.
///   No explicit re-register needed.
/// </summary>
[CardName("Vanquisher's Banner")]
public static class VanquishersBannerFactory
{
    public const string CardName = "Vanquisher's Banner";
    public const string Cost = "{5}";

    // Per-card chosen creature type. Stored externally to keep
    // Artifact's surface clean — Vanquisher's Banner is the only artifact
    // today that needs "as ~ ETBs, choose a creature type". If more cards
    // land (Door of Destinies, Coat of Arms — though those use cast-time
    // choices), extract a shared Component (same TODO as Cavern of Souls).
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Artifact, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct a Vanquisher's Banner with no ETB type choice resolved
    /// and no live runtime wiring. Suitable for card-shape / dispatcher
    /// tests; the chosen-type slot is unset, the static effect is not
    /// registered, and the cast trigger is attached but never matches.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, typeChooser: null, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a Vanquisher's Banner with optional ETB type choice and
    /// runtime services. When <paramref name="typeChooser"/> is supplied
    /// the chosen subtype is captured eagerly; when
    /// <paramref name="continuousEffects"/> is supplied AND a type was
    /// chosen, the +1/+1 lord static is registered; when
    /// <paramref name="triggers"/> is supplied the cast trigger is
    /// registered against the manager.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<Player, CardSubtype>? typeChooser,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB type choice — CR 614.12.
        // v1: eager-resolve at factory time. Same observational shape as
        // Cavern of Souls / Pithing Needle until ChooseSubtype agent
        // prompt lands. When typeChooser is null the chosen-type slot
        // stays empty and both downstream effects no-op silently.
        // ----------------------------------------------------------------
        CardSubtype? chosenType = null;
        if (typeChooser != null)
        {
            var picked = typeChooser(owner);
            _chosenType.Add(card, new ChoiceBox { Value = picked });
            chosenType = picked;
        }

        // ----------------------------------------------------------------
        // Static "creatures you control of the chosen type get +1/+1"
        // CR 613 Layer 7c. Only registered when a type was actually
        // chosen — without a type there's nothing to match. The
        // LordStaticEffect reads card.Controller live (its sameController
        // check), so a control-change on the Banner re-projects the
        // bonus onto the new controller's creatures automatically.
        //
        // includeSelf: true so a creature of the chosen type owned by
        // the Banner's controller that happens to also share a subtype
        // with the Banner's source (it can't — the Banner is an artifact)
        // would still be buffed. The printed text says "Creatures you
        // control of the chosen type get +1/+1" with no "other" clause,
        // so the inclusive reading is correct.
        // ----------------------------------------------------------------
        if (continuousEffects != null && chosenType.HasValue)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: chosenType.Value,
                power: 1,
                toughness: 1,
                includeSelf: true,
                opponentsOnly: false));
        }

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast a creature spell, if it shares a creature
        //    type with the chosen type, draw a card."
        //
        // Predicate: spell controller == Banner's live controller AND
        // the spell card has type Creature AND (chosen type is set AND
        // the spell card has that subtype). With chosen type unset the
        // predicate is always false (deterministic no-op for shape-only
        // tests).
        //
        // The "intervening if" clause ("if it shares a creature type
        // with the chosen type") is folded into the trigger predicate
        // itself — observationally equivalent for v1, with the strict
        // CR 603.4 reading deferred to when the engine grows a proper
        // intervening-if gate at resolution time. (Same shape as other
        // intervening-if cards we've shipped — see e.g. Bedlam Reveler.)
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            var spellCard = e.Spell.Card;
            if (!spellCard.HasType(CardType.Creature)) return false;

            // Lookup the chosen type at fire-time so a chooser supplied
            // post-factory (future ETB prompt landing) is honoured. v1
            // captures the choice eagerly, so this evaluates the same
            // value, but the lookup pattern matches Cavern of Souls.
            if (!_chosenType.TryGetValue(card, out var box)) return false;
            return spellCard.HasSubtype(box.Value);
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (whenever you cast a creature spell sharing the chosen type)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { Zones.ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at
    /// construction time, else null. The choice is per-card.
    /// </summary>
    public static CardSubtype? GetChosenType(Artifact banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        return _chosenType.TryGetValue(banner, out var box) ? box.Value : null;
    }
}
