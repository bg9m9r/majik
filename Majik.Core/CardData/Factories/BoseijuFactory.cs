using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boseiju, Who Endures (Kamigawa: Neon Dynasty).
///
/// Legendary Land.
/// Oracle text:
///   "Boseiju, Who Endures enters tapped unless you control two or fewer
///    other lands.
///    {T}: Add {G}.
///    Channel — {1}{G}, Discard Boseiju, Who Endures: Destroy target
///    artifact, enchantment, or nonbasic land an opponent controls. If that
///    permanent was a land, its controller may search their library for a
///    basic land card, put it onto the battlefield, then shuffle."
///
/// ## Implemented (v1)
/// - Legendary Land identity with Legendary supertype
/// - {T}: Add {G} mana ability (<see cref="ManaAbility"/>)
/// - Channel activated ability costs: {1}{G} + Discard self
///   (<see cref="ManaCostCost"/> + <see cref="DiscardSelfCost"/>)
///   Effect: no-op in v1 (target destroy requires ITarget / TargetResolver).
///
/// ## Deferred (v1 gaps)
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control two
///   or fewer other lands" requires a "count permanents of type on ETB"
///   replacement-effect check. Deferred until ETB replacement-effect
///   infrastructure is ready.
/// - <b>Channel effect — target selection</b>: destroying the chosen
///   artifact, enchantment, or nonbasic land requires agent-driven targeting
///   (ITarget / TargetResolver). The effect closure is a no-op in v1;
///   production wiring passes an <paramref name="opponentsResolver"/> and
///   target-selection callback.
/// - <b>Channel effect — basic-land-search follow-up</b>: when the destroyed
///   permanent was a land, the opponent may search their library for a basic
///   land. Deferred entirely (requires library-search + optional prompt).
/// - <b>Tap cost on Channel</b>: Channel abilities are activated from the
///   Hand zone (CR 702.74a); the card is discarded as a cost, so no tap cost
///   applies. This is correct per oracle text. The <see cref="ManaAbility"/>
///   tap-ability is a separate ability that activates from the Battlefield.
/// </summary>
public static class BoseijuFactory
{
    /// <summary>
    /// Construct Boseiju with no target/opponent resolver (test / vanilla path).
    /// The Channel destroy effect is a no-op in this mode.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, opponentsResolver: null);

    /// <summary>
    /// Construct Boseiju with a runtime opponent resolver so the Channel
    /// ability can be wired for production use.
    /// </summary>
    /// <param name="owner">Owner and initial controller of the card.</param>
    /// <param name="opponentsResolver">
    /// Called at Channel resolution time to obtain the list of all players.
    /// May be null — destroy effect is silently skipped in that case.
    /// Full targeting (choose artifact/enchantment/nonbasic land) is still
    /// deferred even with a non-null resolver.
    /// </param>
    public static Land Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            name: "Boseiju, Who Endures",
            supertypes: new[] { CardSupertype.Legendary });

        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {G}
        // Mana ability — does not use the stack (CR 605).
        // Taps a non-basic land for {G}, analogous to Forest except Boseiju
        // does not carry the Basic supertype or Forest subtype.
        // ----------------------------------------------------------------
        var manaAbility = new ManaAbility(land, owner, ManaCost.Parse("G"));
        land.AddAbility(manaAbility);

        // ----------------------------------------------------------------
        // Channel — {1}{G}, Discard ~: Destroy target artifact, enchantment,
        // or nonbasic land an opponent controls.
        //
        // CR 702.74a: Channel is an activated ability that can be activated
        // only while the card is in a player's hand.
        //
        // v1: effect is a no-op stub; see class xmldoc for deferred items.
        // ----------------------------------------------------------------
        var channelEffect = new Effect(
            "Boseiju Channel — destroy target artifact, enchantment, or nonbasic land",
            () =>
            {
                // DEFERRED: target selection + destroy effect.
                // Production code should supply opponentsResolver and
                // hook in ITarget / TargetResolver before invoking.
                // For v1, silently no-op so tests can verify cost shape.
                var _ = opponentsResolver?.Invoke();
            });

        var channelAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse("1G")),
                new DiscardSelfCost(land),
            },
            effects: new IEffect[] { channelEffect });

        land.AddAbility(channelAbility);

        return land;
    }
}
