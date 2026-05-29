using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Witch Enchanter // Witch-Blessed Meadow (Wilds of Eldraine).
///
/// Land. Oracle text (back):
///   "As this land enters, you may pay 3 life. If you don't, it enters
///    tapped."
///   "{T}: Add {W}."
///
/// Front face — <see cref="WitchEnchanterFactory"/> (Creature — Human
/// Warlock {3}{W} 2/2 with an ETB "destroy target artifact or enchantment
/// an opponent controls" trigger).
///
/// ## MDFC infra
/// See <see cref="WitchEnchanterFactory"/>'s class doc for the cast-either-
/// face design. This factory is the back-face dispatch arm: when a player
/// chooses to play the MDFC as a land, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Witch-Blessed Meadow"</c> and lands
/// here. The card is constructed with its <see cref="MdfcState"/> pre-flipped
/// to the back face so the face tracker reads as authoritative.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="RazorgrassFieldFactory"/>'s back-face posture).
/// - <b>{T}: Add {W}</b> — single <see cref="ManaAbility"/> producing one
///   white mana (CR 605.1 — mana ability, no stack).
/// - <b>ETB "you may pay 3 life; if you don't, it enters tapped"
///   (CR 614.1c)</b> — modelled via <see cref="ConditionalEntersTappedReplacement"/>
///   on the supplied <see cref="ReplacementBus"/>. Predicate mirrors
///   <see cref="RazorgrassFieldFactory"/>:
///     <list type="bullet">
///       <item>CR 119.4 — life below 3 → no prompt; enters tapped.</item>
///       <item>Otherwise consult the registered <see cref="IPlayerAgent"/>
///         via <see cref="IPlayerAgent.ChooseYesNoAsync"/> with intent
///         <see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.CostToDecline"/>.
///         On "yes" the controller loses 3 life (CR 118.8) and the land
///         enters untapped. On "no" / no-agent / agent-throw the land enters
///         tapped.</item>
///     </list>
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture).
///
/// ## References
/// - <see cref="RazorgrassFieldFactory"/> — identical painland-3 + {T}: Add {W}
///   shape; this factory directly mirrors it for the Witch Enchanter pair.
/// - <see cref="SoporificSpringsFactory"/> — same ETB predicate shape ({U}).
/// </summary>
[CardName("Witch-Blessed Meadow")]
public static class WitchBlessedMeadowFactory
{
    public const string CardName = "Witch-Blessed Meadow";
    public const string FrontName = "Witch Enchanter";

    /// <summary>
    /// Construct Witch-Blessed Meadow without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped predicate is omitted; the {T}: Add {W} mana ability
    /// is still wired. Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Witch-Blessed Meadow with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "you may pay 3 life;
    /// if you don't, it enters tapped" replacement is registered
    /// (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Witch-Blessed Meadow is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Witch-Blessed Meadow is the back face that actually
        // exists on the battlefield). Mirrors RazorgrassFieldFactory.
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "As this land enters, you may pay 3 life. If you don't, it
        // enters tapped." (CR 614.1c)
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    TryPayThreeLifeOrEnterTapped(controller, self)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {W}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        return land;
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Honours CR 119.4
    /// ("you can't pay life you don't have"), consults the controller's
    /// registered agent for the optional payment, and deducts 3 life on
    /// the yes path.
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (life was paid).
    /// <c>false</c> ⇒ enters tapped (declined, no agent, or insufficient
    /// life).</returns>
    private static bool TryPayThreeLifeOrEnterTapped(Player controller, ICard self)
    {
        _ = self;

        // CR 119.4 — life floor at exactly 3 (a payment that brings the
        // total to 0 is legal — SBAs handle the loss-of-game afterward).
        if (controller.LifeTotal < 3) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent registered — decline (same default-decline posture
            // as RazorgrassFieldFactory / SoporificSpringsFactory).
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: "Pay 3 life so Witch-Blessed Meadow enters untapped?",
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

        // CR 118.8 — pay 3 life.
        controller.LoseLife(3);
        return true;
    }
}
