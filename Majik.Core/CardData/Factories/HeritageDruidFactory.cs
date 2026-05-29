using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heritage Druid (Morningtide, {G}).
/// Creature — Elf Druid 1/1. Oracle text (verified against Scryfall):
///   "Tap three untapped Elves you control: Add {G}{G}{G}."
///
/// The base shape (name, Creature, Elf Druid subtypes, {G}, 1/1) is
/// materialised from the embedded JSON definition (<c>heritage-druid.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The mana ability is layered on
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express a
/// tap-N-Elves cost, so the behaviour lives in the factory (same posture as
/// the other JSON-backed cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Mana ability (CR 605.1)</b>: "Tap three untapped Elves you control:
///   Add {G}{G}{G}." Built on <see cref="ManaAbility"/>'s no-self-tap
///   overload (<c>tapsAsCost: false</c>) — Heritage Druid does NOT tap
///   itself as part of the cost (there is no {T} symbol in the printed
///   cost). The entire activation cost is the
///   <see cref="TapElvesYouControlCost"/> (count 3), consulted by
///   <c>canActivateCheck</c> and executed by <c>additionalCostPayer</c>.
///   This is the same composition Springleaf Drum uses, minus the self-tap.
///
/// ## Notes
/// - <b>No summoning-sickness gate on the ABILITY.</b> Because the cost is
///   the printed word "Tap" on a set of Elves rather than a {T} symbol in
///   the activation cost, CR 302.6 does not apply: Heritage Druid may
///   activate this ability the turn it enters, and a summoning-sick Elf is
///   still an eligible body to tap (CR 302.6 only restricts a creature
///   tapping <i>itself</i> via a tap symbol in an activation cost). The
///   no-tap overload deliberately skips the central
///   <c>SummoningSicknessTapGate</c>. See
///   <see cref="TapElvesYouControlCost"/> for the eligibility rationale.
/// - The Druid itself is an Elf you control and so is an eligible body to
///   tap as one of the three (CR 602.2b imposes no "other" restriction).
/// - Agents may pre-set <see cref="HeritageDruidManaAbility.TapChoice"/>'s
///   <see cref="TapElvesYouControlCost.Targets"/> to pick exactly which
///   three Elves to tap; otherwise the cost taps the first three eligible
///   Elves deterministically.
/// </summary>
[CardName("Heritage Druid")]
public static class HeritageDruidFactory
{
    public const string CardName = "Heritage Druid";
    public const string Slug = "heritage-druid";
    public const int ElvesToTap = 3;

    /// <summary>
    /// Construct Heritage Druid owned and controlled by
    /// <paramref name="owner"/> with the "Tap three untapped Elves you
    /// control: Add {G}{G}{G}" mana ability attached. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Elf
        // Druid subtypes, {G}, 1/1). The JSON carries no abilities — the
        // mana ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 605.1 — mana ability (doesn't use the stack). The cost is the
        // tap-three-Elves cost alone; the Druid itself does not tap
        // (tapsAsCost: false on the no-self-tap overload).
        card.AddAbility(BuildManaAbility(card, owner));

        return card;
    }

    /// <summary>
    /// Build the "Tap three untapped Elves you control: Add {G}{G}{G}" mana
    /// ability. Exposed for tests that need to inspect or activate it.
    /// </summary>
    public static HeritageDruidManaAbility BuildManaAbility(Creature source, Player controller)
    {
        var tapCost = new TapElvesYouControlCost(ElvesToTap);
        return new HeritageDruidManaAbility(source, controller, tapCost);
    }
}

/// <summary>
/// Heritage Druid's mana ability. Subclasses <see cref="ManaAbility"/> so
/// the embedded <see cref="TapElvesYouControlCost"/> is reachable from
/// outside (agents / tests) for target-setting — same shape as
/// <see cref="SpringleafDrumManaAbility"/>'s creature-tap cost.
/// </summary>
public sealed class HeritageDruidManaAbility : ManaAbility
{
    /// <summary>
    /// The tap-three-Elves cost paid as part of activating this ability.
    /// Set <see cref="TapElvesYouControlCost.Targets"/> before
    /// <see cref="ManaAbility.Activate"/> to pick specific Elves; otherwise
    /// the cost falls back to its deterministic first-eligible pick.
    /// </summary>
    public TapElvesYouControlCost TapChoice { get; }

    internal HeritageDruidManaAbility(
        Creature source,
        Player controller,
        TapElvesYouControlCost tapCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse("{G}{G}{G}"),
            // CR 605.1 — legal only while three untapped Elves you control
            // exist. No {T}/summoning-sickness gate (cost is "Tap … Elves",
            // not a {T} symbol — CR 302.6 does not apply).
            canActivateCheck: () => tapCost.CanPay(controller),
            // The entire cost is paying the tap-three-Elves cost.
            additionalCostPayer: p => tapCost.Pay(p),
            // CR 602.2b — Heritage Druid does NOT tap itself; the only cost
            // is tapping the three chosen Elves.
            tapsAsCost: false)
    {
        TapChoice = tapCost;
    }
}
