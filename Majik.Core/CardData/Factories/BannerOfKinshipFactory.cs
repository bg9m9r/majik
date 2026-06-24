using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Banner of Kinship (Modern Horizons 3 — Artifact {5}).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "As this artifact enters, choose a creature type. This artifact enters
///    with a fellowship counter on it for each creature you control of the
///    chosen type.
///    Creatures you control of the chosen type get +1/+1 for each fellowship
///    counter on this artifact."
///
/// A colourless chosen-type anthem whose magnitude scales off a counter the
/// artifact loads at entry. The "choose a creature type as it enters" half
/// mirrors <see cref="PatchworkBannerFactory"/> / <see cref="AdaptiveAutomatonFactory"/>;
/// the "enters with N counters" half mirrors <see cref="EverflowingChaliceFactory"/>
/// (a dynamic-count <see cref="EntersWithCountersReplacement"/>); the
/// counter-scaling anthem is a dynamic-boost <see cref="LordStaticEffect"/>.
///
/// ## Composition
/// - Base shape (name, Artifact type, {5} cost) is materialised from the
///   embedded JSON definition (<c>banner-of-kinship.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - The as-enters type choice, the ETB fellowship-counter load, and the
///   counter-scaling anthem are layered on here because the JSON schema
///   expresses none of them — same posture as Patchwork Banner.
///
/// ## "As this artifact enters, choose a creature type." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Player, CardSubtype&gt;</c> selector on
/// the wired overload — the engine has no ChooseSubtype agent prompt yet, so
/// bots/tests supply the chosen type directly (same shape as Patchwork Banner /
/// Adaptive Automaton / Cavern of Souls). Stored per-card and exposed via
/// <see cref="GetChosenType"/>.
///
/// ## "This artifact enters with a fellowship counter on it for each creature
/// you control of the chosen type." (CR 614.1d / CR 122)
/// Modelled as a true "enters the battlefield with N counters" REPLACEMENT
/// (<see cref="EntersWithCountersReplacement"/>) with a dynamic count that, at
/// ETB-replacement time, tallies the creatures the Banner's controller controls
/// of the chosen type. The <see cref="Services.ZoneService"/> ETB pipeline
/// queues the fellowship counters onto the move intent so the artifact enters
/// WITH them already present — no after-the-fact trigger window. CR 614.12 — the
/// chosen type the count reads is the one selected by the same as-enters
/// replacement; the v1 eager-resolution captures it before this replacement
/// fires, so the tally observes the correct type.
///
/// ## "Creatures you control of the chosen type get +1/+1 for each fellowship
/// counter on this artifact." (CR 613.7c)
/// Wired via the dynamic-boost <see cref="LordStaticEffect"/> constructor with
/// <c>powerFn</c> / <c>toughnessFn</c> = the Banner's live fellowship-counter
/// count. Default controller filter (own creatures only) and
/// <c>includeSelf: true</c> faithfully models the printed "Creatures" (not
/// "Other") — moot in practice since an Artifact never matches a creature-type
/// filter. The closure is re-sampled every layer pass and counter mutations
/// bump the continuous-effects generation (CR 613), so the buff tracks the
/// count live; <see cref="ContinuousEffect.IsActive"/> gates on the Banner being
/// on the battlefield, so the buff lifts on LTB/flicker.
///
/// ## Deferred
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   has no ChooseSubtype prompt; the wired overload takes a selector closure.
///   Same posture as Patchwork Banner / Adaptive Automaton / Cavern of Souls.
/// </summary>
[CardName("Banner of Kinship")]
public static class BannerOfKinshipFactory
{
    public const string CardName = "Banner of Kinship";
    public const string Slug = "banner-of-kinship";

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // PatchworkBannerFactory. Keyed by the Artifact instance so a flicker
    // (which produces a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Artifact, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct Banner of Kinship with no live wiring and no as-enters choice
    /// resolved. Suitable for card-shape / dispatcher tests — the chosen-type
    /// slot is unset, <see cref="GetChosenType"/> returns null, and neither the
    /// ETB-counter replacement nor the anthem is registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, replacements: null, continuousEffects: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Banner of Kinship. When
    /// <paramref name="typeChooser"/> is supplied the as-enters creature-type
    /// choice is resolved eagerly. When <paramref name="replacements"/> is also
    /// supplied, the CR 614.1d "enters with a fellowship counter for each
    /// creature you control of the chosen type" replacement is registered. When
    /// <paramref name="continuousEffects"/> is supplied, the per-counter +1/+1
    /// chosen-type <see cref="LordStaticEffect"/> is registered against the
    /// layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Replacement bus to register the ETB
    /// fellowship-counter load against. May be null — the Banner then enters
    /// with no counters.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// per-counter +1/+1 chosen-type <see cref="LordStaticEffect"/> against. May
    /// be null — no live anthem.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype at
    /// as-enters time, called with Banner of Kinship's controller. May be null —
    /// no choice is made and neither the ETB load nor the anthem activates.</param>
    public static Artifact Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? continuousEffects,
        System.Func<Player, CardSubtype>? typeChooser)
    {
        System.ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact, {5}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        if (typeChooser == null) return card;

        // v1: eager-resolve the as-enters creature-type choice at factory time.
        // CR 614.12 — the choice is part of the as-enters replacement;
        // observationally equivalent in the current ETB pipeline (mirrors
        // Patchwork Banner / Adaptive Automaton / Cavern of Souls).
        var chosen = typeChooser(owner);
        _chosenType.AddOrUpdate(card, new ChoiceBox { Value = chosen });

        // CR 614.1d / CR 122 — "This artifact enters with a fellowship counter
        // on it for each creature you control of the chosen type." Dynamic-count
        // ETB replacement: tally is taken at replacement time (as the Banner
        // lands), reading the controller's battlefield creatures of the chosen
        // type. The ZoneService ETB pipeline queues the counters onto the move
        // intent so the artifact enters WITH them.
        replacements?.Register<ZoneMoveIntent>(
            new EntersWithCountersReplacement(
                card, CounterType.Fellowship,
                () => CountControlledCreaturesOfType(card, chosen)));

        // CR 613.7c — "Creatures you control of the chosen type get +1/+1 for
        // each fellowship counter on this artifact." Dynamic-boost lord: the
        // +P/+T magnitude = the Banner's live fellowship-counter count, sampled
        // each layer pass (counter mutations bump the effect generation, CR 613,
        // so the buff tracks the count live). Default controller filter (own
        // creatures only); includeSelf: true models the printed "Creatures"
        // (moot — an Artifact never matches a creature-type filter).
        continuousEffects?.Register(new LordStaticEffect(
            source: card,
            matchingSubtype: chosen,
            powerFn: () => card.Counters.Count(CounterType.Fellowship),
            toughnessFn: () => card.Counters.Count(CounterType.Fellowship),
            includeSelf: true));

        return card;
    }

    /// <summary>
    /// CR 614.1d count helper — the number of creatures the Banner's controller
    /// controls of the chosen creature type, read live from the battlefield.
    /// Falls back to the owner if the controller is somehow unset.
    /// </summary>
    private static int CountControlledCreaturesOfType(Artifact banner, CardSubtype type)
    {
        var ctrl = banner.Controller ?? banner.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => ReferenceEquals(c.Controller, ctrl) && c.HasSubtype(type));
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at construction
    /// time, else null. Per-card (not per-factory) — a flickered Banner is a new
    /// object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Artifact bannerOfKinship)
    {
        System.ArgumentNullException.ThrowIfNull(bannerOfKinship);
        return _chosenType.TryGetValue(bannerOfKinship, out var box) ? box.Value : null;
    }
}
