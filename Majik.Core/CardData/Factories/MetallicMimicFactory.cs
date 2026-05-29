using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Metallic Mimic (Aether Revolt — Artifact Creature
/// — Shapeshifter {2} 2/1).
///
/// Oracle text (verified against Scryfall):
///   "As this creature enters, choose a creature type.
///    This creature is the chosen type in addition to its other types.
///    Each other creature you control of the chosen type enters with an
///    additional +1/+1 counter on it."
///
/// The base shape (name, Artifact Creature — Shapeshifter, {2}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>metallic-mimic.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two as-enters behaviours
/// are layered on top here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express the as-enters type choice nor a global ETB-counter
/// replacement, so they live in the factory (same posture as
/// <see cref="ThaliaHereticCatharFactory"/> / <see cref="EngineeredPlagueFactory"/>).
///
/// ## Implemented
///
/// ### "As this creature enters, choose a creature type." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Player, CardSubtype&gt;</c> selector on
/// the wired overload — same pattern as
/// <see cref="EngineeredPlagueFactory"/> / <see cref="PlagueEngineerFactory"/>
/// / <see cref="CavernOfSoulsFactory"/> (the engine has no ChooseSubtype
/// agent prompt yet). The choice is stored per-card and exposed via
/// <see cref="GetChosenType"/>.
///
/// ### "This creature is the chosen type in addition to its other types."
/// (CR 613.1d — Layer 4 type-adding effect.) Wired via
/// <see cref="AddSubtypeEffect"/> registered on the supplied
/// <see cref="ContinuousEffectsService"/>. The effect's
/// <see cref="ContinuousEffect.IsActive"/> already gates on the source being
/// on the battlefield, so the granted type lifts when Metallic Mimic leaves.
/// Additive — Metallic Mimic keeps its Shapeshifter subtype (CR 205.3).
///
/// ### "Each other creature you control of the chosen type enters with an
/// additional +1/+1 counter on it." (CR 614.1d)
/// Wired via <see cref="MetallicMimicEntersWithCounterEffect"/>: while
/// Metallic Mimic is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/> that increments
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> for any
/// battlefield-entry intent carrying a creature of the chosen type that
/// Metallic Mimic's controller will control — excluding Metallic Mimic
/// itself ("each OTHER creature", CR 109.5). The lifecycle unregisters when
/// Metallic Mimic leaves the battlefield. Same global-replacement +
/// ETB/LTB-lifecycle shape as <see cref="ThaliaHereticCatharFactory"/>.
///
/// ## Deferred
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseSubtype prompt. The wired overload accepts
///   a <c>Func&lt;Player, CardSubtype&gt;</c> selector closure — bots and
///   tests supply the chosen type directly. Same posture as Engineered
///   Plague / Cavern of Souls / Pillar of Origins.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the
///   as-enters replacement; v1 captures it eagerly at factory-build time.
///   Observationally equivalent in the current ETB pipeline.
/// </summary>
[CardName("Metallic Mimic")]
public static class MetallicMimicFactory
{
    public const string CardName = "Metallic Mimic";
    public const string Slug = "metallic-mimic";

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // EngineeredPlagueFactory / PlagueEngineerFactory. Keyed by the Creature
    // instance so a flicker (which produces a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Creature, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct Metallic Mimic with no live wiring and no as-enters choice
    /// resolved. Suitable for card-shape / dispatcher tests — the
    /// chosen-type slot is unset, <see cref="GetChosenType"/> returns null,
    /// and neither the type-adding effect nor the ETB-counter replacement is
    /// registered. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, replacementBus: null,
                  eventBus: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Metallic Mimic. When
    /// <paramref name="typeChooser"/> is supplied the as-enters creature-type
    /// choice is resolved eagerly. The type-adding effect is registered when
    /// <paramref name="continuousEffects"/> is also supplied; the
    /// "other creatures enter with a counter" replacement is registered when
    /// <paramref name="replacementBus"/> is supplied. The card shape is
    /// always wired regardless of which services are present.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// chosen-type <see cref="AddSubtypeEffect"/> against. May be null.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the ETB-counter replacement on. May be null.</param>
    /// <param name="eventBus">Event bus for the replacement's ETB/LTB
    /// lifecycle. May be null — the lifecycle still syncs once on Attach.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype at
    /// as-enters time, called with Metallic Mimic's controller. May be null —
    /// no choice is made and neither chosen-type effect activates.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacementBus,
        IEventBus? eventBus,
        Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact Creature —
        // Shapeshifter, {2}, 2/1).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says the
            // choice is made as part of the as-enters replacement;
            // observationally equivalent in the current ETB pipeline
            // (mirrors Engineered Plague / Cavern of Souls / Pillar of
            // Origins).
            var chosen = typeChooser(owner);
            _chosenType.AddOrUpdate(card, new ChoiceBox { Value = chosen });

            // CR 613.1d — "This creature is the chosen type in addition to
            // its other types." Additive Layer 4 type-add; keeps the printed
            // Shapeshifter subtype.
            continuousEffects?.Register(new AddSubtypeEffect(card, chosen));
        }

        // CR 614.1d — "Each other creature you control of the chosen type
        // enters with an additional +1/+1 counter on it." Global ETB
        // replacement registered while Metallic Mimic is on the battlefield.
        // Reads the chosen type lazily so it correctly no-ops until a choice
        // is made.
        if (replacementBus != null)
        {
            var lifecycle = new MetallicMimicEntersWithCounterEffect(
                source: card,
                replacementBus: replacementBus,
                chosenType: () => GetChosenType(card),
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at
    /// construction time, else null. Per-card (not per-factory) — a
    /// flickered Metallic Mimic is a new object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Creature metallicMimic)
    {
        ArgumentNullException.ThrowIfNull(metallicMimic);
        return _chosenType.TryGetValue(metallicMimic, out var box) ? box.Value : null;
    }
}
