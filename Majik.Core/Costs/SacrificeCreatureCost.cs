using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a creature."
/// Caller specifies WHICH creature; this class just performs the
/// payment + validation.
/// </summary>
public sealed class SacrificeCreatureCost : IAdditionalCost
{
    private readonly Creature _target;
    private readonly IEventBus? _eventBus;

    /// <param name="target">The creature sacrificed as the cost.</param>
    /// <param name="eventBus">Optional event bus. When supplied, paying the
    /// cost publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the caster so aristocrat payoffs fire. Null preserves the
    /// legacy publish-nothing posture.</param>
    public SacrificeCreatureCost(Creature target, IEventBus? eventBus = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _eventBus = eventBus;
    }

    public string Description => $"sacrifice {_target.Name}";

    /// <summary>
    /// The creature that was sacrificed once <see cref="Pay"/> succeeded.
    /// Null before payment. Effect closures (Fling, Thud, Life's Legacy …)
    /// read this to compute "sacrificed creature's power / toughness".
    /// </summary>
    public Creature? Sacrificed { get; private set; }

    public bool CanPay(Player caster) =>
        ReferenceEquals(_target.Controller, caster)
        && _target.Zone == ZoneType.Battlefield
        && _target.HasType(CardType.Creature);

    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;
        SacrificeCostHelper.Sacrifice(caster, _target, _eventBus);
        Sacrificed = _target;
        return true;
    }
}

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a creature." —
/// data-driven binder variant that defers target choice to
/// <see cref="Pay"/>. The first eligible creature the caster controls is
/// sacrificed (deterministic v1 — matches
/// <see cref="SacrificeAnotherCreatureCost"/>). After payment,
/// <see cref="Sacrificed"/> exposes the creature for downstream effects
/// that reference "the sacrificed creature's power" (Fling, Thud, …).
/// </summary>
public sealed class SacrificeACreatureAdditionalCost : IAdditionalCost
{
    private readonly IEventBus? _eventBus;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeACreatureAdditionalCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    public Creature? Sacrificed { get; private set; }

    public string Description => "sacrifice a creature";

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any();
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();
        if (pick == null) return false;
        SacrificeCostHelper.Sacrifice(caster, pick, _eventBus);
        Sacrificed = pick;
        return true;
    }
}

/// <summary>
/// "As an additional cost to cast this spell, sacrifice an artifact." —
/// same shape as <see cref="SacrificeACreatureAdditionalCost"/> but for
/// artifacts. <see cref="Sacrificed"/> exposes the chosen artifact after
/// payment so effects can reference its mana value.
/// </summary>
public sealed class SacrificeAnArtifactAdditionalCost : IAdditionalCost
{
    private readonly IEventBus? _eventBus;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeAnArtifactAdditionalCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    public Permanent? Sacrificed { get; private set; }

    public string Description => "sacrifice an artifact";

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(p => p.HasType(CardType.Artifact));
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.HasType(CardType.Artifact));
        if (pick == null) return false;
        SacrificeCostHelper.Sacrifice(caster, pick, _eventBus);
        Sacrificed = pick;
        return true;
    }
}

/// <summary>
/// "As an additional cost to cast this spell, pay N life." Life payment
/// is deducted from the caster on <see cref="Pay"/>. Used by Hatred,
/// Toxic Deluge, Bond of Agony, etc. — those cards pay X life where X
/// equals the spell's X cost; the resolver passes the X value to the
/// constructor.
/// </summary>
public sealed class PayLifeAdditionalCost : IAdditionalCost
{
    private readonly int _amount;

    public PayLifeAdditionalCost(int amount)
    {
        _amount = amount;
    }

    /// <summary>The amount of life paid on <see cref="Pay"/>.</summary>
    public int Amount => _amount;

    public string Description => $"pay {_amount} life";

    public bool CanPay(Player caster) => caster != null && caster.LifeTotal >= _amount;

    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;
        caster.LoseLife(_amount);
        return true;
    }
}

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a Goblin." —
/// subtype-restricted variant of <see cref="SacrificeACreatureAdditionalCost"/>.
/// Used by Goblin Grenade (CR 601.2f). Picks the first eligible Goblin
/// (creature with the Goblin subtype) the caster controls on the battlefield
/// — deterministic v1, same posture as the other sacrifice picker costs
/// in this file. Self-sacrifice (the spell sacrificing itself) is impossible
/// here because Goblin Grenade is a Sorcery, not a Goblin creature.
///
/// <para><see cref="Sacrificed"/> exposes the sacrificed Goblin after
/// payment so downstream effects could reference it if needed; Goblin
/// Grenade itself doesn't read the sacrificed creature's stats — the
/// 5 damage is a flat amount independent of the sacrificed Goblin.</para>
/// </summary>
public sealed class SacrificeAGoblinAdditionalCost : IAdditionalCost
{
    private readonly IEventBus? _eventBus;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeAGoblinAdditionalCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>The Goblin that was sacrificed (null before <see cref="Pay"/>).</summary>
    public Creature? Sacrificed { get; private set; }

    public string Description => "sacrifice a Goblin";

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.HasSubtype(CardSubtype.Goblin));
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));
        if (pick == null) return false;
        SacrificeCostHelper.Sacrifice(caster, pick, _eventBus);
        Sacrificed = pick;
        return true;
    }
}

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a land." —
/// land variant of <see cref="SacrificeACreatureAdditionalCost"/> /
/// <see cref="SacrificeAnArtifactAdditionalCost"/>. Used by Shard Volley
/// (CR 601.2f). Any land qualifies — basic or nonbasic — so the filter is
/// the broad <see cref="CardType.Land"/> type (CR 305), not a basic-land
/// subtype like <see cref="SacrificeBasicLandCost"/>.
///
/// Picks the first eligible land the caster controls on the battlefield
/// (deterministic v1 — same posture as the sibling sacrifice-picker costs
/// in this file). A real "choose a land to sacrifice" agent prompt is
/// deferred behind the same prompting MVP those costs wait on.
///
/// <para><see cref="Sacrificed"/> exposes the sacrificed land after payment.
/// Shard Volley doesn't read it (the 3 damage is a flat amount independent
/// of the land), but the property mirrors the other costs' shape.</para>
/// </summary>
public sealed class SacrificeALandAdditionalCost : IAdditionalCost
{
    private readonly IEventBus? _eventBus;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeALandAdditionalCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>The land that was sacrificed (null before <see cref="Pay"/>).</summary>
    public Permanent? Sacrificed { get; private set; }

    public string Description => "sacrifice a land";

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(p => p.HasType(CardType.Land));
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.HasType(CardType.Land));
        if (pick == null) return false;
        SacrificeCostHelper.Sacrifice(caster, pick, _eventBus);
        Sacrificed = pick;
        return true;
    }
}
