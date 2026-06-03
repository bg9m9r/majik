using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Baneslayer Angel (Magic 2010, {3}{W}{W}).
/// Creature — Angel 5/5. Oracle text (verified against Scryfall):
///   "Flying, first strike, lifelink, protection from Demons and from Dragons"
///
/// The base shape (name, Creature, Angel subtype, {3}{W}{W}, 5/5) is
/// materialised from the embedded JSON definition (<c>baneslayer-angel.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The four printed static keyword
/// riders (Flying, First strike, Lifelink, and the two subtype protections)
/// are layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers or protection qualities, so they live in the
/// factory (same posture as <see cref="MirranCrusaderFactory"/> and the other
/// JSON-backed creatures whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Flying / First strike / Lifelink (CR 702.9 / 702.7 / 702.15)</b> —
///   <see cref="KeywordAbility"/> markers; the
///   <see cref="Majik.Core.Combat.CombatAbilities"/> helpers surface the
///   combat properties.
/// - <b>Protection from Demons and from Dragons (CR 702.16 / 205.3)</b> — two
///   <see cref="ProtectionAbility"/> markers naming the creature subtypes
///   "demons" / "dragons". The protection-from-subtype seam
///   (<see cref="Majik.Core.Rules.Protection.HasProtectionFromSubtype"/>) reads
///   the OPPOSING permanent's <em>effective</em> subtypes (Layer-4), so a
///   creature animated into a Demon/Dragon is correctly handled. DEBT-A: the
///   subtype-quality markers feed combat block legality
///   (<see cref="Majik.Core.Combat.CombatValidator"/>), combat-damage
///   prevention (<see cref="Majik.Core.Combat.CombatFlow"/>), and targeting
///   legality (<see cref="Majik.Core.Rules.TargetLegality"/>).
///
/// ## Deferred (v1 gaps)
/// - None for Baneslayer Angel itself — every clause is a static evergreen
///   rider the engine models. The single-arg dispatcher path is fully wired
///   (no service-dependent behaviour).
/// </summary>
[CardName("Baneslayer Angel")]
public static class BaneslayerAngelFactory
{
    public const string CardName = "Baneslayer Angel";
    public const string Slug = "baneslayer-angel";

    /// <summary>CR 702.16 quality buckets for the two subtype protections.
    /// Stored as the printed plural; the protection helper depluralises and
    /// matches against the source's <see cref="Cards.Types.CardSubtype"/>
    /// names.</summary>
    public const string DemonProtectionQuality = "demons";
    public const string DragonProtectionQuality = "dragons";

    /// <summary>
    /// Construct Baneslayer Angel. Fully wired — Flying, First strike,
    /// Lifelink, and the two subtype protections are all static markers with
    /// no service dependency, so this single overload is also the
    /// <see cref="NamedCardFactory"/> dispatch target.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 / 702.7 / 702.15 — evergreen combat keyword riders.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("First strike", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // CR 702.16 / 205.3 — protection from Demons and from Dragons. Qualities
        // stored normalised; HasProtectionFromSubtype interprets them against
        // the opposing permanent's effective subtypes.
        card.AddAbility(new ProtectionAbility(DemonProtectionQuality));
        card.AddAbility(new ProtectionAbility(DragonProtectionQuality));

        return card;
    }
}
