using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vampiric Rites (Core Set 2020 / reprints, {B}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "{1}{B}, Sacrifice a creature: You gain 1 life and draw a card."
///
/// ## Why it gets its own factory
/// Vampiric Rites is the repeatable, permanent-based cousin of
/// <see cref="VillageRitesFactory"/>: instead of a one-shot Instant whose
/// additional cast cost is "sacrifice a creature" (and then draw two), it's a
/// {B} Enchantment that sits on the battlefield carrying a repeatable
/// <see cref="ActivatedAbility"/> (CR 602) whose cost is {1}{B} + sacrifice a
/// creature and whose resolution gains the controller 1 life (CR 119.3) and
/// draws ONE card (CR 120). Every primitive already ships — no new engine
/// mechanic is required.
///
/// The base shape (name, single Enchantment card type, {B}) is materialised
/// from the embedded JSON definition (<c>vampiric-rites.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/>. The activated ability is layered on here
/// because the JSON schema doesn't express activated abilities.
///
/// ## Implemented (v1)
/// - Card identity (Enchantment, mana cost {B}, owner / controller wiring).
/// - <b>{1}{B}, Sacrifice a creature: gain 1 life + draw a card</b> — a single
///   <see cref="ActivatedAbility"/> (CR 602) with two costs:
///   <list type="bullet">
///     <item>A <see cref="ManaCostCost"/> for the {1}{B} mana component.</item>
///     <item>A <see cref="SacrificeAnotherCreatureCost"/> for "Sacrifice a
///       creature". Vampiric Rites itself isn't a creature, so the cost's
///       "another" exclusion is vacuously satisfied — every creature the
///       controller controls is eligible (same posture as
///       <see cref="GoblinBombardmentFactory"/>). The cost prompts the
///       controller for WHICH creature via
///       <see cref="IChooseCreatureToSacrificeCost"/> on the live activation
///       path; the factory-direct / dispatcher fallback picks the first
///       eligible creature deterministically.</item>
///   </list>
///   Resolution gains 1 life (CR 119.3) then draws one card (CR 120 — the
///   replacement bus routes the single draw; an empty library stamps the SBA
///   loss flag, CR 704.5b, without throwing).
///
/// ## Rules citations
/// - CR 602 — activated ability "[Cost]: [Effect]".
/// - CR 119.3 — "You gain 1 life."
/// - CR 120 — "draw a card."
/// - CR 701.16a — sacrificing the creature publishes a
///   <see cref="PermanentSacrificedEvent"/> when an event bus is threaded
///   (effects-aware build) so aristocrat payoffs fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-target prompt fallback</b>: on paths that don't prompt
///   (dispatcher / factory-direct tests) the cost auto-picks the first
///   eligible creature. Same queue as the sibling sacrifice-picker costs.
/// </summary>
[CardName("Vampiric Rites")]
public static class VampiricRitesFactory
{
    public const string CardName = "Vampiric Rites";
    public const string Slug = "vampiric-rites";

    /// <summary>CR 121.1 — "draw a card." (one card).</summary>
    public const int DrawAmount = 1;

    /// <summary>CR 119.3 — "You gain 1 life."</summary>
    public const int LifeGain = 1;

    /// <summary>The {1}{B} mana component of the activation cost.</summary>
    public const string AbilityManaCost = "{1}{B}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Vampiric Rites owned and controlled by
    /// <paramref name="owner"/>. Shape-only — no event bus, so paying the
    /// sacrifice cost publishes nothing (legacy posture; dispatcher /
    /// structural tests).
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Festival-Crasher / Aura-of-Silence seam). Threads
    /// <c>effects.EventBus</c> into the sacrifice cost so paying it publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
    /// cost-payer — the seam aristocrat payoffs read.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the <see cref="SacrificeAnotherCreatureCost"/> so paying
    /// it publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Null
    /// preserves the legacy publish-nothing posture.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {B}) from the embedded JSON definition.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {1}{B}, Sacrifice a creature: You gain 1 life and draw a card.
        // CR 602 — activated ability with two costs (mana + sacrifice).
        // CR 119.3 — gain 1 life. CR 120 — draw one card (replacement bus
        // routes the single draw; empty library stamps the SBA loss flag,
        // CR 704.5b).
        // ----------------------------------------------------------------
        var resolveEffect = new Effect(
            $"{CardName}: gain {LifeGain} life and draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 119.3 — gain 1 life FIRST (the printed order).
                Fx.GainLife(controller, LifeGain);
                // CR 120 — draw one card. Per-draw replacement bus; an empty
                // library stamps the SBA loss flag (CR 704.5b) without throwing.
                Fx.DrawCards(controller, DrawAmount);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(AbilityManaCost),
                // Vampiric Rites isn't a creature, so "another" is vacuously
                // satisfied — any creature the controller controls is eligible
                // (CR 602.1). Bus on the cost so the live activation path
                // publishes PermanentSacrificedEvent (CR 701.16a).
                new SacrificeAnotherCreatureCost(card, eventBus),
            },
            effects: new IEffect[] { resolveEffect });

        card.AddAbility(ability);

        return card;
    }
}
