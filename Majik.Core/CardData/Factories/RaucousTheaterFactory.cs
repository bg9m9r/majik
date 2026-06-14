using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Raucous Theater (Murders at Karlov Manor "surveil
/// land" dual cycle — the B/R member, the last cycle-mate without a factory).
///
/// B/R surveil tapland. Oracle text (verified against Scryfall):
///   "({T}: Add {B} or {R}.)
///    This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)"
///
/// Type line is <c>Land — Swamp Mountain</c>. The whole card shape is fully
/// expressible in the data schema, so this factory is a thin wrapper: it loads
/// <c>Majik.Core/CardData/Cards/raucous-theater.json</c> and builds it through
/// <see cref="CardDefinitionFactory"/>. Identical shape to Undercity Sewers
/// (U/B) and the rest of the surveil cycle (Hedge Maze / Shadowy Backstreet /
/// Underground Mortuary).
///
/// ## Implemented (v1) — all from the JSON definition
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {B}
///   and {R} ("{T}: Add {B} or {R}"). Mana abilities don't use the stack.
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
[CardName("Raucous Theater")]
public static class RaucousTheaterFactory
{
    public const string CardName = "Raucous Theater";
    public const string Slug = "raucous-theater";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Raucous Theater (Land — Swamp Mountain) from its embedded JSON
    /// definition: dual {B}/{R} mana plus the ETB surveil-1 trigger.
    /// Enters-tapped (CR 614.1c) is owned by the binder layer on the
    /// production load path, not wired here.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
