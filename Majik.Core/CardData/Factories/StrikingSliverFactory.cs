using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Striking Sliver (Magic 2014 / many reprints,
/// {R}). Creature — Sliver 1/1. Oracle text (verified against Scryfall):
///   "Sliver creatures you control have first strike."
///
/// The card's base shape (name, Creature, Sliver subtype, {R}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>striking-sliver.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed Sliver
/// first-strike anthem is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express lord statics, so the
/// anthem lives in the factory (same posture as
/// <see cref="BladeSplicerFactory"/>'s Golem first-strike anthem).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Sliver at {R}.
/// - <b>Sliver first-strike anthem (CR 613.1f)</b>: "Sliver creatures you
///   control have first strike." Wired via <see cref="LordStaticEffect"/>
///   with <c>matchingSubtype: Sliver</c>, <c>power: 0, toughness: 0</c>
///   (keyword-only anthem), <c>grantedKeywords: ["First strike"]</c>,
///   <c>includeSelf: true</c>, <c>opponentsOnly: false</c>,
///   <c>allPlayers: false</c>. The keyword string is "First strike" — the
///   exact token
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> reads.
///   <c>includeSelf: true</c> is correct: the printed text has NO "Other"
///   qualifier (contrast Goblin Chieftain / Elvish Archdruid), and Striking
///   Sliver is itself a Sliver, so it grants first strike to itself too —
///   matching every Sliver lord's self-inclusive wording. Controller-scoped
///   (<c>allPlayers: false</c>): only Slivers the controller controls gain
///   first strike. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied. Same shape as
///   <see cref="BladeSplicerFactory"/>'s keyword-granting lord static.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="LordStaticEffect.IsActive"/> short-circuits when
///   Striking Sliver isn't on the battlefield so the first-strike grant
///   lifts correctly, but a future Prune pass could drop the entry. Same
///   shape as <see cref="BladeSplicerFactory"/> / <see cref="GoblinChieftainFactory"/>.
/// </summary>
[CardName("Striking Sliver")]
public static class StrikingSliverFactory
{
    public const string CardName = "Striking Sliver";
    public const string Slug = "striking-sliver";

    /// <summary>
    /// Construct Striking Sliver with no live wiring. The Sliver anthem is
    /// NOT registered (no continuous-effects service). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Striking Sliver. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting first strike to Slivers the
    /// controller controls (Striking Sliver itself included) is registered
    /// against the layers service. May be null — no live grant.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the Sliver
    /// first-strike anthem against. May be null — no live grant.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Sliver subtype, {R}, 1/1). The JSON carries no abilities — the
        // Sliver first-strike anthem is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Sliver first-strike anthem — CR 613.1f (granted keyword).
        //   "Sliver creatures you control have first strike."
        // matchingSubtype: Sliver; power/toughness 0 (keyword-only anthem).
        // includeSelf: true honours the printed text — no "Other"
        // qualifier, and Striking Sliver is itself a Sliver, so it gains
        // first strike too. Controller-scoped (allPlayers: false).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Sliver,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "First strike" },
                includeSelf: true,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}
