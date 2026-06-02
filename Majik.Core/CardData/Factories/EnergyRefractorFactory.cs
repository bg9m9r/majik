using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Energy Refractor (Edge of Eternities, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-01):
///   "When this artifact enters, draw a card.
///    {2}: Add one mana of any color."
///
/// A cantrip mana rock. The ETB cantrip mirrors
/// <see cref="PropheticPrismFactory"/> / Chromatic Star / Mishra's Bauble.
/// The colour-fixing ability is the any-colour twin of those prisms, but the
/// printed activation cost is a flat {2} with NO {T} component — so the
/// refractor stays untapped and can be activated repeatedly as long as the
/// controller can pay {2} (mirrors the "no-tap" shape of
/// <see cref="WallOfRootsFactory"/> / <see cref="PentadPrismFactory"/>).
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring) and
///   the <b>"When this artifact enters, draw a card"</b> trigger — both
///   built from the embedded JSON definition
///   (<c>Majik.Core/CardData/Cards/energy-refractor.json</c>) via
///   <see cref="CardDefinitionFactory"/>. The trigger is a single
///   <see cref="TriggeredAbility"/> on the <c>etb_self</c> condition
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>) carrying a
///   <c>draw_card</c> effect (amount 1). CR 603.6 — an enters-the-battlefield
///   trigger; it uses the stack. CR 120.2 — its controller draws one card on
///   resolution. Empty library is a silent no-op in the effect closure; the
///   loss is handled by SBAs elsewhere (CR 104.3c / 704.5c).
/// - <b>{2}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), same modal-colour
///   shape as <see cref="PropheticPrismFactory"/> / Chromatic Star /
///   Lotus Petal. Each uses the no-{T} mana-ability overload
///   (<c>tapsAsCost: false</c>, the Wall of Roots / Pentad Prism shape):
///   the printed cost is a flat {2}, NOT {T}, so the refractor stays
///   untapped and can be activated as many times as the controller can pay
///   {2}.
///     - <c>canActivateCheck</c> = <c>Zone == Battlefield AND
///       ManaPool.CanPay({2})</c> (CR 605.3a — the cost must be payable).
///     - <c>additionalCostPayer</c> spends {2} from the pool inline
///       (CR 602.1 — the cost is paid up front in the same atomic step as
///       the mana production; mana abilities don't use the stack, CR 605.3b).
///   The {2} mana abilities are NOT modeled in the JSON because the JSON
///   <c>mana</c> ability encoding always implies a {T} component (it routes
///   through the untapped-gated / tapping <see cref="ManaAbility"/>
///   constructor), which would tap the refractor and forbid repeat
///   activation — incorrect for Energy Refractor's printed wording.
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color"
///   is bound as five separate <see cref="ManaAbility"/> instances — the
///   bot's source-picker selects the right colour at payment time. Same
///   posture as Prophetic Prism / Chromatic Star / Lotus Petal / Mox Opal.
/// </summary>
[CardName("Energy Refractor")]
public static class EnergyRefractorFactory
{
    public const string CardName = "Energy Refractor";
    public const string Slug = "energy-refractor";

    /// <summary>The {2} flat activation cost of the any-colour ability.</summary>
    private static readonly ManaCost ActivationCost = ManaCost.Parse("2");

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Energy Refractor owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Card identity + "When this artifact enters, draw a card" come from
        // the embedded JSON definition (etb_self -> draw_card).
        var refractor = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {2}: Add one mana of any color. (CR 605.1 — mana ability;
        // CR 605.3b — doesn't use the stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Prophetic Prism / Chromatic Star / Lotus Petal. The
        // activation cost is a flat {2}, NOT {T}, so we use the no-tap
        // overload (tapsAsCost: false, the Wall of Roots / Pentad Prism
        // shape). Each is gated on:
        //   (1) the refractor is still on the battlefield, AND
        //   (2) the controller can pay {2} (CR 605.3a — the cost must be
        //       payable).
        // The additionalCostPayer spends {2} from the pool inline.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            refractor.AddAbility(new ManaAbility(
                source: refractor,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => refractor.Zone == ZoneType.Battlefield
                                        && owner.ManaPool.CanPay(ActivationCost),
                additionalCostPayer: p => p.PayMana(ActivationCost),
                tapsAsCost: false));
        }

        return refractor;
    }
}
