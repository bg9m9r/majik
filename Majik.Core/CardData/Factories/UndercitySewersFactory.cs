using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Undercity Sewers (Murders at Karlov Manor "surveil
/// land" dual cycle — the U/B member).
///
/// U/B surveil tapland. Oracle text (verified against Scryfall):
///   "({T}: Add {U} or {B}.)
///    This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)"
///
/// Type line is <c>Land — Island Swamp</c>. The whole card shape is fully
/// expressible in the data schema, so this factory is a thin wrapper: it loads
/// <c>Majik.Core/CardData/Cards/undercity-sewers.json</c> and builds it through
/// <see cref="CardDefinitionFactory"/>. Identical shape to Hedge Maze (G/U) and
/// the rest of the surveil cycle (Shadowy Backstreet / Underground Mortuary).
///
/// ## Implemented (v1) — all from the JSON definition
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {U}
///   and {B} ("{T}: Add {U} or {B}"). Mana abilities don't use the stack.
/// - <b>ETB surveil 1 (CR 603.6a + CR 701.43 — surveil keyword action)</b> —
///   a self-ETB <see cref="Majik.Core.Abilities.TriggeredAbility"/> whose
///   <c>surveil_self</c> effect peeks the top card and, via the controller's
///   registered agent, decides graveyard-vs-top. With no agent it defaults to
///   all-peeked-to-graveyard (same posture as the rest of the surveil cycle).
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text,
/// not by this named factory — same posture as the rest of the surveil lands.
/// The shape-only factory path therefore enters untapped (no
/// <see cref="Majik.Core.Abilities.ReplacementBus"/> available here to own the
/// replacement).
/// </summary>
[CardName("Undercity Sewers")]
public static class UndercitySewersFactory
{
    public const string CardName = "Undercity Sewers";
    public const string Slug = "undercity-sewers";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Undercity Sewers (Land — Island Swamp) from its embedded JSON
    /// definition: dual {U}/{B} mana plus the ETB surveil-1 trigger.
    /// Enters-tapped (CR 614.1c) is owned by the binder layer on the
    /// production load path, not wired here.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
