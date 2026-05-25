using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mind Stone (Weatherlight, {2}).
///
/// Artifact. Oracle text:
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice Mind Stone: Draw a card."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1).
///   <see cref="ManaCost.Parse"/> folds {C} into the generic bucket per
///   CR 107.4c (see <c>ManaCost.cs:170</c>). Same shape as Sol Ring /
///   Mana Crypt's tap-for-colourless body.
/// - <b>{1}, {T}, Sacrifice Mind Stone: Draw a card</b> — a sorcery-shape
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{1}") for the mana pip,
///   <see cref="AdditionalCost.Tap"/> on Mind Stone, and
///   <see cref="AdditionalCost.Sacrifice"/> on Mind Stone itself.
///   Resolution sacrifices the stone (battlefield → owner's graveyard) and
///   draws one card via <see cref="Fx.DrawCards"/>. Empty library is a
///   silent no-op (SBAs handle the loss flag via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Pyrite Spellbomb / Aether Spellbomb /
///   Lotus Petal. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
[CardName("Mind Stone")]
public static class MindStoneFactory
{
    public const string CardName = "Mind Stone";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Mind Stone owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var stone = new Artifact(CardName, PrintedManaCost);
        stone.SetOwner(owner);
        stone.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}.
        // CR 605.1 — mana abilities don't use the stack. {C} folds into
        // the generic bucket via ManaCost.Parse (see ManaCost.cs:170).
        // ----------------------------------------------------------------
        stone.AddAbility(new ManaAbility(stone, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice Mind Stone: Draw a card.
        // CR 602 — activated ability with three costs (mana pip, tap, sac).
        // Empty library marks the loss flag via Fx.DrawCards (CR 120.3 /
        // 704.5b) without throwing.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Mind Stone: draw a card + sac self",
            () =>
            {
                SacrificeSelf(stone, owner);
                Fx.DrawCards(owner, 1);
            });

        var drawAbility = new ActivatedAbility(
            source: stone,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(stone),
                AdditionalCost.Sacrifice(stone),
            },
            effects: new IEffect[] { drawEffect });

        stone.AddAbility(drawAbility);

        return stone;
    }

    /// <summary>
    /// Move <paramref name="stone"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield.
    /// Mirrors the closure used by Pyrite Spellbomb / Aether Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact stone, Player owner)
    {
        if (stone.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(stone);
        owner.Zones.Graveyard.AddCard(stone);
        stone.SetZone(ZoneType.Graveyard);
    }
}
