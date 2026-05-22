using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

    public SacrificeCreatureCost(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
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
        caster.Zones.Battlefield.RemoveCard(_target);
        caster.Zones.Graveyard.AddCard(_target);
        _target.SetZone(ZoneType.Graveyard);
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
        caster.Zones.Battlefield.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
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
        caster.Zones.Battlefield.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
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

    public string Description => $"pay {_amount} life";

    public bool CanPay(Player caster) => caster != null && caster.LifeTotal >= _amount;

    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;
        caster.LoseLife(_amount);
        return true;
    }
}
