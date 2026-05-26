using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arbor Elf (Lorwyn, {G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "{T}: Untap target Forest."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid at printed cost {G}, owner/controller wired.
/// - <b>Activated ability (CR 602.1)</b>: <c>{T}: Untap target Forest.</c>
///   Cost = <see cref="AdditionalCost.Tap"/>. The chosen target is a 1..1
///   "target Forest" <see cref="TargetRequest"/> (no choose-time legality
///   candidates wired — same posture as Solitude / Snapcaster Mage). On
///   resolution the chosen permanent is re-validated against CR 608.2b
///   (still on the battlefield, still a Land with the Forest subtype) and
///   then <see cref="Permanent.Untap"/>'d. Idempotent on an already-untapped
///   Forest — printed "Untap" is a no-op in that case (CR 701.27).
///
/// ## Untap interaction
/// The ability is a standard activated ability (NOT a mana ability —
/// CR 605.1 reserves "mana ability" status for abilities that produce
/// mana directly; an untap is one indirection removed). However the
/// canonical use is Utopia Sprawl / Wild Growth: tap Arbor Elf, untap an
/// enchanted Forest, then tap that Forest for {G} + the enchantment's
/// bonus mana — the engine simply sequences these as two separate
/// activations.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time target enumeration</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is left empty — the production agent enumerates the live battlefield
///   itself (same posture as Heliod, Sun-Crowned / Solitude). Resolve-time
///   recheck enforces the Forest predicate.
/// </summary>
[CardName("Arbor Elf")]
public static class ArborElfFactory
{
    public const string CardName = "Arbor Elf";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Arbor Elf owned and controlled by <paramref name="owner"/>.
    /// The activated "{T}: Untap target Forest" ability is attached
    /// structurally; the chosen-target is honoured on resolution via
    /// <see cref="ActivatedAbility.ChosenTargets"/> (set by the
    /// production agent or directly by tests).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Untap target Forest (CR 602.1 — activated ability with a
        // tap cost; CR 701.27 — untap is idempotent on an already-untapped
        // permanent). NOT a mana ability — it doesn't produce mana
        // directly (CR 605.1).
        // ----------------------------------------------------------------
        ActivatedAbility? untapAbility = null;
        var untapEffect = new Effect(
            $"{CardName}: untap target Forest",
            () =>
            {
                if (untapAbility == null) return;
                var chosen = untapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still
                // be on the battlefield AND still be a Forest.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Land)) return;
                if (!target.HasSubtype(CardSubtype.Forest)) return;

                // CR 701.27 — untap. Idempotent on already-untapped Forest.
                if (target.IsTapped) target.Untap();
            });

        untapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { untapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target Forest",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(untapAbility);

        return card;
    }
}
