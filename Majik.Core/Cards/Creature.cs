using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents a creature card/permanent.
/// </summary>
public class Creature : Permanent
{
    private int _damage;
    private int _basePower;
    private int _baseToughness;

    /// <summary>
    /// The base power of the creature.
    /// </summary>
    public int BasePower
    {
        get => _basePower;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Power cannot be negative", nameof(value));
            }
            _basePower = value;
            // CR 613 — base P/T feeds the layer pipeline's seed; invalidate the
            // owning service's memoization cache so a later GetPower recomputes.
            ActiveEffects?.BumpGeneration();
        }
    }

    /// <summary>
    /// The base toughness of the creature.
    /// </summary>
    public int BaseToughness
    {
        get => _baseToughness;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Toughness cannot be negative", nameof(value));
            }
            _baseToughness = value;
            // CR 613 — invalidate memoization (see BasePower).
            ActiveEffects?.BumpGeneration();
        }
    }

    /// <summary>
    /// The current power of the creature (base + effects).
    /// </summary>
    public int Power => GetPower();

    /// <summary>
    /// The current toughness of the creature (base + effects).
    /// </summary>
    public int Toughness => GetToughness();

    /// <summary>
    /// The damage marked on the creature.
    /// </summary>
    public int Damage
    {
        get => _damage;
        private set
        {
            if (value < 0)
            {
                throw new ArgumentException("Damage cannot be negative", nameof(value));
            }
            _damage = value;
        }
    }

    public Creature(string name, string manaCost, int power, int toughness, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Creature }, supertypes, subtypes)
    {
        BasePower = power;
        BaseToughness = toughness;
        _damage = 0;
    }
    // ActiveEffects moved up to Permanent (CR 613) so non-creature
    // permanents can also consult the layer system (e.g. Layer-5
    // colour-changing on artifacts / enchantments). P/T and keyword
    // lookups below read the inherited property.

    /// <summary>Get the current power after applying continuous effects.
    /// CR 708.2 — face-down creatures are 2/2 with no other characteristics,
    /// short-circuited before consulting the layer system.</summary>
    public int GetPower()
    {
        if (IsFaceDown) return 2;
        if (ActiveEffects == null) return BasePower;
        // Hot path — the scalar P/T cache returns an int with ZERO heap
        // allocation on a cache hit (no layered clone, no HashSets). See
        // ContinuousEffectsService.ComputePowerToughness.
        return ActiveEffects.ComputePowerToughness(this).Power;
    }

    /// <summary>Get the current toughness after applying continuous effects.
    /// CR 708.2 — face-down creatures are 2/2.</summary>
    public int GetToughness()
    {
        if (IsFaceDown) return 2;
        if (ActiveEffects == null) return BaseToughness;
        // Hot path — see GetPower / ComputePowerToughness (zero-alloc on hit).
        return ActiveEffects.ComputePowerToughness(this).Toughness;
    }

    /// <summary>
    /// CR 613.1f / 613.8 — true iff this creature currently has
    /// <paramref name="keyword"/> as an EFFECTIVE keyword (printed marker OR
    /// granted by an active Layer-6 effect, minus any Layer-6 strip). When
    /// <see cref="Permanent.ActiveEffects"/> is wired this reads the layer
    /// system's post-Layer-6 keyword set
    /// (<see cref="Majik.Core.Effects.ContinuousEffectsService.EffectiveKeywords"/>);
    /// otherwise it falls back to printed
    /// <see cref="Majik.Core.Abilities.KeywordAbility"/> markers. Casing is
    /// irrelevant (the keyword set is ordinal-ignore-case). Keyword-gated
    /// anthems ("Other creatures you control with flying get +1/+1") read
    /// through this so a creature granted the keyword qualifies.
    /// </summary>
    public bool HasEffectiveKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return false;
        if (ActiveEffects != null)
        {
            return ActiveEffects.EffectiveKeywords(this).Contains(keyword);
        }
        return Abilities
            .OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deal damage to the creature.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Damage amount cannot be negative", nameof(amount));
        }

        // CR 120.3 — stamp the per-turn "was dealt damage this turn" flag at
        // the single common creature-damage sink (every combat / noncombat /
        // ping path routes here). A 0-amount deal is not damage and is
        // filtered by RecordDamageDealt.
        RecordDamageDealt(amount);
        Damage += amount;
    }

    /// <summary>
    /// Remove damage from the creature.
    /// </summary>
    public void RemoveDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Removal amount cannot be negative", nameof(amount));
        }

        Damage = Math.Max(0, Damage - amount);
    }

    /// <summary>
    /// Clear all damage from the creature.
    /// </summary>
    public void ClearDamage()
    {
        Damage = 0;
    }

    /// <summary>
    /// CR 701.15c — clear marked combat damage when a regeneration shield
    /// is consumed. Overrides the no-op default on <see cref="Permanent"/>
    /// so the shield-consume hook no longer needs a runtime type test.
    /// </summary>
    protected override void OnRegenerationShieldConsumed()
    {
        ClearDamage();
    }

    /// <summary>
    /// Check if the creature is dead (damage >= toughness).
    /// </summary>
    public bool IsDead()
    {
        return Damage >= Toughness;
    }

    /// <summary>
    /// CR 702.2b — set by combat when a deathtouch source deals nonzero
    /// damage to this creature. The SBA pass uses this as a synonym for
    /// "lethal damage marked." Cleared in cleanup along with Damage.
    /// </summary>
    public bool MarkedForDestructionByDeathtouch { get; set; }

    /// <summary>CR 903.3 — flagged if this creature is its controller's
    /// commander. Combat damage from a commander is tracked per-opponent
    /// for the 21-damage loss condition (CR 903.10a).</summary>
    public bool IsCommander { get; set; }

    /// <summary>
    /// CR 702.74b — set by <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>
    /// when the spell was cast for its evoke cost. The "sacrifice this
    /// creature" ETB trigger added to evoke creatures (CR 702.74c) reads
    /// this as an intervening-if to decide whether to fire. Tagged on the
    /// Creature object so it survives the Stack → Battlefield transition
    /// (we set it during the alt-cost's <c>OnResolved</c>, which runs
    /// before <see cref="Majik.Core.Services.StackResolver"/> moves the
    /// card to the battlefield and fires the ETB <see cref="CardMovedEvent"/>).
    /// </summary>
    public bool EvokeWasPaid { get; set; }

    /// <summary>
    /// CR 702.152b — set by <see cref="Majik.Core.Costs.BlitzAlternativeCost"/>
    /// when the spell was cast for its blitz cost. The three blitz riders added
    /// to blitz creatures (CR 702.152c — gains haste; "when this creature dies,
    /// draw a card"; and a delayed "sacrifice it at the beginning of the next
    /// end step") all gate on this flag so a creature cast for its normal mana
    /// cost (or returned to the battlefield some other way) gets none of them.
    /// Tagged on the Creature object so it survives the Stack → Battlefield
    /// transition (we set it during the alt-cost's <c>OnResolved</c>, which runs
    /// before <see cref="Majik.Core.Services.StackResolver"/> moves the card to
    /// the battlefield and fires the ETB <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>).
    /// Mirror of <see cref="EvokeWasPaid"/>.
    /// </summary>
    public bool BlitzWasPaid { get; set; }

    /// <summary>
    /// CR 508.1a relaxation — set by effects that let a creature "attack this
    /// turn as though it didn't have defender" (Nivix Cyclops, Axebane
    /// Stag/Assault Formation family). When true, the Defender keyword's
    /// can't-attack rule (CR 702.3b) is ignored for THIS creature for the rest
    /// of the turn; every OTHER attack-legality check (tapped, summoning
    /// sickness, "can't attack" restrictions) still applies normally. This is a
    /// per-turn permission grant, not a keyword removal — the creature still
    /// HAS defender (e.g. for "can't block" effects keyed on defender), it is
    /// merely permitted to be declared as an attacker. Cleared at cleanup
    /// (CR 514.2) alongside other "until end of turn" effects.
    /// </summary>
    public bool CanAttackAsThoughItDidntHaveDefenderThisTurn { get; set; }
}
