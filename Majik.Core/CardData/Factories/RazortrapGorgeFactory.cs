using System.Linq;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Razortrap Gorge (Tarkir: Dragonstorm). Oracle text
/// (verified against Scryfall):
///   "This land enters tapped unless a player has 13 or less life.
///    {T}: Add {B} or {R}."
///
/// <para>
/// The Land shell — both mana abilities {B}/{R} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/razortrap-gorge.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ForebodingRuinsFactory"/>. Razortrap Gorge is a nonbasic,
/// non-typed land (no printed land subtype), so the def carries only the two
/// mana abilities.
/// </para>
///
/// <para>
/// "This land enters tapped unless a player has 13 or less life." (CR 614.1c —
/// a replacement effect modifying how the permanent enters) is wired as a
/// <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. Unlike the "buddy land" check
/// (which inspects the controller's board), the oracle here says "a player"
/// (CR 102) — so the condition is satisfied when ANY player in the game,
/// controller OR opponent, is at 13 or less life. The live player set is read
/// from <see cref="GamePlayersRegistry.AllPlayers"/> (the ambient per-game
/// roster); the entering land's controller is always included as a fallback so
/// the predicate is still correct on a controller-low-life ETB even when no
/// game-scoped roster is installed (shape paths). Predicate returns true =>
/// untapped, false => tapped.
/// </para>
///
/// <para>
/// Single-arg dispatcher path constructs without a
/// <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
/// (shape-only posture matching every other ETB-replacement factory's
/// single-arg path); the mana abilities are still attached. The full overload
/// wires the predicate when the bus is supplied.
/// </para>
/// </summary>
[CardName("Razortrap Gorge")]
public static class RazortrapGorgeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("razortrap-gorge");

    /// <summary>The "13 or less life" threshold from the oracle text.</summary>
    private const int LifeThreshold = 13;

    /// <summary>Construct Razortrap Gorge owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Razortrap Gorge with an optional
    /// <see cref="ReplacementBus"/> for full "enters tapped unless a player has
    /// 13 or less life" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped unless a player has 13 or less life."
        // (CR 614.1c). "a player" (CR 102) ⇒ ANY player in the game, not
        // just the controller. Predicate returns true => untapped (some
        // player is at/under 13), false => tapped.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    AnyPlayerHasThirteenOrLess(controller)));
        }

        return land;
    }

    private static bool AnyPlayerHasThirteenOrLess(Player controller)
    {
        // The controller is always a "player" for this check; include it as a
        // fallback so the predicate is correct even when no game-scoped roster
        // is installed (shape-only paths). The ambient registry supplies the
        // rest of the table (opponents) in a live game.
        if (controller.LifeTotal <= LifeThreshold) return true;

        return GamePlayersRegistry.AllPlayers
            .Any(p => p != null && p.LifeTotal <= LifeThreshold);
    }
}
