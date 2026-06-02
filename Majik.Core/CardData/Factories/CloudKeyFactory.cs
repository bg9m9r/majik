using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cloud Key (Future Sight — Artifact {2}).
///
/// Oracle text (verified against Scryfall):
///   "As this artifact enters, choose artifact, creature, enchantment,
///    instant, or sorcery.
///    Spells you cast of the chosen type cost {1} less to cast."
///
/// The base shape (name, Artifact, {2}) is materialised from the embedded
/// JSON definition (<c>cloud-key.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The as-enters type choice and
/// the chosen-type cost-reduction rider are layered on here — the JSON
/// <c>AbilityDefinition</c> schema expresses neither an as-enters choose-a-type
/// nor a parameterised cost reducer (same posture as
/// <see cref="AdaptiveAutomatonFactory"/>).
///
/// ## Implemented
///
/// ### "As this artifact enters, choose artifact, creature, enchantment,
/// instant, or sorcery." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Player, CardType&gt;</c> selector on the
/// wired overload — the same pattern Adaptive Automaton / Metallic Mimic use
/// for as-enters subtype choices (the engine has no ChooseType agent prompt
/// yet). The chosen <see cref="CardType"/> is stored per-card and exposed via
/// <see cref="GetChosenType"/>. The choice is restricted to the five card
/// types named on the card; the wired overload throws on anything else.
///
/// ### "Spells you cast of the chosen type cost {1} less to cast." (CR 117.7)
/// Wired via <see cref="SpellCostReductionAbility"/> — the exact shape used by
/// <see cref="EtheriumSculptorFactory"/> (artifact spells) and
/// <see cref="GoblinElectromancerFactory"/> (instant/sorcery), here
/// parameterised by the as-enters chosen type. The predicate gates on the
/// spell carrying the chosen <see cref="CardType"/>; the reduction is a flat 1
/// generic per cast. Scoped to the caster's battlefield by
/// <see cref="CostReduction.GetEffectiveCost"/> — only the controller of this
/// Cloud Key benefits ("spells you cast"). Coloured pips are untouched
/// (CR 117.7c); floor-at-zero is enforced inside the cost-calc helper. The
/// rider is only attached when a type is chosen — the parameterless
/// <see cref="Create(Player)"/> overload (used by dispatcher / card-shape
/// tests) attaches no rider.
///
/// Multiple Cloud Keys stack: two Cloud Keys both naming "instant" reduce each
/// instant spell by {2}. Note Cloud Key itself is an Artifact, so a Cloud Key
/// naming "artifact" discounts a later Cloud Key cast.
///
/// ## Deferred
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseType prompt. The wired overload accepts a
///   <c>Func&lt;Player, CardType&gt;</c> selector closure — bots and tests
///   supply the chosen type directly. Same posture as Adaptive Automaton.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the as-enters
///   replacement; v1 captures it eagerly at factory-build time. Observationally
///   equivalent in the current ETB pipeline.
/// </summary>
[CardName("Cloud Key")]
public static class CloudKeyFactory
{
    public const string CardName = "Cloud Key";
    public const string Slug = "cloud-key";

    /// <summary>The five card types the as-enters choice is restricted to
    /// (CR 614.12 — Cloud Key names exactly these).</summary>
    public static readonly IReadOnlyList<CardType> ChoosableTypes = new[]
    {
        CardType.Artifact,
        CardType.Creature,
        CardType.Enchantment,
        CardType.Instant,
        CardType.Sorcery,
    };

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // AdaptiveAutomatonFactory. Keyed by the Artifact instance so a flicker
    // (which produces a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Artifact, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardType Value; }

    /// <summary>
    /// Construct Cloud Key with no as-enters choice resolved. Suitable for
    /// card-shape / dispatcher tests — the chosen-type slot is unset,
    /// <see cref="GetChosenType"/> returns null, and no cost-reduction rider is
    /// attached. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, typeChooser: null);

    /// <summary>
    /// Construct a Cloud Key, optionally resolving the as-enters type choice.
    /// When <paramref name="typeChooser"/> is supplied the chosen
    /// <see cref="CardType"/> is captured eagerly and the
    /// <see cref="SpellCostReductionAbility"/> rider for that type is attached.
    /// The card shape (Artifact, {2}) is always wired regardless.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="typeChooser">Resolves the chosen card type at as-enters
    /// time, called with Cloud Key's controller. May be null — no choice is
    /// made and no rider is attached. Must return one of
    /// <see cref="ChoosableTypes"/> (CR 614.12) or an
    /// <see cref="ArgumentOutOfRangeException"/> is thrown.</param>
    public static Artifact Create(Player owner, Func<Player, CardType>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact, {2}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says the
            // choice is made as part of the as-enters replacement;
            // observationally equivalent in the current ETB pipeline (mirrors
            // Adaptive Automaton).
            var chosen = typeChooser(owner);
            if (!ChoosableTypes.Contains(chosen))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(typeChooser),
                    chosen,
                    "Cloud Key may only name artifact, creature, enchantment, " +
                    "instant, or sorcery (CR 614.12).");
            }

            _chosenType.AddOrUpdate(card, new ChoiceBox { Value = chosen });

            // CR 117.7 — "Spells you cast of the chosen type cost {1} less to
            // cast." Predicate gates on the spell carrying the chosen type;
            // reduction is a flat 1 generic. CostReduction.GetEffectiveCost
            // scans only the caster's battlefield for this ability shape, so
            // the "you cast" scope is enforced by the cost-calc helper.
            card.AddAbility(new SpellCostReductionAbility(
                predicate: c => c.HasType(chosen),
                reduction: (_, _) => 1,
                description:
                    $"Spells you cast of the chosen type ({chosen.ToString().ToLowerInvariant()}) " +
                    "cost {1} less to cast."));
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen card type if one was resolved at construction time,
    /// else null. Per-card (not per-factory) — a flickered Cloud Key is a new
    /// object and chooses again.
    /// </summary>
    public static CardType? GetChosenType(Artifact cloudKey)
    {
        ArgumentNullException.ThrowIfNull(cloudKey);
        return _chosenType.TryGetValue(cloudKey, out var box) ? box.Value : null;
    }
}
