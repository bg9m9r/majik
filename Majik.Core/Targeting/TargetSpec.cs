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

        var typeOk =
            (AcceptsCreatures && card.HasType(CardType.Creature)) ||
            (AcceptsPlaneswalkers && card.HasType(CardType.Planeswalker)) ||
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
}
