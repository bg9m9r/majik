using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wild Cantor (Planar Chaos, {R/G}).
///
/// Creature — Human Druid 1/1. Oracle text:
///   "({R/G} can be paid with either {R} or {G}.)
///    Sacrifice this creature: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Card identity (Creature — Human Druid 1/1 {R/G}) is loaded from
///   <c>Majik.Core/CardData/Cards/wild-cantor.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
///   through <see cref="CardDefinitionFactory"/> — same data-driven
///   identity route as <see cref="SimianSpiritGuideFactory"/>. The hybrid
///   <c>{R/G}</c> mana cost is parsed by the standard cost reader (CR 107.4f
///   / 202.2f — a monocolored hybrid symbol payable with either colour),
///   the same as Dryad Militant's <c>{G/W}</c>.
/// - "Sacrifice this creature: Add one mana of any color" is a mana ability
///   (CR 605.1a — it could add mana, has no target, and doesn't use the
///   stack). It is attached in C# rather than via the JSON ability schema
///   because the JSON <c>"kind": "mana"</c> shape only models a battlefield
///   <c>{T}</c> ability — it has no representation for a sacrifice activation
///   cost. Same reason Lotus Petal / Simian Spirit Guide hand-build their
///   sacrifice/exile-rider mana abilities in C#.
/// - "Add one mana of any color" is modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG) — same shape as
///   <see cref="LotusPetalFactory"/>. The bot's source-picker selects the
///   colour at payment time.
/// - The activation cost is "Sacrifice this creature" alone — there is NO
///   <c>{T}</c> — so each ability uses the no-tap overload
///   (<c>tapsAsCost: false</c>, the same overload Simian Spirit Guide uses).
///   <c>canActivateCheck</c> gates on the creature still being on the
///   battlefield so the ability can only be activated once; the
///   <c>additionalCostPayer</c> performs the sacrifice (CR 701.16) inline,
///   moving the creature from its controller's battlefield to its owner's
///   graveyard. CR 605.1 keeps the activation off the stack despite the
///   non-{T} cost.
///
/// ## Deferred (v1 gaps)
/// - "Mana of any color" is bound as five separate ManaAbility instances;
///   a single modal-colour ManaAbility (choose colour at activation) is not
///   in the engine yet — same posture as Lotus Petal / Mox Opal / City of
///   Brass.
/// - Sacrifice payment is performed inline by the local closure (same
///   posture as <see cref="LotusPetalFactory"/>) rather than through a
///   first-class sacrifice-cost primitive.
/// </summary>
[CardName("Wild Cantor")]
public static class WildCantorFactory
{
    public const string CardName = "Wild Cantor";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("wild-cantor");

    /// <summary>
    /// Construct Wild Cantor owned and controlled by <paramref name="owner"/>.
    /// Identity comes from the embedded JSON; the sacrifice-for-any-color
    /// mana abilities are attached here (see class remarks for why they
    /// can't be JSON-driven).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var cantor = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Sacrifice this creature: Add one mana of any color.
        // Five ManaAbility instances, one per WUBRG. Each is gated on Wild
        // Cantor still being on the battlefield (i.e. not yet sacrificed by
        // a sibling activation). The additionalCostPayer performs the
        // sacrifice (CR 701.16) inline. No-tap overload (tapsAsCost: false)
        // — the printed cost is the sacrifice alone, there is no {T}.
        // CR 605.1a keeps the activation off the stack (it adds mana, has no
        // target).
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            cantor.AddAbility(new ManaAbility(
                source: cantor,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => cantor.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => SacrificeCantor(cantor),
                tapsAsCost: false));
        }

        return cantor;
    }

    /// <summary>
    /// CR 701.16 — sacrifice: the controller moves their permanent from the
    /// battlefield to its owner's graveyard. Idempotent: if Wild Cantor has
    /// already been moved (defensive — shouldn't happen given the
    /// canActivateCheck gate) we no-op. Mirrors
    /// <see cref="LotusPetalFactory"/>'s inline-cost closure.
    /// </summary>
    private static void SacrificeCantor(Creature cantor)
    {
        if (cantor.Zone != ZoneType.Battlefield) return;

        var controller = cantor.Controller;
        var owner = cantor.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Battlefield.RemoveCard(cantor);
        owner.Zones.Graveyard.AddCard(cantor);
        cantor.SetZone(ZoneType.Graveyard);
    }
}
