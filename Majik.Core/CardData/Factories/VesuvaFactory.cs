using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vesuva (Time Spiral, Land).
///
/// ## Card text (verified against Scryfall 2026-06-02)
/// Land.
///   "You may have this land enter tapped as a copy of any land on the
///    battlefield."
///
/// (Older printings carried "…except it's not legendary if that land is
/// legendary"; the current Oracle text drops the explicit clause because
/// CR 706.2 already strips Legendary on any copy. This factory applies the
/// strip for correctness either way.)
///
/// ## Implemented (v1)
/// - Plain <see cref="Land"/> named "Vesuva" (no printed subtypes / supertypes
///   / mana ability — all of those come from whatever land it copies).
/// - <b>Enters tapped as a copy of any land (CR 706.2 / 707.2)</b> via the
///   shared generalized <see cref="EntersAsCopyReplacement"/> with
///   <see cref="EntersAsCopyReplacement.SourceFilter.Land"/> and pool
///   <see cref="EntersAsCopyReplacement.CopyPool.AnyBattlefield"/>. This is the
///   first ETB-copy wired to a LAND source — the generalized path registers a
///   <see cref="CopyCharacteristicsEffect"/> (CR 707.2) that rewrites Vesuva's
///   subtypes to the copied land's. CR 305.6 then synthesizes the copied
///   basic-land mana abilities (e.g. copying a Forest produces {G}) via
///   <see cref="EffectiveManaAbilities"/>.
/// - <b>Enters tapped</b> via
///   <see cref="EntersAsCopyReplacement.Options.EntersTapped"/> →
///   <see cref="ZoneMoveIntent.EntersTapped"/>.
/// - <b>"not legendary if that land is legendary" (CR 706.2)</b> via
///   <see cref="EntersAsCopyReplacement.Options.StripLegendary"/> — a Layer-4
///   <see cref="RemoveSupertypeEffect"/> strips the copied Legendary supertype
///   so the copy never trips the legend rule against the legendary original.
///
/// ## Deferred (v1 gaps)
/// - "You may" choice — auto-yes when any land candidate exists (shared
///   <see cref="EntersAsCopyReplacement"/> posture; "decline" modelled by no
///   land on the battlefield → Vesuva enters as a plain non-mana land).
/// - Copy-source pool sees the controller's battlefield only (no cross-player
///   land candidates without a Game handle) — same boundary as the other
///   land-copy factories (Thespian's Stage).
/// </summary>
[CardName("Vesuva")]
public static class VesuvaFactory
{
    public const string CardName = "Vesuva";

    /// <summary>Shape-only overload dispatched by <see cref="NamedCardFactory"/>.</summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, effects: null);

    /// <summary>
    /// Construct Vesuva with optional replacement-bus + continuous-effects
    /// wiring. When both are supplied the generalized enters-tapped-as-a-copy
    /// replacement (CR 706.2) is registered.
    /// </summary>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        if (replacements != null && effects != null)
        {
            replacements.Register(new EntersAsCopyReplacement(
                land,
                EntersAsCopyReplacement.CopyPool.AnyBattlefield,
                effects,
                new EntersAsCopyReplacement.Options(
                    Filter: EntersAsCopyReplacement.SourceFilter.Land,
                    StripLegendary: true,
                    EntersTapped: true)));

            land.ActiveEffects = effects;
        }

        return land;
    }
}
