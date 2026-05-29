using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Adaptive Automaton (Magic 2012 — Artifact Creature
/// — Construct {3} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "As this creature enters, choose a creature type.
///    This creature is the chosen type in addition to its other types.
///    Other creatures you control of the chosen type get +1/+1."
///
/// The base shape (name, Artifact Creature — Construct, {3}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>adaptive-automaton.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two as-enters behaviours are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express the as-enters type choice nor a chosen-type anthem, so they live in
/// the factory (same posture as <see cref="MetallicMimicFactory"/> /
/// <see cref="PlagueEngineerFactory"/>).
///
/// ## Implemented
///
/// ### "As this creature enters, choose a creature type." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Player, CardSubtype&gt;</c> selector on
/// the wired overload — same pattern as <see cref="MetallicMimicFactory"/> /
/// <see cref="PlagueEngineerFactory"/> / <see cref="CavernOfSoulsFactory"/>
/// (the engine has no ChooseSubtype agent prompt yet). The choice is stored
/// per-card and exposed via <see cref="GetChosenType"/>.
///
/// ### "This creature is the chosen type in addition to its other types."
/// (CR 613.1d — Layer 4 type-adding effect.) Wired via
/// <see cref="AddSubtypeEffect"/> registered on the supplied
/// <see cref="ContinuousEffectsService"/>. The effect's
/// <see cref="ContinuousEffect.IsActive"/> already gates on the source being on
/// the battlefield, so the granted type lifts when Adaptive Automaton leaves.
/// Additive — Adaptive Automaton keeps its Construct subtype (CR 205.3).
///
/// ### "Other creatures you control of the chosen type get +1/+1."
/// (CR 613.7c — Layer 7c P/T modification.) Wired via
/// <see cref="LordStaticEffect"/> with the default controller filter
/// (<c>opponentsOnly: false</c>, <c>allPlayers: false</c>) so the buff applies
/// only to creatures the source's controller controls, and
/// <c>includeSelf: false</c> honours "Other" (CR 109.5). The effect's
/// <see cref="ContinuousEffect.IsActive"/> gates on the source being on the
/// battlefield, so the buff lifts on LTB/flicker (same posture as
/// <see cref="LordOfAtlantisFactory"/> / <see cref="PlagueEngineerFactory"/>).
///
/// ## Deferred
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseSubtype prompt. The wired overload accepts a
///   <c>Func&lt;Player, CardSubtype&gt;</c> selector closure — bots and tests
///   supply the chosen type directly. Same posture as Metallic Mimic /
///   Engineered Plague / Cavern of Souls.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the as-enters
///   replacement; v1 captures it eagerly at factory-build time. Observationally
///   equivalent in the current ETB pipeline.
/// - <b>LTB unregister</b>: the registered effects stay on the
///   <see cref="ContinuousEffectsService"/> across zone changes; their
///   <see cref="ContinuousEffect.IsActive"/> checks short-circuit when Adaptive
///   Automaton isn't on the battlefield so the bonuses lift correctly (same
///   shape as Lord of Atlantis / Plague Engineer).
/// </summary>
[CardName("Adaptive Automaton")]
public static class AdaptiveAutomatonFactory
{
    public const string CardName = "Adaptive Automaton";
    public const string Slug = "adaptive-automaton";

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // MetallicMimicFactory / PlagueEngineerFactory. Keyed by the Creature
    // instance so a flicker (which produces a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Creature, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct Adaptive Automaton with no live wiring and no as-enters choice
    /// resolved. Suitable for card-shape / dispatcher tests — the chosen-type
    /// slot is unset, <see cref="GetChosenType"/> returns null, and neither the
    /// type-adding effect nor the anthem is registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Adaptive Automaton. When
    /// <paramref name="typeChooser"/> is supplied the as-enters creature-type
    /// choice is resolved eagerly. When <paramref name="continuousEffects"/> is
    /// also supplied, both the chosen-type <see cref="AddSubtypeEffect"/> and
    /// the +1/+1 <see cref="LordStaticEffect"/> are registered against the
    /// layers service. The card shape is always wired regardless of which
    /// services are present.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// chosen-type <see cref="AddSubtypeEffect"/> and the +1/+1
    /// <see cref="LordStaticEffect"/> against. May be null — no live
    /// effects.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype at
    /// as-enters time, called with Adaptive Automaton's controller. May be
    /// null — no choice is made and neither chosen-type effect activates.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact Creature —
        // Construct, {3}, 2/2).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says the
            // choice is made as part of the as-enters replacement;
            // observationally equivalent in the current ETB pipeline (mirrors
            // Metallic Mimic / Engineered Plague / Cavern of Souls).
            var chosen = typeChooser(owner);
            _chosenType.AddOrUpdate(card, new ChoiceBox { Value = chosen });

            if (continuousEffects != null)
            {
                // CR 613.1d — "This creature is the chosen type in addition to
                // its other types." Additive Layer 4 type-add; keeps the
                // printed Construct subtype (CR 205.3).
                continuousEffects.Register(new AddSubtypeEffect(card, chosen));

                // CR 613.7c — "Other creatures you control of the chosen type
                // get +1/+1." Default controller filter (own creatures only),
                // includeSelf: false honours "Other" (CR 109.5).
                continuousEffects.Register(new LordStaticEffect(
                    source: card,
                    matchingSubtype: chosen,
                    power: 1,
                    toughness: 1,
                    grantedKeywords: null,
                    includeSelf: false,
                    opponentsOnly: false,
                    allPlayers: false));
            }
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at construction
    /// time, else null. Per-card (not per-factory) — a flickered Adaptive
    /// Automaton is a new object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Creature adaptiveAutomaton)
    {
        ArgumentNullException.ThrowIfNull(adaptiveAutomaton);
        return _chosenType.TryGetValue(adaptiveAutomaton, out var box) ? box.Value : null;
    }
}
