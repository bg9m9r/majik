using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Adventurer's Inn. Oracle text (verified against
/// Scryfall):
///   "When this land enters, you gain 2 life.
///    {T}: Add {C}."
///
/// <para>
/// A member of the "Town" land cycle (printed land subtype <c>Town</c>,
/// CR 205.3m — no rules reference the Town subtype yet; it is a purely printed
/// characteristic). Same gain-life-on-ETB shape as the Khans gain-land cycle
/// (<see cref="BloodfellCavesFactory"/>) but with two key deltas:
///   - it enters <b>untapped</b> — there is NO "This land enters tapped."
///     clause, so no <see cref="EntersTappedReplacement"/> / CR 614.1c wiring;
///   - it produces only colorless mana — a single <c>{T}: Add {C}</c> mana
///     ability (CR 605.1 — mana abilities don't use the stack) instead of a
///     two-colour pair;
///   - the ETB gains <b>2</b> life rather than 1 (CR 119.3).
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the <c>Town</c> subtype, the
/// {T}: Add {C} mana ability, and the "When this land enters, you gain 2 life"
/// triggered ability — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/adventurers-inn.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="GohnTownOfRuinFactory"/>. There is no enters-tapped clause and no
/// bespoke C# behaviour, so the single-arg shape path is the whole factory.
/// </para>
/// </summary>
[CardName("Adventurer's Inn")]
public static class AdventurersInnFactory
{
    public const string Slug = "adventurers-inn";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Adventurer's Inn owned and controlled by
    /// <paramref name="owner"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Untapped land — no EntersTappedReplacement / CR 614.1c wiring. The
        // entire surface (Town subtype, {T}: Add {C} mana ability, ETB
        // gain-2-life trigger) comes from the embedded JSON definition.
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
