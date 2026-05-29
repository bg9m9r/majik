using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mana Confluence (Journey into Nyx). Oracle text
/// (verified against Scryfall):
///   "{T}, Pay 1 life: Add one mana of any color."
///
/// <para>
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/mana-confluence.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ZagothTriomeFactory"/>. The "any color" mana abilities are
/// attached on top in C# because the data-only
/// <see cref="ManaAbilityDefinition"/> schema only carries a
/// <c>Produces</c> string — it cannot express the "Pay 1 life" additional
/// activation cost nor the five-colour any-colour fan-out. The JSON
/// therefore declares no mana abilities; this factory adds them.
/// </para>
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) via JSON.
/// - <b>{T}, Pay 1 life: Add one mana of any color.</b> — modelled as five
///   <see cref="ManaAbility"/> instances, one per WUBRG (same any-colour
///   fan-out as Aether Hub's coloured modes and Cavern of Souls). Unlike
///   Aether Hub / the pain lands there is NO <c>{C}</c> mode. Each ability
///   is built via the additional-cost overload of <see cref="ManaAbility"/>:
///   <c>additionalCostPayer = controller.LoseLife(1)</c> (CR 120.6 — "Pay N
///   life"), running after the {T} tap. The mana picker chooses whichever
///   colour is needed when paying spell costs.
/// - <b>Life-floor gate (CR 119.4)</b> — "Pay 1 life" is a cost you can't
///   pay if you don't have the life. The <c>canActivateCheck</c> requires
///   the land untapped AND <c>controller.LifeTotal &gt;= 1</c>: legal at 1
///   life (drops to 0, which is not "below 0"), illegal at 0 or less. This
///   is the key distinction from the pain lands
///   (<see cref="PainLandCycleFactory"/>), which deal damage (CR 120.3) and
///   carry no life-floor gate — they can drop you to 0 or below.
///
///   NOTE: <see cref="HorizonLandBinder.AttachPayLifeMana"/> uses a stricter
///   <c>LifeTotal &gt; 1</c> gate (forbids activation at exactly 1 life).
///   Mana Confluence uses <c>&gt;= 1</c> here, the precise CR 119.4 reading,
///   so it is not reused — the floor differs.
///
/// ## Deferred (v1 gaps)
/// - Full <c>LifeChangedEvent</c> nuance: the life payment goes through
///   <see cref="Player.LoseLife"/>, the same path every other "Pay N life"
///   cost in the engine uses.
/// </summary>
[CardName("Mana Confluence")]
public static class ManaConfluenceFactory
{
    public const string CardName = "Mana Confluence";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mana-confluence");

    /// <summary>Construct Mana Confluence owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}, Pay 1 life: Add one mana of any color.
        //   Five ManaAbility instances (one per WUBRG) — same any-colour
        //   fan-out as Aether Hub / Cavern of Souls. Each carries:
        //     - canActivateCheck: land untapped AND controller has >= 1 life
        //       (CR 119.4 — you can't pay life you don't have; the floor is
        //       life > 0, so 1 life is payable and drops to 0).
        //     - additionalCostPayer: lose 1 life (CR 120.6 — "Pay N life")
        //       after the tap pays {T}.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () =>
                {
                    if (land.IsTapped) return false;
                    var controller = land.Controller ?? owner;
                    return controller.LifeTotal >= 1;
                },
                additionalCostPayer: controller => controller.LoseLife(1)));
        }

        return land;
    }
}
