using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rakdos Signet (Ravnica "Signet" mana-rock cycle).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{1}, {T}: Add {B}{R}."
/// Printed mana cost: {2}.
///
/// Thin wrapper that loads the artifact identity ({2} Artifact, name,
/// owner/controller wiring) from
/// <c>Majik.Core/CardData/Cards/rakdos-signet.json</c> via
/// <see cref="CardDefinitionFactory"/>, then attaches the single mana
/// ability in C#. The {1}-pay shape is not expressible in the JSON
/// <c>mana</c> ability schema (which builds a vanilla cost-free
/// <see cref="ManaAbility"/>), so the pay-mana additional-cost
/// <see cref="ManaAbility"/> overload is wired here — the same proven
/// shape <see cref="FilterLandCycleFactory"/> uses for its
/// <c>{1}, {T}: Add …</c> filter modes.
///
/// ## Implemented (v1)
/// - Artifact identity ({2}, owner / controller wiring) + printed name.
/// - <b>{1}, {T}: Add {B}{R}</b> — a single <see cref="ManaAbility"/>
///   wired via the additional-cost overload (CR 605.1 — still a mana
///   ability, never on the stack):
///     - <c>canActivateCheck</c> = <c>!IsTapped &amp;&amp;
///       ManaPool.CanPay({1})</c>. The <c>!IsTapped</c> half models the
///       {T} cost (the engine taps in <see cref="ManaAbility.Activate"/>);
///       the affordability half gates on the {1} extra cost so activation
///       can't tap the signet and then no-op on an unpayable cost.
///     - <c>additionalCostPayer</c> = <c>PayMana({1})</c> — the printed
///       {1} extra cost, paid atomically with the {T} tap (CR 605.1).
///   Both {B} and {R} are produced together by the one activation —
///   unlike the talisman/painland cycles' "or" modal split, the signet
///   adds the colour pair as a single fixed output.
///
/// ## Signet net mana
/// Activating costs {1} (deducted from the pool) and adds {B}{R} — a net
/// gain of 1 mana plus conversion of one generic into two coloured pips,
/// the signature signet ramp/fixing curve (1 → BR).
///
/// ## Deferred (v1 gaps)
/// - Activation requires {1} to already be in the mana pool. The engine
///   doesn't auto-tap other sources to feed the {1} cost (no look-ahead
///   mana planner) — same posture every other additional-mana-cost
///   activated ability takes (Mind Stone's draw cost, Springleaf Drum,
///   the filter-land cycle, etc.).
/// </summary>
[CardName("Rakdos Signet")]
public static class RakdosSignetFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rakdos-signet");

    /// <summary>Construct Rakdos Signet owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var signet = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // {1}, {T}: Add {B}{R}. CR 605.1 — mana ability, never on the
        // stack; the {1} extra cost is paid atomically with the {T} tap.
        var output = ManaCost.Parse("BR");
        var oneGeneric = ManaCost.Parse("1");

        signet.AddAbility(new ManaAbility(
            source: signet,
            controller: owner,
            manaGenerated: output,
            canActivateCheck: () => !signet.IsTapped && owner.ManaPool.CanPay(oneGeneric),
            additionalCostPayer: p => p.PayMana(oneGeneric)));

        return signet;
    }
}
