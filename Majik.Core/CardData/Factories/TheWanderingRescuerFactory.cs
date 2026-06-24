using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Wandering Rescuer (Outlaws of Thunder
/// Junction, {3}{W}{W}).
///
/// Legendary Creature — Human Samurai Noble 3/4. Oracle text (verified
/// against the embedded modern-cards seed):
///   "Flash
///    Convoke (Your creatures can help cast this spell. Each creature you
///    tap while casting this spell pays for {1} or one mana of that
///    creature's color.)
///    Double strike
///    Other tapped creatures you control have hexproof."
///
/// The base shape (name, Legendary supertype, Creature, Human/Samurai/Noble
/// subtypes, {3}{W}{W}, 3/4, intrinsic Flash + Double strike keywords) is
/// materialised from the embedded JSON definition
/// (<c>the-wandering-rescuer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Convoke + the hexproof static
/// are layered on top here.
///
/// ## Implemented (v1)
///
/// - <b>3/4 Legendary Creature — Human Samurai Noble</b> at {3}{W}{W}.
/// - <b>Flash</b> + <b>Double strike</b> — intrinsic keywords carried by the
///   JSON def (KeywordAbility markers; Double strike is honoured by the
///   combat system, CR 702.4; Flash by the casting timing rules, CR 702.8).
/// - <b>Convoke keyword marker</b> (CR 702.51) — same descriptive inline
///   <see cref="KeywordAbility"/> shape as
///   <see cref="ConclaveTribunalFactory"/> / <see cref="ChordOfCallingFactory"/>.
///   The marker is purely descriptive; the per-tap cost-reduction primitive
///   is surfaced via <see cref="ConvokeAdditionalCost"/> (built on demand by
///   <see cref="BuildAdditionalCost"/>), threaded through the cast flow's
///   <c>additionalCosts</c> parameter — identical posture to Conclave
///   Tribunal.
/// - <b>"Other tapped creatures you control have hexproof"</b> static
///   (CR 702.11 hexproof) wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: null</c> (every creature, no type gate),
///   <c>power: 0, toughness: 0</c> (no P/T change — keyword grant only),
///   <c>grantedKeywords: ["Hexproof"]</c>, <c>includeSelf: false</c> (the
///   "Other" rider excludes the Rescuer itself), and <c>tappedOnly: true</c>
///   (the tapped-state membership gate, re-evaluated each Compute). The
///   default controller filter (no <c>allPlayers</c>, no <c>opponentsOnly</c>)
///   honours the "you control" scope (CR 109.5).
///
/// ## Deferred (v1 gaps)
///
/// - Same Convoke-flow gaps documented on <see cref="ChordOfCallingFactory"/>
///   / <see cref="ConclaveTribunalFactory"/>: the v1 cost-reduction path is
///   the per-tap reducer; agent-driven creature-tap prompts on the cast flow
///   are deferred.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; its <see cref="LordStaticEffect.IsActive"/> gate
///   short-circuits when the Rescuer isn't on the battlefield so the
///   hexproof grant lifts correctly. Same posture as Lord of the Unreal /
///   Sliver Legion.
/// </summary>
[CardName("The Wandering Rescuer")]
public static class TheWanderingRescuerFactory
{
    public const string CardName = "The Wandering Rescuer";
    public const string PrintedManaCost = "{3}{W}{W}";
    public const string Slug = "the-wandering-rescuer";

    /// <summary>
    /// Construct The Wandering Rescuer with no live continuous-effects
    /// service. The hexproof static is NOT registered (no layers service);
    /// the Convoke marker + intrinsic Flash / Double strike keywords are
    /// present. Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired The Wandering Rescuer. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting Hexproof to other TAPPED
    /// creatures the controller controls is registered against the layers
    /// service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// hexproof static against. May be null — no live grant.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human/Samurai/Noble, {3}{W}{W}, 3/4, Flash + Double
        // strike). Convoke + the hexproof static are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke keyword marker. Marker is descriptive; the
        // cost-reduction primitive lives on the ConvokeAdditionalCost
        // returned by BuildAdditionalCost. Same inline attach pattern as
        // Conclave Tribunal.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.1f (granted keyword) — "Other tapped creatures you
            // control have hexproof." No subtype gate (matchingSubtype: null),
            // no P/T change (0/0), Hexproof grant only, includeSelf: false
            // ("Other"), tappedOnly: true (the tapped membership gate). Default
            // controller filter honours the "you control" scope (CR 109.5).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: null,
                matchingKeyword: null,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "Hexproof" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false,
                tokensOnly: false,
                tappedOnly: true));
        }

        return card;
    }

    /// <summary>
    /// CR 702.51 — build the Convoke additional cost for this Wandering
    /// Rescuer spell with the caller-selected untapped creatures. Same shape
    /// as <see cref="ConclaveTribunalFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);
}
