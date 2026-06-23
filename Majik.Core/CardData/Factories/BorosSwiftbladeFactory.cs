using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boros Swiftblade (Ravnica: City of Guilds,
/// {R}{W}). Creature — Human Soldier 1/2. Oracle text (verified against
/// Scryfall):
///   "Double strike"
///
/// The card's base shape (name, Creature type, Human + Soldier subtypes,
/// {R}{W}, 1/2) is materialised from the embedded JSON definition
/// (<c>boros-swiftblade.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed static
/// keyword rider (Double strike) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers, so
/// it lives in the factory (same posture as
/// <see cref="MirranCrusaderFactory"/>, whose Double strike rider uses the
/// identical wiring).
///
/// ## Implemented (v1)
/// - 1/2 Human Soldier (CR 205.3m) at {R}{W}. Owner / controller wired.
/// - <b>Double strike (CR 702.4)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDoubleStrike"/>
///   surfaces the combat property (first-strike + regular-damage steps).
///   Same marker shape as Mirran Crusader's Double strike.
///
/// ## Deferred (v1 gaps)
/// - None. Boros Swiftblade has no triggered or activated abilities — its
///   sole clause is a static evergreen keyword rider the engine already
///   models. The single-arg dispatcher path is fully wired (there is no
///   service-dependent behaviour to gate).
/// </summary>
[CardName("Boros Swiftblade")]
public static class BorosSwiftbladeFactory
{
    public const string CardName = "Boros Swiftblade";
    public const string Slug = "boros-swiftblade";

    /// <summary>
    /// Construct Boros Swiftblade. Fully wired — Double strike is a static
    /// marker with no service dependency, so this single overload is also
    /// the <see cref="NamedCardFactory"/> dispatch target.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Soldier subtypes, {R}{W}, 1/2). The JSON carries no
        // abilities — the keyword rider is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.4 — Double strike. KeywordAbility marker so
        // CombatAbilities.HasDoubleStrike surfaces the first-strike +
        // regular-damage combat behaviour.
        card.AddAbility(new KeywordAbility("Double strike", card, owner));

        return card;
    }
}
