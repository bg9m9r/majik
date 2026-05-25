using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Spells;

/// <summary>
/// Represents a spell on the stack.
/// </summary>
public class Spell : ISpell
{
    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<ICost> _costs = new();
    private readonly List<IEffect> _effects = new();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public ICard Card { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<ICost> Costs => _costs.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;

    /// <summary>
    /// Raw targets selected at cast time (CR 601.2c). Independent of the
    /// engine's <see cref="ITarget"/> abstraction — used by resolution
    /// recheck (CR 608.2b) to validate against current game state.
    /// </summary>
    public IList<object> ChosenTargets { get; } = new List<object>();

    /// <summary>
    /// Predicate the resolver uses to check whether at least one chosen
    /// target is still legal. Null = spell has no targets (always passes).
    /// </summary>
    public Func<object, bool>? TargetLegalityPredicate { get; set; }

    /// <summary>
    /// CR 608.2 / CR 715.3d — optional override of the post-resolution
    /// destination zone. Stamped by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// from <see cref="Majik.Core.Costs.IAlternativeCost.PostResolutionZone"/>
    /// when the cast used an alt-cost that re-routes destination (Adventure
    /// → Exile so a creature card cast as Adventure does not enter the
    /// battlefield). Read by <see cref="Majik.Core.Services.StackResolver"/>
    /// in preference to the printed-type default when non-null.
    /// </summary>
    public ZoneType? PostResolutionZoneOverride { get; set; }

    /// <summary>
    /// CR 118 — "no mana was spent to cast this spell" sentinel. Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the resolved total
    /// cost (printed + X + alt-cost overrides + Delve / cost reductions) is
    /// <c>ManaCost.Zero</c>. Read by triggers gated on the free-cast posture
    /// — Roiling Vortex's "Whenever a player casts a spell, if no mana was
    /// spent to cast it, …" is the prototypical consumer. Defaults to
    /// <c>false</c> so hand-built test spells without an explicit stamp are
    /// treated as normal (mana-paid) casts.
    /// </summary>
    public bool WasFreeCast { get; set; }

    /// <summary>
    /// CR 702.138b — "escaped" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used an
    /// <see cref="Majik.Core.Costs.EscapeAlternativeCost"/> alt-cost.
    /// Read by downstream gates that branch on "escaped"-ness:
    /// <see cref="Majik.Core.CardData.Factories.UroTitanFactory"/>'s
    /// "sacrifice it unless it escaped" trigger is the canonical
    /// consumer; future <em>escapes with [counters]</em> wiring
    /// (CR 702.138c) reads the same flag on the resolving spell to gate
    /// the ETB-with-counters replacement.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as normal (non-escape) casts.
    /// </summary>
    public bool WasCastForEscape { get; set; }

    /// <summary>
    /// CR 702.62d / 702.62g — "cast via suspend" runtime sentinel on
    /// the resolving spell. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used a
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> whose
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// flag is set. Read by downstream gates that branch on the
    /// suspend-cast posture; the matching
    /// <see cref="Majik.Core.Cards.Card.WasCastFromSuspend"/> mirror
    /// stamps the underlying card for resolve-body reads.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-suspend casts.
    /// </summary>
    public bool WasCastFromSuspend { get; set; }

    /// <summary>
    /// CR 702.33b — "kicked" runtime sentinel on the resolving spell.
    /// Stamped <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// when the cast layered a paid
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost"/>. Read by
    /// downstream rules / triggers that branch on the kicker decision
    /// (Burst Lightning's deals-4-instead-of-2 toggle is the canonical
    /// consumer; future kicker-bearing factories that key triggers on
    /// "if [spell] was kicked" read off the resolving spell).
    /// <see cref="Majik.Core.Cards.Card.WasKicked"/> mirrors the flag
    /// on the underlying card for resolve-body reads that don't have
    /// the spell reference handy.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-kicked casts.
    /// </summary>
    public bool WasKicked { get; set; }

    public Spell(ICard card, Player controller, IEnumerable<ITarget>? targets = null, IEnumerable<ICost>? costs = null, IEnumerable<IEffect>? effects = null)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (controller == null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        Card = card;
        Controller = controller;
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        _resolutionState = ResolutionState.NotResolving();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (costs != null)
        {
            _costs.AddRange(costs);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// Check if the spell can be cast.
    /// </summary>
    public bool CanBeCast(bool isMainPhase, bool isStackEmpty)
    {
        // Check card type restrictions
        if (Card.HasType(CardType.Sorcery))
        {
            // Sorceries can only be cast during main phase with empty stack
            return isMainPhase && isStackEmpty;
        }

        // Instants can be cast at instant speed (any time you have priority)
        if (Card.HasType(CardType.Instant))
        {
            return true;
        }

        // Other spell types (future)
        return true;
    }

    public void Resolve()
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Spell is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();
        
        // Resolution logic (Rule 608)
        // Execute all effects
        foreach (var effect in _effects)
        {
            effect.Execute();
        }
        
        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }

    /// <summary>
    /// Get the zone the spell should move to after resolution.
    /// Permanents go to battlefield, instants/sorceries go to graveyard (Rule 608.2).
    /// </summary>
    public ZoneType GetDestinationZone()
    {
        // Permanents go to battlefield
        if (Card.HasType(CardType.Creature) ||
            Card.HasType(CardType.Land) ||
            Card.HasType(CardType.Enchantment) ||
            Card.HasType(CardType.Artifact) ||
            Card.HasType(CardType.Planeswalker))
        {
            return ZoneType.Battlefield;
        }

        // Instants and sorceries go to graveyard
        if (Card.HasType(CardType.Instant) || Card.HasType(CardType.Sorcery))
        {
            return ZoneType.Graveyard;
        }

        // Default to graveyard
        return ZoneType.Graveyard;
    }
}
