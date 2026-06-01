using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dryad Arbor (Future Sight) — the only printed
/// Land Creature with no mana cost.
///
/// Type line: <c>Land Creature — Forest Dryad</c>.
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "(This land isn't a spell, it's affected by summoning sickness, and it
///    has \"{T}: Add {G}.\")"
///
/// The oracle text is purely reminder text spelling out the consequences of
/// the type line plus the printed mana ability — there is no separate effect
/// to model:
/// <list type="bullet">
///   <item><b>"isn't a spell"</b> — a permanent that is both a Land and a
///   Creature is still a land, and lands aren't cast (CR 305.9 — a permanent
///   with the land type is never put on the stack as a spell). Nothing extra
///   to wire: Dryad Arbor is played as a land via the normal land-drop path.</item>
///   <item><b>"affected by summoning sickness"</b> — it has the Creature type,
///   so CR 302.6 applies automatically; the engine's summoning-sickness check
///   keys off the Creature type, not off this card.</item>
///   <item><b>{T}: Add {G}</b> — a vanilla intrinsic <see cref="ManaAbility"/>
///   (CR 605.1), materialised straight from the JSON definition.</item>
/// </list>
///
/// ## Why a thin JSON-wrapper factory is sufficient
/// Every characteristic of Dryad Arbor is already expressible by the
/// declarative <c>dryad-arbor.json</c> definition + the shared
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> /
/// <see cref="CardDefinitionFactory.Build"/> materializer:
/// <list type="bullet">
///   <item>Primary type <c>Creature</c> (so the runtime instance is a
///   <see cref="Creature"/> and is therefore summoning-sick — CR 302.6),
///   with the additional <c>Land</c> type stacked on.</item>
///   <item>Subtypes <c>Forest</c> + <c>Dryad</c> (CR 205.3).</item>
///   <item>1/1 base power/toughness (CR 208).</item>
///   <item>Green colour indicator (CR 202.2c) — it is green despite the
///   empty mana cost. Threaded through <c>colors: ["G"]</c> in the JSON, which
///   <see cref="CardDefinition.ToCardDef"/> maps onto the card's colour
///   indicator so <see cref="CardColors.GetColors"/> reports green.</item>
///   <item>The {T}: Add {G} mana ability.</item>
/// </list>
///
/// No new engine mechanic is required, so unlike the manland factories this
/// one performs no post-build layering — it is a pure pass-through.
/// </summary>
[CardName("Dryad Arbor")]
public static class DryadArborFactory
{
    public const string CardName = "Dryad Arbor";
    public const string Slug = "dryad-arbor";

    /// <summary>
    /// Construct Dryad Arbor for the supplied owner from the embedded JSON
    /// definition. Returns a <see cref="Creature"/> (Dryad Arbor's first
    /// listed type is Creature, so the runtime instance is summoning-sick per
    /// CR 302.6), which also carries the Land type. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
