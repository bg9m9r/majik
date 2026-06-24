using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Izzet Signet (Ravnica "Signet" mana-rock cycle —
/// the Guildpact enemy-colour UR member).
///
/// Oracle text (verified against Scryfall):
/// <code>
/// Artifact {2}.
/// {1}, {T}: Add {U}{R}.
/// </code>
///
/// ## Fully JSON-driven
/// Like <see cref="BorosSignetFactory"/>, Izzet Signet's whole shape is
/// expressed in <c>Majik.Core/CardData/Cards/izzet-signet.json</c>. The JSON
/// <c>mana</c> ability schema models the printed <b>{1}</b> additional cost
/// via its <c>cost</c> field, so <see cref="CardDefRuntime"/> builds the exact
/// same additional-cost <see cref="Majik.Core.Abilities.ManaAbility"/> the
/// hand-written signet factories build:
/// <list type="bullet">
///   <item><c>manaGenerated</c> = <c>{U}{R}</c> — both coloured pips emitted
///     together in one atomic step (CR 605.1 — a mana ability, never on the
///     stack).</item>
///   <item><c>canActivateCheck</c> = <c>!IsTapped &amp;&amp;
///     ManaPool.CanPay({1})</c> — the {T} (untap) half plus the {1}
///     affordability gate, so activation never taps the signet only to no-op
///     on payment.</item>
///   <item><c>additionalCostPayer</c> = <c>PayMana({1})</c> — the printed {1}
///     extra cost, deducted from the pool atomically with the {T} tap
///     (CR 605.1).</item>
/// </list>
/// This factory is therefore a thin loader: it materializes the JSON
/// definition through <see cref="CardDefinitionFactory"/> and returns the
/// finished <see cref="Artifact"/>. The single mana ability comes entirely
/// from the JSON — none is added here.
///
/// ## Signet net mana
/// Activating costs {1} (deducted from the pool) and adds {U}{R} — a net gain
/// of 1 mana plus conversion of one generic into two coloured pips, the
/// signature signet ramp/fixing curve (1 → UR).
///
/// ## Deferred (v1 gaps — same posture as the rest of the cycle)
/// - Activation requires {1} already in the mana pool. The engine does not
///   auto-tap other sources to feed the signet cost (no look-ahead mana
///   planner) — identical to every other additional-mana-cost activated /
///   mana ability (filter lands, Mind Stone's draw cost, Springleaf Drum,
///   and the C#-built signet members).
/// </summary>
[CardName("Izzet Signet")]
public static class IzzetSignetFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("izzet-signet");

    /// <summary>
    /// Construct Izzet Signet owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // {2} Artifact identity + the {1}, {T}: Add {U}{R} mana ability are
        // both built from the JSON definition (the mana-ability `cost` field
        // carries the {1} extra cost). CR 605.1 — the produced {U}{R} mana
        // ability never uses the stack.
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
