using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn's Bastion (War of the Spark).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {4}, {T}: Proliferate. (Choose any number of permanents and/or
///    players, then give each another counter of each kind already
///    there.)"
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed supertypes/subtypes — Karn's Bastion
///   is just a "Land", no basic-land subtype).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack). Same shape as
///   <see cref="MutavaultFactory"/>'s mana ability.
/// - <b>{4}, {T}: Proliferate</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{4}</c> plus a tap <see cref="AdditionalCost"/>. Resolution
///   invokes the shared proliferate primitive
///   <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/> — CR 701.27
///   — which walks every known player's battlefield and adds one more
///   counter of an existing kind to each permanent that already has at
///   least one counter.
///
/// ## v1 simplifications (shared with SwordOfTruthAndJustice's proliferate)
/// - <b>"Any number" → "all of them"</b>: agent-driven subset selection
///   is deferred; v1 deterministically proliferates every eligible
///   permanent.
/// - <b>Counter-kind picker</b> falls back to the first kind enumerated
///   by <see cref="Majik.Core.Counters.CounterCollection"/> for multi-kind
///   permanents.
/// - <b>Player counters</b> (poison, energy, experience) — Karn's Bastion
///   inherits the same gap as Sword of Truth and Justice:
///   <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/> walks only
///   the source's controller's battlefield (no opponent or player
///   counters) until a <c>Game</c> reference is plumbed through.
/// </summary>
[CardName("Karn's Bastion")]
public static class KarnsBastionFactory
{
    public const string CardName = "Karn's Bastion";

    /// <summary>
    /// Construct Karn's Bastion as a plain Land. The mana ability and the
    /// {4}, {T}: Proliferate activated ability are both attached for
    /// shape; the activated ability's resolution delegates to the
    /// existing proliferate primitive.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {4}, {T}: Proliferate.
        // CR 602 — ordinary activated ability (uses the stack); the cost
        // is {4} mana plus the tap symbol. Resolution invokes the shared
        // proliferate primitive — CR 701.27.
        // ----------------------------------------------------------------
        var proliferateEffect = new Effect(
            $"{CardName}: proliferate (CR 701.27)",
            () => SwordOfTruthAndJusticeFactory.Proliferate(owner));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { proliferateEffect }));

        return land;
    }
}
