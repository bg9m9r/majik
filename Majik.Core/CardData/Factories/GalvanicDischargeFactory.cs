using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanic Discharge (Modern Horizons 3, {R}).
///
/// Instant. Oracle text:
///   "Galvanic Discharge deals X damage to any target, where X is 1 plus
///    the number of charge counters on artifacts and/or lands you control."
///
/// ## Implementation
///
/// On resolution, count the total number of <see cref="CounterType.Charge"/>
/// counters across every <see cref="Artifact"/> and <see cref="Land"/> the
/// spell's controller controls. Damage dealt = 1 + that total
/// (creature-type artifacts count too — every card whose type set includes
/// <see cref="CardType.Artifact"/> or <see cref="CardType.Land"/> is
/// considered, mirroring CR 205.3 type-checking semantics). Charge
/// counters on opponent permanents do not contribute, nor do charge
/// counters on creatures that aren't also artifacts.
///
/// Card-shape only here; the resolve-time spell definition (target +
/// charge-counter-driven damage effect) is built on demand via
/// <see cref="BuildSpellDefinition(Player, Func{object, object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver
/// supplied by the caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Galvanic Discharge")]
public static class GalvanicDischargeFactory
{
    public const string CardName = "Galvanic Discharge";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Build a Galvanic Discharge instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time damage effect.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Galvanic
    /// Discharge is cast. Single 1..1 "any target" request; on resolution
    /// the controller's battlefield is sampled for charge counters on
    /// artifacts and/or lands, and 1 + that total is dealt to the chosen
    /// target.
    /// </summary>
    /// <param name="controller">Spell controller — the player whose
    /// artifacts/lands are sampled for charge counters.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Galvanic Discharge: deal 1 + charge counters damage", () =>
                    {
                        var amount = 1 + CountChargeCountersOnArtifactsAndLands(controller);
                        OracleSpellBinder.DealDamage(target, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// Sum charge counters across every artifact and/or land the
    /// <paramref name="controller"/> controls on the battlefield. A
    /// permanent qualifies if its type set contains
    /// <see cref="CardType.Artifact"/> or <see cref="CardType.Land"/>
    /// (artifact creatures + artifact lands both count). Charge counters
    /// on creatures that aren't also artifacts/lands are excluded.
    /// </summary>
    public static int CountChargeCountersOnArtifactsAndLands(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var total = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent perm) continue;
            if (!perm.HasType(CardType.Artifact) && !perm.HasType(CardType.Land)) continue;
            total += perm.Counters.Count(CounterType.Charge);
        }
        return total;
    }
}
