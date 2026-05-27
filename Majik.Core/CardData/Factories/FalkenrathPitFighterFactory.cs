using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Falkenrath Pit Fighter (Innistrad: Crimson Vow,
/// {R}).
///
/// Creature — Vampire Berserker 1/1. Oracle text:
///   "Trample
///    Haste
///    {R}, Sacrifice another creature or Blood token: Draw a card. Activate
///    only as a sorcery."
///
/// ## Implemented (v1)
///
/// - 1/1 Vampire Berserker with mana cost {R}, owner / controller stamped.
/// - <see cref="KeywordAbility"/> markers for Trample (CR 702.19) and Haste
///   (CR 702.10), read by <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>Activated ability (CR 602.1)</b>: <see cref="ActivatedAbility"/>
///   with the printed sorcery-speed rider
///   (<see cref="ActivatedAbility.IsSorcerySpeed"/> = true so
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects activation
///   outside the controller's main phase / when the stack is non-empty,
///   CR 117.1a / CR 307.5).
///   Costs in declaration order:
///   <list type="number">
///     <item><see cref="ManaCostCost"/> {R} — printed mana cost.</item>
///     <item><see cref="SacrificeAnotherCreatureOrBloodTokenCost"/> —
///       sacrifices any non-source creature OR any Blood-subtype token
///       (CR 205.3i / CR 111.10). Pairs naturally with
///       <see cref="VoldarenEpicureFactory"/>'s Blood production for a
///       Crimson Vow loot engine.</item>
///   </list>
///   Single effect: draw one card from the top of the controller's
///   library (CR 121.1). Empty library flags the SBA loss via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b,
///   same posture as <see cref="InsolentNeonateFactory"/> /
///   <see cref="TokenFactory.CreateBlood"/>).
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. Mana → sacrifice → draw at the
/// implementation level; the cost surface keeps the atomicity contract
/// (legality checked before any payment).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice-target prompt</b>: the cost has a deterministic v1
///   picker (prefer Blood token, then first non-source creature). Real
///   agent-driven "choose what to sacrifice" prompt waits on the shared
///   sacrifice-prompt surface (same gap as Goblin Bombardment / Skirk
///   Prospector). The cost's <see cref="SacrificeAnotherCreatureOrBloodTokenCost.Target"/>
///   property accepts an agent override.
/// </summary>
[CardName("Falkenrath Pit Fighter")]
public static class FalkenrathPitFighterFactory
{
    public const string CardName = "Falkenrath Pit Fighter";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Falkenrath Pit Fighter owned and controlled by
    /// <paramref name="owner"/>. The Trample + Haste keyword markers are
    /// attached and the sorcery-speed activated ability is fully self-
    /// contained — no service wiring required (no event bus, no trigger
    /// manager, no zone service). Same self-contained shape as
    /// <see cref="InsolentNeonateFactory"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Berserker });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. CombatAbilities.HasTrample reads the marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 702.10 — Haste. CombatAbilities.HasHaste reads the marker.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // {R}, Sacrifice another creature or Blood token: Draw a card.
        // Activate only as a sorcery.
        // CR 602.1 + CR 117.1a / 307.5 (sorcery-speed rider).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                // CR 121.1 — draw one card from the top of the controller's
                // library. Empty library flags the SBA loss via
                // MarkTriedToDrawFromEmptyLibrary (CR 704.5b, same handling
                // as Insolent Neonate / Faithless Looting).
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse("R")),
                new SacrificeAnotherCreatureOrBloodTokenCost(card),
            },
            effects: new IEffect[] { drawEffect },
            sorcerySpeed: true);

        card.AddAbility(drawAbility);

        return card;
    }
}
