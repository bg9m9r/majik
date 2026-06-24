using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of Surveillance (Duskmourn: House of Horror,
/// {1}{W}).
///
/// Enchantment Creature — Nightmare 2/2. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Vigilance
///    Whenever this creature attacks, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/fear-of-surveillance.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="SinisterStarfishFactory"/> (the surveil body) crossed with the
/// declarative attack trigger. Both clauses are fully declarative JSON:
///
/// - <b>Vigilance</b> — the <c>keywords</c> array carries the evergreen keyword
///   (CR 702.20); <see cref="CardDefinition"/> calls <c>WithKeyword</c> per
///   entry, so no bespoke code is needed.
/// - <b>Whenever this creature attacks, surveil 1</b> — a <c>triggered</c>
///   ability whose <c>attacks_self</c> trigger (CR 508.1f — a per-attacker self
///   trigger over <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>)
///   carries a <c>surveil_self</c> effect (CR 701.42). At resolution the shared
///   <see cref="CardDefRuntime"/> surveil builder consults the controller's agent
///   (look at the top card, may put it into the graveyard), falling back to the
///   all-to-graveyard default when no agent is registered — identical to
///   <see cref="SinisterStarfishFactory"/>'s surveil body.
/// </summary>
[CardName("Fear of Surveillance")]
public static class FearOfSurveillanceFactory
{
    public const string CardName = "Fear of Surveillance";
    public const string Slug = "fear-of-surveillance";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Fear of Surveillance owned and controlled by
    /// <paramref name="owner"/>. Vigilance + the attacks-trigger surveil ability
    /// are materialised from the embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
