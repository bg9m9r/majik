using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Springleaf Drum (Mirrodin / Modern Horizons, {1}).
///
/// Artifact. Oracle text:
///   "{T}, Tap an untapped creature you control: Add one mana of any color."
///
/// ## Implementation
///
/// One <see cref="ManaAbility"/> per WUBRG colour (5 in total), each
/// wired through the standard "tap-self plus additional cost" overload:
///   - <c>{T}</c> on the drum itself is the implicit self-tap baked into
///     <see cref="ManaAbility"/>'s default ctor path.
///   - The "tap an untapped creature you control" component is a
///     <see cref="TapAnotherUntappedCreatureCost"/> consulted by
///     <c>canActivateCheck</c> + executed by <c>additionalCostPayer</c>.
///
/// Five parallel <see cref="ManaAbility"/> instances (sibling shape to
/// Cavern of Souls / Aether Hub / Delighted Halfling) — the engine treats
/// each colour as a distinct mana-ability slot so the bot / agent can
/// pick the colour at activation time. CR 605.1 — mana abilities don't
/// use the stack; the creature-tap cost is paid concurrently with the
/// drum's self-tap.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> factory is the only
/// surface; the abilities are attached to the card and ready to fire as
/// soon as the drum is on the battlefield. No
/// <see cref="Majik.Core.Services.TriggerManager"/> or
/// <see cref="Majik.Core.Services.ContinuousEffectsService"/> wiring is
/// required (no triggers, no continuous effects).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent prompt for which creature to tap</b> — the cost falls back
///   to the first eligible (untapped, no summoning sickness, not the drum
///   itself) creature on the controller's battlefield via
///   <see cref="TapAnotherUntappedCreatureCost"/>'s deterministic pick.
///   Agents can pre-set <see cref="TapAnotherUntappedCreatureCost.Target"/>
///   to override.
/// - <b>Agent prompt for which colour to add</b> — covered by the
///   per-colour ability shape: the activator picks the colour by picking
///   the matching ability slot, no separate prompt needed.
/// </summary>
[CardName("Springleaf Drum")]
public static class SpringleafDrumFactory
{
    public const string CardName = "Springleaf Drum";
    public const string Cost = "{1}";

    /// <summary>
    /// Construct Springleaf Drum owned and controlled by
    /// <paramref name="owner"/> with all five colour mana abilities
    /// attached.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: null);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Five colour-specific ManaAbility slots — WUBRG. Each is the
        // additional-cost overload so the self-tap and creature-tap are
        // paid concurrently (CR 605.1).
        // ----------------------------------------------------------------
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(BuildAnyColorAbility(card, owner, pip));
        }

        return card;
    }

    /// <summary>
    /// Build one colour's <see cref="ManaAbility"/> slot. Exposed for
    /// tests that need to inspect or activate a specific colour.
    /// </summary>
    public static SpringleafDrumManaAbility BuildAnyColorAbility(
        Permanent source, Player controller, string colorPip)
    {
        var tapCost = new TapAnotherUntappedCreatureCost(source);
        return new SpringleafDrumManaAbility(source, controller, colorPip, tapCost);
    }
}

/// <summary>
/// Springleaf Drum's per-colour mana ability. Subclasses
/// <see cref="ManaAbility"/> so the embedded
/// <see cref="TapAnotherUntappedCreatureCost"/> is reachable from outside
/// (agents / tests) for target-setting — same shape as
/// <see cref="PhyrexianTowerManaAbility"/>'s sacrifice cost.
/// </summary>
public sealed class SpringleafDrumManaAbility : ManaAbility
{
    /// <summary>
    /// Colour pip this ability produces (one of W / U / B / R / G).
    /// </summary>
    public string ColorPip { get; }

    /// <summary>
    /// The creature-tap cost paid as part of activating this ability.
    /// Set <see cref="TapAnotherUntappedCreatureCost.Target"/> before
    /// <see cref="ManaAbility.Activate"/> to pick a specific creature;
    /// otherwise the cost falls back to its deterministic first-eligible
    /// pick.
    /// </summary>
    public TapAnotherUntappedCreatureCost TapChoice { get; }

    internal SpringleafDrumManaAbility(
        Permanent source,
        Player controller,
        string colorPip,
        TapAnotherUntappedCreatureCost tapCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(colorPip),
            canActivateCheck: () => source is Permanent p
                && !p.IsTapped
                && tapCost.CanPay(controller),
            additionalCostPayer: p => tapCost.Pay(p))
    {
        ColorPip = colorPip;
        TapChoice = tapCost;
    }
}
