using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thundermaw Hellkite (Magic 2013, {3}{R}{R}).
/// Creature — Dragon 5/5. Oracle text (verified against Scryfall 2026-06-02):
///   "Flying
///    Haste (This creature can attack and {T} as soon as it comes under your
///    control.)
///    When this creature enters, it deals 1 damage to each creature with
///    flying your opponents control. Tap those creatures."
///
/// ## Shape source
/// The base shape (name, Creature — Dragon, {3}{R}{R}, 5/5) AND the ETB
/// triggered ability are materialised from the embedded JSON definition
/// (<c>thundermaw-hellkite.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB is the declarative
/// <see cref="DamageAndTapEachFlyerOpponentsControlEffectDef"/> verb — the
/// group-apply form of the single-target <c>deal_damage</c> + <c>tap_target</c>
/// verbs (CR 109.5 "your opponents", CR 702.9 Flying, CR 701.21a Tap). The
/// triggered ability auto-registers with the live
/// <see cref="Majik.Core.Abilities.TriggerManager"/> when the card crosses a
/// zone boundary (the central wiring every JSON-built trigger relies on —
/// <see cref="Majik.Core.Abilities.TriggerManager"/> binds any card carrying a
/// triggered ability the first time it sees it move), so no manual
/// <c>TriggerManager</c> threading is needed here.
///
/// ## Why a factory over a fileless JSON card
/// The JSON <c>AbilityDefinition</c> schema carries no evergreen keyword field,
/// so Flying + Haste must be layered on in code (same posture as
/// <see cref="DanithaCapashenParagonFactory"/> for keyword markers). The
/// factory loads the JSON shell — including the declarative ETB — then attaches
/// the two keyword markers.
///
/// ## Implemented (v1)
/// - 5/5 Creature — Dragon at {3}{R}{R}.
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — two
///   <see cref="KeywordAbility"/> markers (canonical strings "Flying" / "Haste"
///   matching the layer-system / combat keyword set).
/// - <b>ETB trigger (CR 603.6a)</b>: deals 1 damage to each creature with
///   flying an opponent controls (CR 109.5 / CR 702.9), then taps those same
///   creatures (CR 701.21a). The set is snapshotted before any mutation so the
///   "those creatures" tap clause acts on the SAME flyers the damage hit
///   (CR 700.3); a flyer that dies to the damage SBA is skipped for the tap.
/// </summary>
[CardName("Thundermaw Hellkite")]
public static class ThundermawHellkiteFactory
{
    public const string CardName = "Thundermaw Hellkite";
    public const string Slug = "thundermaw-hellkite";

    public const string Flying = "Flying";
    public const string Haste = "Haste";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Thundermaw Hellkite. The Flying + Haste keyword markers are
    /// always attached (static markers, no live runtime service needed); the ETB
    /// triggered ability comes from the JSON shell and auto-registers when the
    /// card enters the battlefield. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.10 — printed evergreen keyword markers.
        card.AddAbility(new KeywordAbility(Flying, card, owner));
        card.AddAbility(new KeywordAbility(Haste, card, owner));

        return card;
    }
}
