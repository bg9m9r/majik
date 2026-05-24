using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Engineered Plague (Urza's Legacy / reprints,
/// Enchantment {2}{B}).
///
/// Oracle text:
///   "As Engineered Plague enters the battlefield, choose a creature type.
///    All creatures of the chosen type get -1/-1."
///
/// ## Implemented (v1)
/// - Enchantment with mana cost {2}{B}, no subtypes, correct owner /
///   controller.
/// - <b>ETB type choice</b> (CR 614.12 — "as ~ enters, choose a creature
///   type"): resolved eagerly via a <c>Func&lt;Player, CardSubtype&gt;</c>
///   selector on the 2-arg
///   <see cref="Create(Player, ContinuousEffectsService?, Func{Player, CardSubtype}?)"/>
///   overload. Same pattern as <see cref="PlagueEngineerFactory"/>;
///   the choice is exposed via <see cref="GetChosenType(Enchantment)"/>
///   for tests/introspection.
/// - <b>Static "-1/-1 to ALL creatures of the chosen type"</b>: wired via
///   <see cref="LordStaticEffect"/> with <c>allPlayers: true</c> and
///   <c>power: -1, toughness: -1</c>. Unlike Plague Engineer (which uses
///   <c>opponentsOnly: true</c>), Engineered Plague uses the allPlayers path
///   that bypasses the controller filter entirely — every creature of the
///   chosen type on any player's battlefield is debuffed. Layer 7c
///   (CR 613.7c). The effect's <see cref="ContinuousEffect.IsActive"/>
///   already gates on the source being on the battlefield, so LTB / flicker
///   naturally lifts the debuff.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseSubtype prompt. Until that lands, the
///   factory accepts a <c>Func&lt;Player, CardSubtype&gt;</c> selector
///   closure — bots and tests supply the chosen type directly. Same
///   pattern as Pithing Needle's <c>nameSelector</c> and Cavern of
///   Souls's <c>typeChooser</c>.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the ETB
///   replacement; v1 captures it eagerly at factory-build time.
///   Observationally equivalent in the current ETB pipeline.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; its <see cref="ContinuousEffect.IsActive"/> check
///   short-circuits when Engineered Plague isn't on the battlefield, so
///   the debuff lifts correctly.
/// </summary>
[CardName("Engineered Plague")]
public static class EngineeredPlagueFactory
{
    public const string CardName = "Engineered Plague";
    public const string Cost = "{2}{B}";

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // PlagueEngineerFactory / CavernOfSoulsFactory. Keyed by the
    // Enchantment instance so flickers (which produce a new object)
    // get a fresh choice.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Enchantment, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct an Engineered Plague with no live continuous-effects wiring
    /// and no ETB type choice resolved. Suitable for card-shape /
    /// dispatcher tests — the chosen-type slot is unset and
    /// <see cref="GetChosenType"/> returns null; no debuff effect is
    /// registered.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Engineered Plague. When
    /// <paramref name="continuousEffects"/> AND
    /// <paramref name="typeChooser"/> are both supplied, the ETB choice
    /// is resolved eagerly and a <see cref="LordStaticEffect"/> with
    /// <c>opponentsOnly: false</c> and <c>power: -1, toughness: -1</c> is
    /// registered against the layers service. Either being null skips
    /// effect registration (the card shape is always wired).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// -1/-1 static effect against. May be null — no live debuff.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype
    /// at ETB time. Called with Engineered Plague's controller. May be
    /// null — no live debuff (no chosen type means no scope).</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(name: CardName, manaCost: Cost);

        card.SetOwner(owner);
        card.SetController(owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says
            // the choice is made as part of the ETB replacement;
            // observationally equivalent in the current ETB pipeline
            // (mirrors Cavern of Souls / Pithing Needle / Plague Engineer).
            var chosen = typeChooser(owner);
            _chosenType.Add(card, new ChoiceBox { Value = chosen });

            if (continuousEffects != null)
            {
                // CR 613.7c — P/T modification. allPlayers: true means ALL
                // creatures of the chosen type are affected regardless of
                // controller — both the controller's own and opponents'
                // creatures (unlike Plague Engineer's opponentsOnly: true,
                // which spares the controller's side). Engineered Plague is
                // an Enchantment, not a Creature, so it can never appear in
                // the matching pool — the includeSelf/opponentsOnly flags are
                // bypassed entirely by the allPlayers path.
                continuousEffects.Register(new LordStaticEffect(
                    source: card,
                    matchingSubtype: chosen,
                    power: -1,
                    toughness: -1,
                    grantedKeywords: null,
                    includeSelf: false,
                    opponentsOnly: false,
                    allPlayers: true));
            }
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at
    /// construction time, else null. Per-card (not per-factory) — a
    /// flickered Engineered Plague is a new object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Enchantment engineeredPlague)
    {
        ArgumentNullException.ThrowIfNull(engineeredPlague);
        return _chosenType.TryGetValue(engineeredPlague, out var box) ? box.Value : null;
    }
}
