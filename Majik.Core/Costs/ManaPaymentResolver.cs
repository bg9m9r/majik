using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// Activates a player's chosen mana sources (lands, mana abilities), adds the
/// generated mana into the player's pool, then attempts to pay the cost
/// from the pool. Atomic: if cost can't be paid, no sources are tapped.
/// </summary>
public sealed class ManaPaymentResolver
{
    private readonly ContinuousEffectsService? _layers;

    /// <summary>
    /// Construct a resolver. When <paramref name="layers"/> is supplied,
    /// each source's mana abilities are derived via
    /// <see cref="EffectiveManaAbilities.For"/> so CR 305.6 retyping
    /// (Blood Moon, Spreading Seas, etc.) is honoured. When null, the
    /// resolver falls back to the printed mana abilities — preserves
    /// behaviour for callers (and tests) that don't have a layer service.
    /// </summary>
    public ManaPaymentResolver(ContinuousEffectsService? layers = null)
    {
        _layers = layers;
    }

    public bool Pay(Player payer, ManaCost cost, ManaPayment payment)
    {
        if (payer == null) throw new ArgumentNullException(nameof(payer));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (payment == null) throw new ArgumentNullException(nameof(payment));

        // Pick the best ability per source given the cost. Dual / any-colour
        // lands (Sacred Foundry, Mox Opal) bind multiple ManaAbility options;
        // picking First() blindly short-pays when the bot picked the source
        // for a colour the first ability doesn't produce. Greedy: for each
        // source, choose the ability whose generated colour is still needed
        // (W, U, B, R, G in cost order); fall back to the first ability
        // when no coloured need matches.
        var remaining = new Dictionary<char, int>
        {
            ['W'] = cost.White, ['U'] = cost.Blue, ['B'] = cost.Black,
            ['R'] = cost.Red,   ['G'] = cost.Green,
        };
        var abilities = new List<IManaAbility>(payment.Sources.Count);
        foreach (var src in payment.Sources)
        {
            // CR 305.6 — when a Layer 4 retyping effect has changed the
            // source's land subtypes (Blood Moon, Spreading Seas, …),
            // EffectiveManaAbilities substitutes basic mana abilities
            // for the printed ones. Otherwise prints are returned as-is.
            // Null _layers ⇒ printed path (legacy/tests).
            var options = src is Permanent perm
                ? EffectiveManaAbilities.For(perm, _layers).ToList()
                : src.Abilities.OfType<IManaAbility>().ToList();
            if (options.Count == 0)
                throw new InvalidOperationException($"{src.Name} has no mana ability.");

            IManaAbility picked = options[0];
            foreach (var opt in options)
            {
                var mana = opt.ManaGenerated;
                char? satisfies = null;
                if (remaining['W'] > 0 && mana.White > 0) satisfies = 'W';
                else if (remaining['U'] > 0 && mana.Blue > 0) satisfies = 'U';
                else if (remaining['B'] > 0 && mana.Black > 0) satisfies = 'B';
                else if (remaining['R'] > 0 && mana.Red > 0) satisfies = 'R';
                else if (remaining['G'] > 0 && mana.Green > 0) satisfies = 'G';
                if (satisfies.HasValue)
                {
                    picked = opt;
                    remaining[satisfies.Value]--;
                    break;
                }
            }
            abilities.Add(picked);
        }

        // Simulate adding mana into a copy of the pool to verify the cost
        // is payable BEFORE we tap anything.
        var simulated = payer.ManaPool;
        var produced = new List<ManaCost>(abilities.Count);
        foreach (var ab in abilities)
        {
            // ManaAbility's pre-built ctor stores the cost on ManaGenerated.
            produced.Add(ab.ManaGenerated);
            simulated = simulated.Add(ab.ManaGenerated);
        }

        var (_, canPay) = simulated.Pay(cost);
        if (!canPay)
        {
            return false;
        }

        // Commit: actually tap each source and add to real pool, then pay.
        foreach (var ab in abilities)
        {
            ab.Activate();
        }
        foreach (var p in produced)
        {
            payer.AddManaToPool(p);
        }
        return payer.PayMana(cost);
    }
}
