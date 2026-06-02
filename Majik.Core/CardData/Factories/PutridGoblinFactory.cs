using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Putrid Goblin (Shadows over Innistrad, {1}{B}).
///
/// Creature — Zombie Goblin 2/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// Putrid Goblin is the purest possible Persist body — no ETB, no activated
/// ability. The entire card is the Persist keyword on a vanilla 2/2, so it is
/// the simplest member of the Persist family (Kitchen Finks / Murderous Redcap /
/// Glen Elendra Archmage) minus their extra triggers.
///
/// The base shape (name / Creature / Zombie+Goblin / {1}{B} / 2/2) is
/// materialised from the embedded JSON definition (<c>putrid-goblin.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="PersistentPetitionersFactory"/>). Persist is layered on here via
/// the shared primitive — the JSON ability schema does not express the Persist
/// keyword's death-trigger.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Zombie Goblin at printed cost {1}{B}; owner / controller
///   wired. Both <see cref="CardSubtype.Zombie"/> and
///   <see cref="CardSubtype.Goblin"/> are stamped so Zombie-tribal and
///   Goblin-tribal anchors see it.
/// - <b>Persist (CR 702.79)</b>: wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive — keyword marker +
///   the Battlefield → Graveyard death trigger with the "no -1/-1 counter"
///   interveningIf gate (CR 603.4). On a counter-free death the Goblin returns
///   to the battlefield with one -1/-1 counter (CR 702.79b); a second death
///   (now counter-bearing) stays in the graveyard.
/// </summary>
[CardName("Putrid Goblin")]
public static class PutridGoblinFactory
{
    public const string CardName = "Putrid Goblin";
    public const string Slug = "putrid-goblin";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Putrid Goblin owned and controlled by <paramref name="owner"/>.
    /// The base shape is materialised from the embedded JSON definition; the
    /// Persist death trigger + keyword marker are layered on here. Call
    /// <see cref="Majik.Core.Services.TriggerManager.BindCard"/> on the returned
    /// creature to register the Persist trigger with the live trigger manager.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // Persist (CR 702.79) — keyword marker + death trigger, all from the
        // shared primitive.
        PersistFactory.Build(card);

        return card;
    }
}
