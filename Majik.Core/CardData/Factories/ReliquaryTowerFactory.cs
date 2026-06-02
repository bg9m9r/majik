using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reliquary Tower (Conflux and many reprints).
///
/// Land. Oracle text (verified against Scryfall):
///   "You have no maximum hand size.
///    {T}: Add {C}."
///
/// ## Why it gets its own factory
/// Same skeleton as <see cref="RoadsideReliquaryFactory"/> /
/// <see cref="WastesFactory"/>: a colourless-mana land whose printed
/// "{T}: Add {C}" ability ships in the embedded JSON
/// (<c>reliquary-tower.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. No new engine mechanic is required for
/// the mana half, and the static "no maximum hand size" rider is a documented
/// no-op against the current engine (see Deferred, below).
///
/// ## Implemented (v1)
/// - Card identity (Land, no printed supertypes / subtypes) from the JSON
///   definition, plus owner / controller wiring.
/// - <b>{T}: Add {C}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1), declared in the JSON. {C} folds to one colourless mana via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>. This is the card's
///   fully-observable behaviour.
///
/// ## Deferred (v1 gaps)
/// - <b>"You have no maximum hand size."</b> (CR 402.2 — the 7-card maximum
///   hand size; CR 514.1 / cleanup-step discard). The engine does not yet
///   enforce a maximum hand size at all: no player is ever forced to discard
///   down to seven during the cleanup step, so there is no maximum hand size
///   for this static ability to remove. The rider is therefore a no-op against
///   the current engine — the same documented posture as
///   <see cref="SeaGateRestorationFactory"/>'s "no maximum hand size for the
///   rest of the game" clause. When a real maximum-hand-size mechanic lands,
///   this clause should set a per-player "no maximum hand size" flag consulted
///   by the cleanup step. The card's primary effect (the mana ability) is
///   complete; this is the only gap.
/// </summary>
[CardName("Reliquary Tower")]
public static class ReliquaryTowerFactory
{
    public const string CardName = "Reliquary Tower";
    public const string Slug = "reliquary-tower";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Reliquary Tower owned and controlled by
    /// <paramref name="owner"/>. The {T}: Add {C} mana ability comes from the
    /// embedded JSON; the "You have no maximum hand size." static rider is a
    /// documented no-op against the current engine (see class doc).
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        return land;
    }
}
