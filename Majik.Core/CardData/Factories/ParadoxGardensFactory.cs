using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Paradox Gardens (Edge of Eternities surveil land).
///
/// G/U surveil land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {G} or {U}.
///    {2}{G}{U}, {T}: Surveil 1. (Look at the top card of your library. You
///    may put it into your graveyard.)"
///
/// Scryfall type line is plain <c>Land</c> — no basic-land subtypes (unlike
/// the Karlov Manor "surveil land" dual cycle, which carry Forest/Island
/// etc.). Closest analogue is <see cref="CastleVantressFactory"/>: a land
/// whose surveil is an ACTIVATED ability ({2}{G}{U}, {T}: Surveil 1) rather
/// than an ETB trigger (Hedge Maze).
///
/// ## Implemented (v1) — all from the JSON definition
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {G}
///   and {U} ("{T}: Add {G} or {U}"). Mana abilities don't use the stack.
/// - <b>{2}{G}{U}, {T}: Surveil 1 (CR 701.43 — surveil keyword action)</b> —
///   an <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose cost stack
///   is a ManaCostCost({2}{G}{U}) + a tap-self additional cost, resolving the
///   standard <c>surveil_self</c> effect. When an
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
///   controller decides graveyard-vs-top; otherwise the pre-agent default
///   sends the peeked card to the graveyard.
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text,
/// not by this named factory — same posture as the surveil-land cycle and
/// the Refuge / Temple cycles. The shape-only factory path therefore enters
/// untapped (no <see cref="Majik.Core.Abilities.ReplacementBus"/> available
/// here to own the replacement).
/// </summary>
[CardName("Paradox Gardens")]
public static class ParadoxGardensFactory
{
    public const string CardName = "Paradox Gardens";
    public const string Slug = "paradox-gardens";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Paradox Gardens (plain Land) from its embedded JSON definition:
    /// dual {G}/{U} mana plus the {2}{G}{U}, {T}: Surveil 1 activated ability.
    /// Enters-tapped (CR 614.1c) is owned by the binder layer on the
    /// production load path, not wired here.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
