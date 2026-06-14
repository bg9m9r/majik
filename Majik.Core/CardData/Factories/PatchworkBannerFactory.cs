using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Patchwork Banner (The Brothers' War — Artifact {3}).
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "As this artifact enters, choose a creature type.
///    Creatures you control of the chosen type get +1/+1.
///    {T}: Add one mana of any color."
///
/// This is a colourless mana rock + chosen-type anthem hybrid — the
/// "choose a creature type as it enters" + chosen-type anthem half mirrors
/// <see cref="AdaptiveAutomatonFactory"/>, and the "{T}: Add one mana of any
/// color" half mirrors <see cref="ArcaneSignetFactory"/> (five WUBRG
/// <see cref="ManaAbility"/> slots baked into the JSON definition).
///
/// ## Composition
/// - Base shape (name, Artifact type, {3} cost, and the five WUBRG mana
///   abilities that model "{T}: Add one mana of any color") is materialised
///   from the embedded JSON definition (<c>patchwork-banner.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. CR 605.1 — each is a mana
///   ability; the picker satisfies any single colour pip by selecting the
///   matching slot (CR 106.6).
/// - The as-enters type choice and the chosen-type anthem are layered on
///   here because the JSON schema expresses neither — same posture as
///   <see cref="AdaptiveAutomatonFactory"/>.
///
/// ## "As this artifact enters, choose a creature type." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Player, CardSubtype&gt;</c> selector on
/// the wired overload — the engine has no ChooseSubtype agent prompt yet, so
/// bots/tests supply the chosen type directly (same shape as Adaptive
/// Automaton / Cavern of Souls / Metallic Mimic). Stored per-card and exposed
/// via <see cref="GetChosenType"/>. CR 614.12 strictly says the choice is part
/// of the as-enters replacement; v1 captures it eagerly at factory-build time,
/// observationally equivalent in the current ETB pipeline.
///
/// ## "Creatures you control of the chosen type get +1/+1." (CR 613.7c)
/// Wired via <see cref="LordStaticEffect"/> with the default controller filter
/// (<c>opponentsOnly: false</c>, <c>allPlayers: false</c>) so the buff applies
/// only to creatures the source's controller controls. Note the printed text
/// is "Creatures" — NOT "Other creatures" — so <c>includeSelf: true</c>; this
/// is moot in practice because Patchwork Banner is an Artifact, not a creature,
/// so it never matches the creature-type filter, but it faithfully models the
/// printed scope. The effect's <see cref="ContinuousEffect.IsActive"/> gates on
/// the source being on the battlefield, so the buff lifts on LTB/flicker (same
/// posture as Adaptive Automaton / Lord of Atlantis).
///
/// ## Deferred
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   has no ChooseSubtype prompt; the wired overload takes a selector closure.
///   Same posture as Adaptive Automaton / Cavern of Souls.
/// - <b>Choice timing</b>: eager at factory-build time (see above); same
///   migration path Adaptive Automaton / Cavern of Souls have queued.
/// </summary>
[CardName("Patchwork Banner")]
public static class PatchworkBannerFactory
{
    public const string CardName = "Patchwork Banner";
    public const string Slug = "patchwork-banner";

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // AdaptiveAutomatonFactory. Keyed by the Artifact instance so a flicker
    // (which produces a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Artifact, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct Patchwork Banner with no live wiring and no as-enters choice
    /// resolved. Suitable for card-shape / dispatcher tests — the chosen-type
    /// slot is unset, <see cref="GetChosenType"/> returns null, and the anthem
    /// is not registered. The five WUBRG mana abilities are still wired (from
    /// the JSON). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Patchwork Banner. When
    /// <paramref name="typeChooser"/> is supplied the as-enters creature-type
    /// choice is resolved eagerly. When <paramref name="continuousEffects"/> is
    /// also supplied, the +1/+1 chosen-type <see cref="LordStaticEffect"/> is
    /// registered against the layers service. The card shape (including the
    /// mana abilities) is always wired regardless of which services are present.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the +1/+1
    /// chosen-type <see cref="LordStaticEffect"/> against. May be null — no
    /// live anthem.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype at
    /// as-enters time, called with Patchwork Banner's controller. May be null —
    /// no choice is made and the anthem does not activate.</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact, {3}, and the
        // five WUBRG "{T}: Add one mana of any color" mana abilities).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says the
            // choice is made as part of the as-enters replacement;
            // observationally equivalent in the current ETB pipeline (mirrors
            // Adaptive Automaton / Cavern of Souls / Metallic Mimic).
            var chosen = typeChooser(owner);
            _chosenType.AddOrUpdate(card, new ChoiceBox { Value = chosen });

            if (continuousEffects != null)
            {
                // CR 613.7c — "Creatures you control of the chosen type get
                // +1/+1." Default controller filter (own creatures only).
                // includeSelf: true faithfully models the printed "Creatures"
                // (not "Other"); moot in practice since an Artifact never
                // matches a creature-type filter.
                continuousEffects.Register(new LordStaticEffect(
                    source: card,
                    matchingSubtype: chosen,
                    power: 1,
                    toughness: 1,
                    grantedKeywords: null,
                    includeSelf: true,
                    opponentsOnly: false,
                    allPlayers: false));
            }
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at construction
    /// time, else null. Per-card (not per-factory) — a flickered Patchwork
    /// Banner is a new object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Artifact patchworkBanner)
    {
        ArgumentNullException.ThrowIfNull(patchworkBanner);
        return _chosenType.TryGetValue(patchworkBanner, out var box) ? box.Value : null;
    }
}
