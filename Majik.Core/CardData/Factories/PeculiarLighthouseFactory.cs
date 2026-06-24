using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Peculiar Lighthouse (Duskmourn: House of Horror) —
/// the U/R member of the "13-or-less-life" pain-check tapland cycle. Oracle
/// (verified against Scryfall):
/// <code>
/// This land enters tapped unless a player has 13 or less life.
/// {T}: Add {U} or {R}.
/// </code>
///
/// <para>
/// The Land shell — plain nonbasic <see cref="Land"/> (no supertype, no
/// printed subtype) plus the two mana abilities {U}/{R} (CR 605.1a — mana
/// abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/peculiar-lighthouse.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same JSON-driven posture as
/// <see cref="BloomingMarshFactory"/>.
/// </para>
///
/// <para>
/// "Enters tapped unless a player has 13 or less life" (CR 614.1c) is the
/// pain-check ETB condition. Note the condition reads <i>a player</i> — ANY
/// player in the game (the controller OR any opponent), not just "you" — so
/// the predicate consults the whole player roster, not only the controller's
/// own life total. This factory mirrors that predicate on the optional
/// <see cref="ReplacementBus"/> overload so the behaviour is exercisable in
/// isolation: the land enters untapped iff some player in the game has a life
/// total at or below the <see cref="LifeThreshold"/>.
/// </para>
///
/// <para>
/// The roster is supplied by an optional <c>allPlayersProvider</c> closure
/// (same whole-board-roster injection posture as
/// <see cref="AgathasSoulCauldronFactory"/>). When omitted, the predicate
/// falls back to inspecting the controller's own life total alone — the
/// controller is always a "player", so a low-life controller still flips the
/// land untapped even on the roster-less path.
/// </para>
///
/// <para>
/// <b>Production load path / prod wiring gap.</b>
/// <see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/> only
/// recognizes the "N or [more|fewer] other lands" and subtype-count wordings;
/// it does NOT yet claim the "a player has N or less life" form, so on the
/// prod load path this land currently enters untapped (no replacement wired) —
/// the same single-arg-dispatcher shape-only posture as every other
/// ETB-replacement factory whose oracle wording the binder hasn't learned.
/// The full predicate is wired here on the <see cref="ReplacementBus"/>
/// overload and covered by tests. (A future binder regex for the life-check
/// wording is the prod-path follow-up; tracked the same way the cottage /
/// fast-land wordings were added to the binder incrementally.)
/// </para>
/// </summary>
[CardName("Peculiar Lighthouse")]
public static class PeculiarLighthouseFactory
{
    public const string CardName = "Peculiar Lighthouse";

    /// <summary>
    /// CR 614.1c threshold — "a player has 13 or less life". The land enters
    /// untapped iff some player's life total is at or below this value.
    /// </summary>
    public const int LifeThreshold = 13;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("peculiar-lighthouse");

    /// <summary>
    /// Construct Peculiar Lighthouse owned and controlled by
    /// <paramref name="owner"/>. Shape-only path — no
    /// <see cref="ReplacementBus"/>, so the ETB-tapped predicate is not
    /// wired here.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, allPlayersProvider: null);

    /// <summary>
    /// Construct Peculiar Lighthouse with an optional
    /// <see cref="ReplacementBus"/> for full ETB-tapped wiring (CR 614.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless a
    /// player has 13 or less life" replacement is registered.</param>
    /// <param name="allPlayersProvider">Whole-board roster so the "a player"
    /// life check can consult every player, not just the controller. When
    /// null the predicate inspects the controller's own life total alone.</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        Func<IEnumerable<Player>?>? allPlayersProvider = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + dual {U}/{R} mana come from the JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless a player has 13 or less life (CR 614.1c).
        // Predicate returns true ⇒ enters untapped, false ⇒ enters tapped.
        //
        // "a player" = ANY player in the game (CR 102.1) — the controller or
        // any opponent. The roster comes from allPlayersProvider when given;
        // the controller is always folded in (it is itself "a player"), so a
        // low-life controller flips the land untapped even with no roster.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    AnyPlayerAtOrBelowThreshold(controller, allPlayersProvider)));
        }

        return land;
    }

    /// <summary>
    /// True iff some player in the game has life ≤ <see cref="LifeThreshold"/>.
    /// The controller is always considered; the supplied roster (if any) adds
    /// the remaining players. Duplicate inclusion of the controller is
    /// harmless — this is an existence check.
    /// </summary>
    private static bool AnyPlayerAtOrBelowThreshold(
        Player controller,
        Func<IEnumerable<Player>?>? allPlayersProvider)
    {
        if (controller.LifeTotal <= LifeThreshold)
            return true;

        var roster = allPlayersProvider?.Invoke();
        if (roster is null)
            return false;

        return roster.Any(p => p is not null && p.LifeTotal <= LifeThreshold);
    }
}
