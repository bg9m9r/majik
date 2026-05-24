using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mox Opal (Scars of Mirrodin).
///
/// Legendary Artifact — {0}.
/// Oracle text:
///   "Metalcraft — {T}: Add one mana of any color. Activate only if you
///    control three or more artifacts."
///
/// ## Implementation (v1)
/// - Card identity: Legendary Artifact with mana cost {0}.
/// - "Add one mana of any color" is modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG) — same shape as
///   <see cref="DelightedHalflingFactory"/>, City of Brass, etc. The
///   <see cref="OracleManaBinder"/> already expands this oracle phrase
///   into five abilities at bind time; the factory mirrors that shape
///   inline so the Metalcraft gate can be wired without round-tripping
///   through the binder (which would attach ungated abilities).
/// - Metalcraft gate (CR 702.95): each ManaAbility's <c>canActivateCheck</c>
///   ANDs <c>!IsTapped</c> with a live count of artifact permanents the
///   controller controls (>= 3). Mox Opal itself is an artifact, so the
///   self-count contributes (3-card baseline = Mox Opal + 2 other
///   artifacts). Opponent artifacts do NOT count — the scan is scoped to
///   the controller's battlefield zone.
///
/// ## Deferred (v1 gaps)
/// - "Mana of any color" is bound as five separate ManaAbility instances;
///   the bot's source-picker selects the right colour at payment time.
///   A single modal-colour ManaAbility (single ability, choose colour at
///   activation) is not in the engine yet — same pattern as Delighted
///   Halfling / City of Brass.
/// </summary>
[CardName("Mox Opal")]
public static class MoxOpalFactory
{
    public const string CardName = "Mox Opal";
    public const string Cost = "{0}";

    /// <summary>
    /// Construct Mox Opal owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mox = new Artifact(
            CardName,
            Cost,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        mox.SetOwner(owner);
        mox.SetController(owner);

        // --------------------------------------------------------------
        // Metalcraft — {T}: Add one mana of any color.
        // Five ManaAbility instances, each gated on:
        //   (1) Mox Opal is untapped, AND
        //   (2) controller controls >= 3 artifacts (CR 702.95).
        // The gate is evaluated against the LIVE controller (mox.Controller)
        // so control-change effects are honoured.
        // --------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            mox.AddAbility(new ManaAbility(
                source: mox,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !mox.IsTapped && MetalcraftActive(mox)));
        }

        return mox;
    }

    /// <summary>
    /// CR 702.95 — Metalcraft is active for an object's controller when
    /// they control three or more artifacts. Counts every artifact-type
    /// permanent on the controller's battlefield (Mox Opal itself
    /// included when it is on the battlefield).
    /// </summary>
    private static bool MetalcraftActive(Artifact mox)
    {
        var controller = mox.Controller;
        if (controller == null) return false;

        var count = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card.HasType(CardType.Artifact))
            {
                count++;
                if (count >= 3) return true;
            }
        }
        return count >= 3;
    }
}
