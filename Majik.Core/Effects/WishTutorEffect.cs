using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 408 — primitive for wish-tutor effects ("Choose a card you own from
/// outside the game, reveal it, and put it into your hand"). The
/// "outside the game" pool is the caster's wishboard surface
/// (<see cref="Player.Wishboard"/> — same pile as
/// <see cref="Player.Sideboard"/>; the deck-builder is responsible for
/// marking which cards are in the sideboard, and every sideboard card
/// is automatically a wishboard candidate).
///
/// On resolution:
/// <list type="number">
///   <item>Enumerate <c>caster.Wishboard</c>.</item>
///   <item>Filter by the predicate supplied at construction time
///         (e.g. "artifact card" for Karn the Great Creator,
///         "sorcery card" for Burning Wish, "any card" for Mastermind's
///         Acquisition mode 2).</item>
///   <item>Prompt the caster's <see cref="IPlayerAgent.ChooseFromPileAsync"/>
///         with the filtered list and the human-readable
///         <see cref="PileLabel"/>.</item>
///   <item>If the agent picks a card, move it from the wishboard zone
///         to the caster's hand and stamp its zone.</item>
/// </list>
/// CR 408 owned-and-eligible — the predicate is responsible for ANY
/// ownership / type checks. The default helper predicates supplied here
/// (<see cref="Predicates.ArtifactCard"/> / <see cref="Predicates.AnyCard"/>
/// etc.) gate on type only; the caster's wishboard already contains only
/// cards the caster owns, so an explicit owner check is redundant in v1.
///
/// Empty filtered candidate list, or an agent that returns
/// <see langword="null"/>, resolves as a no-op (legal under CR 408 / CR
/// 117.x — wish effects that find no legal card simply do nothing). The
/// loyalty / spell-resolve frame still completes.
///
/// Construction is decoupled from <see cref="IEffect"/> so the same
/// primitive can be used:
/// <list type="bullet">
///   <item>From a <see cref="LoyaltyAbility"/> body
///         (Karn, the Great Creator's -2) by calling
///         <see cref="Resolve"/> directly.</item>
///   <item>From a <see cref="SpellDefinition"/>'s
///         <see cref="SpellDefinition.EffectFactory"/> closure
///         (Mastermind's Acquisition mode 2 / Burning Wish /
///         Cunning Wish / Glittering Wish / Living Wish) by
///         wrapping it with <see cref="AsEffect"/>.</item>
/// </list>
/// </summary>
public sealed class WishTutorEffect
{
    /// <summary>Common predicates for filtering wishboard candidates by
    /// card type. Factories may also supply their own bespoke predicates
    /// (e.g. "noncreature card" for Cunning Wish).</summary>
    public static class Predicates
    {
        /// <summary>Any card the caster owns from outside the game — the
        /// Mastermind's Acquisition mode 2 / Burning Wish (when the
        /// reprint sideboard isn't artifact-only) shape.</summary>
        public static bool AnyCard(ICard _) => true;

        /// <summary>Artifact card — Karn, the Great Creator's -2.</summary>
        public static bool ArtifactCard(ICard c) =>
            c.HasType(Majik.Core.Cards.Types.CardType.Artifact);

        /// <summary>Sorcery card — Burning Wish.</summary>
        public static bool SorceryCard(ICard c) =>
            c.HasType(Majik.Core.Cards.Types.CardType.Sorcery);

        /// <summary>Instant card — Cunning Wish (the "instant card from
        /// outside the game" wish variant).</summary>
        public static bool InstantCard(ICard c) =>
            c.HasType(Majik.Core.Cards.Types.CardType.Instant);

        /// <summary>Creature or land card — Living Wish.</summary>
        public static bool CreatureOrLandCard(ICard c) =>
            c.HasType(Majik.Core.Cards.Types.CardType.Creature)
            || c.HasType(Majik.Core.Cards.Types.CardType.Land);

        /// <summary>Multicolored card — Glittering Wish (CR 105.1c —
        /// multicolored = ≥2 distinct colours).</summary>
        public static bool MulticoloredCard(ICard c) =>
            Majik.Core.Cards.CardColors.GetColors(c).Count >= 2;
    }

    /// <summary>Predicate the wishboard is filtered by.</summary>
    public Func<ICard, bool> Predicate { get; }

    /// <summary>Human-readable pile label surfaced to the agent prompt
    /// (e.g. "an artifact card from outside the game"). Defaults to
    /// "a card from outside the game" when not specified.</summary>
    public string PileLabel { get; }

    /// <summary>Bot-intent classification surfaced to the agent prompt.
    /// Defaults to <see cref="BotIntent.Tutor"/> — wishboard fetches
    /// score as tutors for heuristic agents.</summary>
    public BotIntent Intent { get; }

    public WishTutorEffect(
        Func<ICard, bool> predicate,
        string? pileLabel = null,
        BotIntent intent = BotIntent.Tutor)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        PileLabel = pileLabel ?? "a card from outside the game";
        Intent = intent;
    }

    /// <summary>
    /// Resolve the wish-tutor against <paramref name="caster"/>'s
    /// wishboard. Returns the picked card (now in <paramref name="caster"/>'s
    /// hand) or <see langword="null"/> when no eligible card existed or
    /// the agent declined.
    /// </summary>
    public ICard? Resolve(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var candidates = caster.Wishboard.GetCards()
            .Where(Predicate)
            .ToList();
        if (candidates.Count == 0) return null;

        // TODO: remove sync-over-async once IEffect.Execute becomes async.
        // Mirrors SearchSpellFactory / GoblinMatronFactory / Liliana of
        // the Veil's deterministic agent-or-first-pick pattern.
        var agent = AgentRegistry.Get(caster);
        ICard? pick = agent != null
            ? agent.ChooseFromPileAsync(caster, candidates, PileLabel, Intent)
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null) return null;

        // Defensive — the agent could return a card not in candidates
        // (illegal pick); reject and fall back to the deterministic
        // first-candidate so the effect still resolves cleanly. Same
        // posture Annihilator / Liliana use.
        if (!ReferenceEquals(pick, candidates[0])
            && candidates.All(c => !ReferenceEquals(c, pick)))
        {
            pick = candidates[0];
        }

        // Move sideboard → hand. Wishboard cards have no engine event
        // bus subscriber chain to honour (CR 408 — outside the game is
        // not a tracked zone for triggers / replacements), so a raw
        // zone move suffices.
        caster.Wishboard.RemoveCard(pick);
        caster.Zones.Hand.AddCard(pick);
        if (pick is Card concrete)
        {
            concrete.SetZone(ZoneType.Hand);
        }
        return pick;
    }

    /// <summary>Wrap this wish-tutor as an <see cref="IEffect"/> bound
    /// to <paramref name="caster"/>. Suitable for placing inside a
    /// <see cref="SpellDefinition.EffectFactory"/> closure.</summary>
    public IEffect AsEffect(Player caster) =>
        new Effect($"wish-tutor: {PileLabel}", () => Resolve(caster));
}
