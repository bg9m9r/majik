using Majik.Bot.Search;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;

namespace Majik.Bot.Strategies;

/// <summary>
/// Shared helpers for authoring <see cref="IDeckStrategy"/> classes — zone
/// queries and building valid live <see cref="PriorityAction"/>s
/// (cast/activate) for the win-line.
///
/// <para>
/// <see cref="BuildCast"/> and <see cref="BuildActivate"/> mirror the
/// construction logic of <see cref="Majik.Bot.Search.LegalActionEnumerator"/>
/// exactly: same affordability gate (<c>ApproxCmc ≤ UntappedManaSources</c>),
/// same instant-speed / sorcery-window check, same record constructors. A null
/// return means "line not currently executable" so
/// <see cref="IDeckStrategy.TryGetNextWinningAction"/> can safely return null
/// without further checks.
/// </para>
/// </summary>
internal static class DeckStrategyHelpers
{
    // ── Zone accessors ─────────────────────────────────────────────────────────

    /// <summary>All cards in the player's hand.</summary>
    public static IReadOnlyList<ICard> Hand(Player p)
        => p.Zones.Hand.GetCards().ToList();

    /// <summary>All cards in the player's graveyard.</summary>
    public static IReadOnlyList<ICard> Graveyard(Player p)
        => p.Zones.Graveyard.GetCards().ToList();

    /// <summary>All permanents the player controls (their battlefield).</summary>
    public static IReadOnlyList<ICard> Board(Player p)
        => p.Zones.Battlefield.GetCards().ToList();

    /// <summary>True when a card with the given name is in the player's hand.</summary>
    public static bool HasInHand(Player p, string name)
        => p.Zones.Hand.GetCards().Any(c => c.Name == name);

    /// <summary>True when a card with the given name is in the player's graveyard.</summary>
    public static bool HasInGraveyard(Player p, string name)
        => p.Zones.Graveyard.GetCards().Any(c => c.Name == name);

    /// <summary>True when a permanent with the given name is on the player's battlefield.</summary>
    public static bool HasOnBoard(Player p, string name)
        => p.Zones.Battlefield.GetCards().Any(c => c.Name == name);

    /// <summary>First card with the given name in hand, or null.</summary>
    public static ICard? FindInHand(Player p, string name)
        => p.Zones.Hand.GetCards().FirstOrDefault(c => c.Name == name);

    // ── Action builders ────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="PriorityAction.CastSpell"/> for the named card in
    /// hand, mirroring <see cref="LegalActionEnumerator.ForPriority"/> exactly.
    ///
    /// <para>Gates applied (same as the enumerator):</para>
    /// <list type="bullet">
    ///   <item>Card must be in <paramref name="self"/>'s hand.</item>
    ///   <item>Affordability: <c>ApproxCmc ≤ UntappedManaSources</c>.</item>
    ///   <item>Timing: instant-speed cards may be cast any time; sorcery-speed
    ///     cards require active player + main phase + empty stack
    ///     (CR 116.2a).</item>
    /// </list>
    ///
    /// <para>
    /// The optional <paramref name="target"/> is folded into a single-element
    /// targets list (or empty when null), matching the
    /// <see cref="PriorityAction.CastSpell"/> constructor the enumerator uses
    /// (<c>Array.Empty&lt;object&gt;()</c> for no target). Hard cases (X costs,
    /// modes, alternative costs, additional costs) are not modeled here — the
    /// caller is responsible for building those via a richer constructor if
    /// needed. Returns null instead of a wrong action.
    /// </para>
    ///
    /// Returns null when the card is absent from hand or currently not castable.
    /// </summary>
    public static PriorityAction? BuildCast(
        GameContext ctx, Player self, string cardName, object? target = null)
    {
        var card = self.Zones.Hand.GetCards().FirstOrDefault(c => c.Name == cardName);
        if (card is null) return null;

        // Affordability gate — mirrors LegalActionEnumerator.ForPriority (line 74–76).
        var manaAvailable = LegalActionEnumerator.UntappedManaSources(self);
        if (LegalActionEnumerator.ApproxCmc(card) > manaAvailable) return null;

        // Timing gate — mirrors LegalActionEnumerator.ForPriority (lines 62–76).
        var instantSpeed = LegalActionEnumerator.IsInstantSpeed(card);
        if (!instantSpeed)
        {
            // Sorcery-speed: require active player + main phase + empty stack
            // (CR 116.2a) — same sorceryWindow predicate as the enumerator.
            var sorceryWindow = ctx.ActivePlayer == self
                && ctx.CurrentPhase is { } phase && phase.IsMain()
                && ctx.Stack.Count == 0;
            if (!sorceryWindow) return null;
        }

        // Construct CastSpell identically to LegalActionEnumerator (line 75):
        //   new PriorityAction.CastSpell(card, Array.Empty<object>())
        // plus optional explicit target folded into the list.
        IReadOnlyList<object> targets = target is null
            ? Array.Empty<object>()
            : new[] { target };

        return new PriorityAction.CastSpell(card, targets);
    }

    /// <summary>
    /// Build a <see cref="PriorityAction.ActivateAbility"/> for the first
    /// non-mana activated ability on the named permanent controlled by
    /// <paramref name="self"/>, mirroring
    /// <see cref="LegalActionEnumerator.ForPriority"/> (lines 80–88).
    ///
    /// <para>Gates applied (same as the enumerator):</para>
    /// <list type="bullet">
    ///   <item>Permanent must be on <paramref name="self"/>'s battlefield.</item>
    ///   <item>Ability must not be a mana ability (<see cref="IManaAbility"/>).</item>
    ///   <item>All activation costs must be payable (<c>CanPay(self)</c>).</item>
    /// </list>
    ///
    /// The optional <paramref name="target"/> is folded into a single-element
    /// targets list, matching the enumerator's
    /// <c>Array.Empty&lt;object&gt;()</c> baseline. Loyalty abilities and mana
    /// abilities are excluded (handled separately by the enumerator and never
    /// needed by the common win-line patterns).
    ///
    /// Returns null when the permanent is absent or no payable non-mana
    /// activated ability is found.
    /// </summary>
    public static PriorityAction? BuildActivate(
        GameContext ctx, Player self, string sourceName, object? target = null)
    {
        // Find the permanent on the battlefield — mirrors enumerator line 81.
        var permanent = self.Zones.Battlefield.GetCards()
            .FirstOrDefault(c => c.Name == sourceName);
        if (permanent is null) return null;

        // Find first payable non-mana IActivatedAbility — mirrors enumerator
        // lines 83–88.
        var ability = permanent.Abilities
            .OfType<IActivatedAbility>()
            .FirstOrDefault(a => a is not IManaAbility && a.Costs.All(cost => cost.CanPay(self)));
        if (ability is null) return null;

        IReadOnlyList<object> targets = target is null
            ? Array.Empty<object>()
            : new[] { target };

        return new PriorityAction.ActivateAbility(ability, targets);
    }
}
