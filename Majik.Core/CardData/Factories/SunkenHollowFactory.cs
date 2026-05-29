using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunken Hollow (Battle for Zendikar) — a member of
/// the "battle land" / "tango land" nonbasic dual cycle. Oracle text:
///   "({T}: Add {U} or {B}.)
///    This land enters tapped unless you control two or more basic lands."
///
/// <para>
/// The Land shell — both printed land subtypes (Island / Swamp) plus the two
/// mana abilities {U}/{B} (CR 605.1 — mana abilities don't use the stack) — is
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/sunken-hollow.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="CanopyVistaFactory"/>. Mana flows from the printed subtypes the
/// same way every other dual-subtype land's data def does.
/// </para>
///
/// <para>
/// "Enters tapped unless you control two or more basic lands" (CR 614.1c) is
/// wired as a <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. The predicate counts the
/// controller's battlefield lands carrying the Basic supertype
/// (<see cref="ICard.HasSupertype"/>), excluding this land itself by reference
/// equality so the count is correct whether the entering card is on the
/// battlefield at predicate time or not. The land enters untapped iff that
/// count is &gt;= 2.
/// </para>
///
/// <para>
/// Single-arg dispatcher path constructs without a <see cref="ReplacementBus"/>
/// — the ETB-tapped replacement is omitted (shape-only posture matching every
/// other ETB-replacement factory's single-arg path); the mana abilities are
/// still attached. The full overload wires the predicate when the bus is
/// supplied.
/// </para>
/// </summary>
[CardName("Sunken Hollow")]
public static class SunkenHollowFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sunken-hollow");

    /// <summary>Construct Sunken Hollow owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped
    /// replacement wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Sunken Hollow with an optional
    /// <see cref="ReplacementBus"/> for full "enters tapped unless you control
    /// two or more basic lands" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control two or more basic lands
        // (CR 614.1c). Predicate returns true => enters untapped, false =>
        // enters tapped. The card itself is excluded from the count via
        // reference equality so the replacement is correct whether the
        // entering card is on the battlefield at predicate time or not.
        // "basic lands" => lands carrying the Basic supertype (CR 205.4a),
        // not a subtype match — this is what distinguishes the battle-land
        // predicate from the check-land predicate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountControllerBasicLands(controller, self) >= 2));
        }

        return land;
    }

    private static int CountControllerBasicLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self)
                && c.HasType(CardType.Land)
                && c.HasSupertype(CardSupertype.Basic));
}
