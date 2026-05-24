using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Bombardment (Tempest, reprinted many
/// times). Enchantment — {1}{R}. Oracle text:
///
///   "Sacrifice a creature: This enchantment deals 1 damage to any target."
///
/// ## Implemented (v1)
/// - Card identity (Enchantment, mana cost {1}{R}, owner/controller).
/// - <b>Activated ability</b> with one cost
///   (<see cref="SacrificeAnotherCreatureCost"/>) and one effect (1 damage
///   to a chosen target). Because the enchantment itself is non-creature,
///   "another" is trivially satisfied — same cost class as Yawgmoth.
/// - <b>Target picking</b>: v1 uses a deterministic "first legal"
///   heuristic. The <em>damage</em> target is set on the ability via
///   <see cref="DamageTarget"/> before activation; the <em>sacrifice</em>
///   target is set on <see cref="SacrificeAnotherCreatureCost.Target"/>
///   (or auto-picked by that cost when null). Bot wiring is in
///   <see cref="CreateForBot"/>: it picks the first sacrificeable creature
///   the controller controls and the first damageable creature an
///   opponent controls (falling back to that opponent).
///
/// ## Deferred (v1 gaps)
/// - Real prompt-driven targeting for the damage target — the activated
///   ability does not yet declare a <c>TargetRequest</c> because the
///   activated-ability flow doesn't prompt for targets the way
///   SpellCastFlow does. Same gap as WalkingBallista ping targeting.
/// - "Sacrifice a creature" technically allows sacrificing any creature
///   the controller controls; <see cref="SacrificeAnotherCreatureCost"/>'s
///   "another" is satisfied vacuously here (Goblin Bombardment isn't a
///   creature) so behavior is correct — only the cost name reads
///   "another," which is fine.
/// </summary>
[CardName("Goblin Bombardment")]
public static class GoblinBombardmentFactory
{
    /// <summary>
    /// Construct Goblin Bombardment with a single, unbound activated
    /// ability: cost = sacrifice a creature, effect = 1 damage to
    /// <see cref="DamageTarget"/> if set (no-op otherwise). Callers
    /// set <see cref="DamageTarget"/> before activation to choose the
    /// damage target; bots can use <see cref="CreateForBot"/> for the
    /// pick-first-legal heuristic.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var goblin = new Enchantment("Goblin Bombardment", "{1}{R}");
        goblin.SetOwner(owner);
        goblin.SetController(owner);

        // The ability holds its own mutable "chosen damage target" slot.
        // Set DamageTarget before activation; the effect closure reads it
        // at resolution time so that the same ability instance can be
        // re-targeted across activations.
        var sacrificeCost = new SacrificeAnotherCreatureCost(goblin);
        var pingEffect = new PingEffectState();

        var ability = new GoblinBombardmentAbility(
            source: goblin,
            controller: owner,
            sacrificeCost: sacrificeCost,
            damageState: pingEffect);

        goblin.AddAbility(ability);
        return goblin;
    }

    /// <summary>
    /// Convenience wrapper: pre-binds the sacrifice and damage choices
    /// using the bot's pick-first-legal heuristic.
    ///   - Sacrifice: first creature the controller controls.
    ///   - Damage target: first opponent's creature, falling back to the
    ///     first opponent.
    /// </summary>
    public static Enchantment CreateForBot(Player owner, IReadOnlyList<Player> allPlayers)
    {
        var goblin = Create(owner);
        var ability = (GoblinBombardmentAbility)goblin.Abilities
            .OfType<ActivatedAbility>()
            .Single();

        var sacrifice = owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();
        if (sacrifice != null)
        {
            ability.SacrificeChoice.Target = sacrifice;
        }

        var opponent = allPlayers?.FirstOrDefault(p => !ReferenceEquals(p, owner));
        if (opponent != null)
        {
            var creature = opponent.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .FirstOrDefault();
            ability.DamageTarget = (object?)creature ?? opponent;
        }

        return goblin;
    }
}

/// <summary>
/// Goblin Bombardment's only activated ability. Subclasses
/// <see cref="ActivatedAbility"/> so the chosen damage target can travel
/// with the ability instance (test / bot setter), and the sacrifice cost
/// is reachable for tests that want to inspect or set its
/// <see cref="SacrificeAnotherCreatureCost.Target"/>.
/// </summary>
public sealed class GoblinBombardmentAbility : ActivatedAbility
{
    /// <summary>
    /// The single sacrifice cost on the ability — exposed so callers can
    /// pre-set <see cref="SacrificeAnotherCreatureCost.Target"/> before
    /// activation.
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    private readonly PingEffectState _state;

    /// <summary>
    /// The chosen damage target. Set this between activations. Accepts
    /// <see cref="Player"/>, <see cref="Creature"/>, or
    /// <see cref="Planeswalker"/> — anything <c>OracleSpellBinder.DealDamage</c>
    /// can route 1 damage to. <c>null</c> means the effect is a no-op.
    /// </summary>
    public object? DamageTarget
    {
        get => _state.Target;
        set => _state.Target = value;
    }

    internal GoblinBombardmentAbility(
        Enchantment source,
        Player controller,
        SacrificeAnotherCreatureCost sacrificeCost,
        PingEffectState damageState)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { sacrificeCost },
            effects: new IEffect[]
            {
                new Effect(
                    "Goblin Bombardment: 1 damage to chosen target",
                    () =>
                    {
                        if (damageState.Target == null) return; // no chosen target — no-op
                        OracleSpellBinder.DealDamage(damageState.Target, 1);
                    }),
            })
    {
        SacrificeChoice = sacrificeCost;
        _state = damageState;
    }
}

/// <summary>
/// Mutable box for the chosen damage target so the effect closure can
/// re-read it across activations.
/// </summary>
internal sealed class PingEffectState
{
    public object? Target { get; set; }
}
