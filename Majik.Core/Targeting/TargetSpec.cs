using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 115 — declarative spec for what a spell or ability can target.
/// Composable via the fluent builder; predicates evaluated against any
/// candidate (card or player) at cast time AND at resolution time
/// (CR 608.2b). Combined with the predicate-form, enumeration helpers
/// walk game state to find every legal candidate.
/// </summary>
public sealed class TargetSpec
{
    public string Description { get; }
    public bool AcceptsCreatures { get; private set; }
    public bool AcceptsPlayers { get; private set; }
    public bool AcceptsPlaneswalkers { get; private set; }
    public bool AcceptsArtifacts { get; private set; }
    public bool AcceptsEnchantments { get; private set; }
    public bool AcceptsLands { get; private set; }
    public Player? RequiredController { get; private set; }
    public Player? ExcludedController { get; private set; }

    /// <summary>Custom predicate run after the type filters.</summary>
    public Func<object, bool>? AdditionalPredicate { get; private set; }

    public TargetSpec(string description) { Description = description; }

    public TargetSpec AnyCreatureOrPlayer()
    { AcceptsCreatures = true; AcceptsPlayers = true; return this; }

    public TargetSpec AnyTarget()
    {
        AcceptsCreatures = AcceptsPlayers = AcceptsPlaneswalkers = true;
        return this;
    }

    public TargetSpec Creatures() { AcceptsCreatures = true; return this; }
    public TargetSpec Players() { AcceptsPlayers = true; return this; }
    public TargetSpec Planeswalkers() { AcceptsPlaneswalkers = true; return this; }
    public TargetSpec Artifacts() { AcceptsArtifacts = true; return this; }
    public TargetSpec Enchantments() { AcceptsEnchantments = true; return this; }
    public TargetSpec Lands() { AcceptsLands = true; return this; }

    public TargetSpec ControlledBy(Player p) { RequiredController = p; return this; }
    public TargetSpec NotControlledBy(Player p) { ExcludedController = p; return this; }

    public TargetSpec Where(Func<object, bool> predicate)
    { AdditionalPredicate = predicate; return this; }

    /// <summary>Test whether the candidate satisfies type + controller +
    /// predicate filters (untargetability is enforced separately by
    /// <see cref="TargetLegality"/>).</summary>
    public bool Matches(object candidate)
    {
        if (candidate is Player) return AcceptsPlayers
            && AdditionalPredicateOk(candidate);

        if (candidate is not ICard card) return false;

        // CR 115.4 / 613.1c / 711 — classify the creature / planeswalker
        // type slots by the EFFECTIVE (layer-computed) characteristics, not the
        // lingering printed C# instance type. A creature-front transform DFC
        // flipped to its planeswalker BACK face (Ral, Monsoon Mage // Ral,
        // Leyline Prodigy) is still a Creature instance whose printed
        // HasType(Creature) reads true and HasType(Planeswalker) reads false,
        // yet it is EFFECTIVELY a planeswalker (carries a transient loyalty
        // body, CR 306.5b) and NOT a creature. Without this widening a "target
        // planeswalker" / "any target" removal spell could never OFFER such a
        // permanent (the candidate-gather half of the v1-deferral), while a
        // "target creature"-only spell would wrongly offer it. An animated
        // non-creature (a manland computing as a creature via a Layer-4 grant)
        // is symmetrically offered as a creature. Non-Permanent cards (and the
        // artifact / enchantment / land slots) keep the printed-type check.
        var matchesCreature = AcceptsCreatures && IsEffectivelyCreature(card);
        var matchesPlaneswalker = AcceptsPlaneswalkers && IsEffectivelyPlaneswalker(card);

        var typeOk =
            matchesCreature ||
            matchesPlaneswalker ||
            (AcceptsArtifacts && card.HasType(CardType.Artifact)) ||
            (AcceptsEnchantments && card.HasType(CardType.Enchantment)) ||
            (AcceptsLands && card.HasType(CardType.Land));
        if (!typeOk) return false;

        if (RequiredController != null && !ReferenceEquals(card.Controller, RequiredController))
            return false;
        if (ExcludedController != null && ReferenceEquals(card.Controller, ExcludedController))
            return false;

        return AdditionalPredicateOk(candidate);
    }

    private bool AdditionalPredicateOk(object c) =>
        AdditionalPredicate == null || AdditionalPredicate(c);

    /// <summary>
    /// CR 613.1c / 711 — effective creature classification. A
    /// <see cref="Permanent"/> defers to
    /// <see cref="Permanent.IsEffectivelyCreature"/> (layer-computed, so a
    /// flipped planeswalker-back DFC is NOT a creature and an animated land
    /// IS); any other card (stack object, off-battlefield card) falls back to
    /// the printed <see cref="Card.HasType"/> flag.
    /// </summary>
    private static bool IsEffectivelyCreature(ICard card) =>
        card is Permanent p ? p.IsEffectivelyCreature() : card.HasType(CardType.Creature);

    /// <summary>
    /// CR 306.5b / 711 — effective planeswalker classification. A
    /// <see cref="Permanent"/> defers to
    /// <see cref="Permanent.IsEffectivePlaneswalker"/> (true for a real
    /// planeswalker OR a creature-front DFC flipped to its planeswalker back
    /// carrying a transient loyalty body); any other card falls back to the
    /// printed <see cref="Card.HasType"/> flag.
    /// </summary>
    private static bool IsEffectivelyPlaneswalker(ICard card) =>
        card is Permanent p ? p.IsEffectivePlaneswalker() : card.HasType(CardType.Planeswalker);
}
