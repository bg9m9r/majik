using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Hydroelectric Specimen // Hydroelectric Laboratory (Modern Horizons 3).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "As this land enters, you may pay 3 life. If you don't, it enters tapped."
///   "{T}: Add {U}."
///
/// Front face — <see cref="HydroelectricSpecimenFactory"/> (Creature — Weird
/// {2}{U} 1/4 with Flash + an ETB "change the target of target instant or
/// sorcery spell with a single target to this creature").
///
/// ## MDFC infra
///
/// See <see cref="HydroelectricSpecimenFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm: when a
/// player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Hydroelectric Laboratory"</c> and lands here. The card is constructed
/// with its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative. Mirrors <see cref="JwariRuinsFactory"/>
/// (the structurally identical ZNR blue MDFC back-face tapland) except the
/// ETB is the SHOCK-style "pay 3 life or enter tapped" condition rather than
/// Jwari Ruins' unconditional enters-tapped.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {U}</b> mana ability are loaded from the
/// embedded JSON definition (<c>hydroelectric-laboratory.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// conditional pay-3-life ETB replacement are attached in code (the JSON
/// schema models neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {U}</b> — single <see cref="Abilities.ManaAbility"/>
///   producing one blue mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." (CR 614.1c)</b> — modelled via
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>, the SAME shape as the Ravnica shock-land
///   cycle (<see cref="ShockLandCycleFactory"/>) except the life cost is 3
///   instead of 2. The predicate consults the controller's registered
///   <see cref="IPlayerAgent"/>, honours CR 119.4 ("you can't pay life you
///   don't have"), and on a "yes" deducts 3 life via
///   <see cref="Player.LoseLife"/> (CR 118.8) so SBA / combat listeners
///   observe it.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired — the
///   ETB replacement is omitted (shape-only posture); the {T}: Add {U} mana
///   ability is still attached.
///
/// ## References
///
/// - <see cref="JwariRuinsFactory"/> — the structurally identical ZNR blue
///   MDFC back-face tapland this directly mirrors (modulo the pay-3-life
///   condition).
/// - <see cref="ShockLandCycleFactory"/> — the pay-life-or-tapped predicate
///   shape this back face reuses (life cost 3 instead of 2).
/// </summary>
[CardName("Hydroelectric Laboratory")]
public static class HydroelectricLaboratoryFactory
{
    public const string CardName = "Hydroelectric Laboratory";
    public const string FrontName = "Hydroelectric Specimen";
    public const string Slug = "hydroelectric-laboratory";

    /// <summary>CR 118.8 — the optional life payment for entering untapped.</summary>
    public const int LifeCost = 3;

    /// <summary>
    /// Construct Hydroelectric Laboratory without a <see cref="ReplacementBus"/>.
    /// The pay-3-life-or-tapped replacement is omitted; the {T}: Add {U} mana
    /// ability (from JSON) is still wired. Suitable for card-shape / dispatcher
    /// tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Hydroelectric Laboratory with an optional
    /// <see cref="ReplacementBus"/> for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "you may pay 3 life; if you
    /// don't, it enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {U} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Hydroelectric Laboratory is the back face that actually exists
        // on the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "As this land enters, you may pay 3 life. If you don't, it
        // enters tapped." (CR 614.1c). Modelled as a single
        // ConditionalEntersTappedReplacement: the predicate returns true
        // (untapped) iff the controller can pay and elects to pay 3 life —
        // deducting the life as a side-effect on the yes path. Returning
        // false flips the intent's EntersTapped to true. Registered only
        // when a bus is supplied.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    TryPayLifeOrEnterTapped(controller, self)));
        }

        return land;
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Consults the controller's
    /// registered agent for the pay-3-life optional, honours CR 119.4 ("you
    /// can't pay life you don't have"), and deducts the 3 life as a
    /// side-effect on the yes path. Mirrors
    /// <see cref="ShockLandCycleFactory"/>'s <c>TryPayTwoLifeOrEnterTapped</c>
    /// (life cost 3 instead of 2).
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (life was paid).
    /// <c>false</c> ⇒ enters tapped (declined, no agent, or insufficient
    /// life).</returns>
    private static bool TryPayLifeOrEnterTapped(Player controller, ICard self)
    {
        _ = self;

        // CR 119.4 — you can't pay life you don't have. With life total at or
        // above the cost the payment is legal (dropping to 0 is allowed for a
        // life payment — SBAs handle the loss afterward).
        if (controller.LifeTotal < LifeCost) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent registered — default to declining the optional payment
            // so the land enters tapped. Matches the shape-only posture of the
            // single-arg dispatcher path and the legacy auto-tapped default.
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: $"Pay {LifeCost} life so {CardName} enters untapped?",
                intent: BotIntent.LoseLife | BotIntent.CostToDecline,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent failure → fall back to entering tapped.
            return false;
        }

        if (!wantsToPay) return false;

        // CR 118.8 — pay the life. Run through Player.LoseLife so combat / SBA
        // listeners observe the life change.
        controller.LoseLife(LifeCost);
        return true;
    }
}
