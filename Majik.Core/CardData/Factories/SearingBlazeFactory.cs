using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Searing Blaze (Worldwake / Modern Horizons, {R}{R}).
///
/// Instant. Oracle text:
///   "Searing Blaze deals 1 damage to target player or planeswalker and 1
///    damage to target creature that player or that planeswalker's controller
///    controls.
///    Landfall — If you had a land enter the battlefield under your control
///    this turn, Searing Blaze deals 3 damage to that player or planeswalker
///    and 3 damage to that creature instead."
///
/// ## Implementation
///
/// CR 702.142 (Landfall) describes a triggered-ability shape; Searing Blaze
/// is one of the few cards that uses a landfall-style condition on an instant
/// spell — it's a resolution-time state check ("if you had a land enter under
/// your control this turn"), not a printed trigger. The flag is sampled from
/// <see cref="TurnState.LandEnteredThisTurn(Player)"/> at resolution.
///
/// Two <see cref="TargetRequest"/>s are declared:
///   - target[0] = "target player or planeswalker"
///   - target[1] = "target creature controlled by that player or planeswalker's controller"
///
/// V1 simplification: the engine's targeting prompt cannot yet express the
/// "controlled by the previous target's controller" constraint, so target[1]
/// is declared as "target creature" and the relationship is V1-relaxed —
/// callers/agents are expected to pick a creature whose controller matches
/// target[0]'s player (or target[0].Controller for planeswalkers). The
/// resolve effect honours both target picks and deals damage to each.
///
/// <see cref="OracleSpellBinder.DealDamage"/> handles Player + Creature only;
/// Planeswalker damage is dealt via <see cref="Planeswalker.RemoveLoyalty"/>
/// directly here.
///
/// Card-shape only here; the resolve-time spell definition is built on-demand
/// via <see cref="BuildSpellDefinition(Player, Func{TurnState?}, Func{object, object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver supplied by
/// the caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Searing Blaze")]
public static class SearingBlazeFactory
{
    public const string CardName = "Searing Blaze";
    public const string PrintedManaCost = "{R}{R}";

    public const int BaseDamage = 1;
    public const int LandfallDamage = 3;

    /// <summary>
    /// Build a Searing Blaze instant owned by <paramref name="owner"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Searing Blaze is
    /// cast. Two 1..1 target requests; on resolution the controller's
    /// per-turn landfall tally is sampled and the damage amount picked
    /// based on whether a land entered under the controller's control this
    /// turn (CR 702.142).
    /// </summary>
    /// <param name="controller">Spell controller — whose per-turn
    /// landfall tally drives the conditional upgrade.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. When the callback returns
    /// null (no driver wired — typical for shape / dispatcher tests) the
    /// gate is treated as inactive (base damage applies).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<TurnState?> turnStateResolver,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player or planeswalker", 1, 1, Array.Empty<object>()),
                // V1: targeting prompt cannot express "controlled by the
                // previous target's controller" — the constraint is enforced
                // at the agent/caller level, not at TargetRequest declaration.
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var playerOrPw = resolver(chosen.Targets[0][0]);
                var creature = resolver(chosen.Targets[1][0]);
                return new IEffect[]
                {
                    new Effect("Searing Blaze: landfall-conditional twin damage", () =>
                    {
                        var amount = IsLandfallActive(controller, turnStateResolver)
                            ? LandfallDamage
                            : BaseDamage;
                        DealDamageWithPlaneswalker(playerOrPw, amount);
                        DealDamageWithPlaneswalker(creature, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// Sample the controller's per-turn landfall tally (CR 702.142): true
    /// iff at least one land has entered the battlefield under
    /// <paramref name="controller"/>'s control this turn. When no
    /// <see cref="TurnState"/> is wired the gate is treated as inactive
    /// (base damage applies).
    /// </summary>
    public static bool IsLandfallActive(
        Player controller,
        Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        var turnState = turnStateResolver.Invoke();
        return turnState != null && turnState.LandEnteredThisTurn(controller);
    }

    /// <summary>
    /// Deal <paramref name="amount"/> damage to <paramref name="target"/>,
    /// extending <see cref="OracleSpellBinder.DealDamage"/> to also handle
    /// <see cref="Planeswalker"/> (loyalty removal — CR 119.3 / 306.7).
    /// </summary>
    public static void DealDamageWithPlaneswalker(object target, int amount)
    {
        if (amount <= 0) return;
        switch (target)
        {
            case Planeswalker pw: pw.RemoveLoyalty(amount); break;
            default: OracleSpellBinder.DealDamage(target, amount); break;
        }
    }
}
