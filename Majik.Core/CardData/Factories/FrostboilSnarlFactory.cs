using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frostboil Snarl (Strixhaven: School of Mages) — a
/// member of the "Snarl" reveal-land cycle (Furycalm Snarl, Necroblossom
/// Snarl, Shineshadow Snarl, Vineglimmer Snarl), the Strixhaven successor to
/// the Shadows over Innistrad reveal duals (<see cref="ChokedEstuaryFactory"/>,
/// Port Town, …).
///
/// Oracle text (verified against Scryfall):
/// <code>
/// As this land enters, you may reveal an Island or Mountain card from your hand.
/// If you don't, this land enters tapped.
/// {T}: Add {U} or {R}.
/// </code>
///
/// The type line is a bare <c>Land</c> — no printed land subtype — so the two
/// mana modes (<c>{T}: Add {U} or {R}</c>) are wired explicitly as two painless
/// <see cref="ManaAbility"/> instances from the JSON definition (CR 605.1a —
/// mana abilities don't use the stack), one per produced colour. Structurally
/// identical to <see cref="ChokedEstuaryFactory"/>; only the two consulted
/// hand subtypes (Island / Mountain) and the produced colours ({U} / {R})
/// differ.
///
/// ## Implemented (v1)
/// - <b>Identity + {U}/{R} mana</b> — loaded from
///   <c>Majik.Core/CardData/Cards/frostboil-snarl.json</c> via
///   <see cref="CardDefinitionFactory"/>: a nonbasic <see cref="Land"/> with no
///   printed subtype and two painless mana abilities producing {U} and {R}.
/// - <b>ETB "you may reveal an Island or Mountain; if you don't, it enters
///   tapped" (CR 614.1c)</b> via
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate (enters untapped iff true): the
///   hand match is a card carrying the <see cref="CardSubtype.Island"/> <i>or</i>
///   <see cref="CardSubtype.Mountain"/> subtype (CR 205.4a — a card "is an
///   Island" / "is a Mountain" by subtype):
///     - Requires such a card in the controller's hand (CR 701.16 — "you can't
///       reveal a card you don't have"; with nothing matching, the optional
///       can't be taken, so the land enters tapped and the agent is never
///       prompted).
///     - When a match is present, consults the registered
///       <see cref="IPlayerAgent"/> via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///       with intent <see cref="BotIntent.CardAdvantage"/>. Revealing a card you
///       already hold is pure upside (no cost — it just lets the land enter
///       untapped), so heuristic / scripted bots reveal by default; remote-agent
///       UIs surface the printed question verbatim. On a "yes" the land enters
///       untapped; on "no" / no-agent it enters tapped.
///
/// ## Deferred (v1 gaps)
/// - <b>The reveal itself isn't surfaced as a public event.</b> The predicate
///   only consults the agent's decision and gates tapped-ness; it does not
///   publish a "card revealed" event. Same data-light simplification the other
///   ETB-replacement reveal lands take (see <see cref="ChokedEstuaryFactory"/>).
///   The matching card stays in hand (revealing never moves it).
/// - <b>Single-arg dispatcher path</b> — constructs without a
///   <see cref="ReplacementBus"/>; the reveal-or-tapped replacement is omitted
///   (shape-only posture matching <see cref="ChokedEstuaryFactory"/> and the
///   Check / Shock land cycles). The mana abilities are still attached. Lands
///   enter untapped on this path; the full overload wires the replacement when
///   the bus is supplied.
/// </summary>
[CardName("Frostboil Snarl")]
public static class FrostboilSnarlFactory
{
    public const string CardName = "Frostboil Snarl";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("frostboil-snarl");

    /// <summary>
    /// Construct Frostboil Snarl with no ETB wiring (single-arg dispatcher
    /// path). The reveal-or-tapped replacement is omitted — no
    /// <see cref="ReplacementBus"/> available — matching the reveal / shock /
    /// check land cycle shape-only posture. The {U}/{R} mana modes are still
    /// attached.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Frostboil Snarl. When <paramref name="replacements"/> is
    /// supplied the ETB "you may reveal an Island or Mountain; if you don't, it
    /// enters tapped" replacement is registered (CR 614.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the reveal-or-tapped ETB
    /// replacement is wired.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (bare Land, no subtype) + the two painless {T}: Add {U}/{R}
        // mana abilities come from the JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB: "As this land enters, you may reveal an Island or Mountain card
        // from your hand. If you don't, this land enters tapped." (CR 614.1c)
        //
        // Modelled as a ConditionalEntersTappedReplacement: the predicate
        // returns true (untapped) iff the controller has an Island/Mountain
        // card in hand and the agent elects to reveal it.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, _) => TryRevealIslandOrMountain(controller)));
        }

        return land;
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Honours CR 701.16
    /// ("you can't reveal a card you don't have") by gating on an Island or
    /// Mountain card in hand first (CR 205.4a — match by the Island/Mountain
    /// subtype), then consults the controller's registered agent for the
    /// optional reveal.
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (an Island/Mountain was
    /// revealed). <c>false</c> ⇒ enters tapped (no matching card in hand,
    /// declined, or no agent registered).</returns>
    private static bool TryRevealIslandOrMountain(Player controller)
    {
        // CR 701.16 — nothing to reveal ⇒ the optional can't be taken.
        // Enter tapped without prompting the agent.
        var hasMatch = controller.Zones.Hand.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Island) || c.HasSubtype(CardSubtype.Mountain));
        if (!hasMatch) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent — default to declining (enter tapped), matching the
            // shape-only single-arg dispatcher posture.
            return false;
        }

        try
        {
            // Revealing a card you already hold is pure upside — classify
            // as CardAdvantage so heuristic bots reveal by default.
            return agent.ChooseYesNoAsync(
                question: "Reveal an Island or Mountain card so this land enters untapped?",
                intent: BotIntent.CardAdvantage,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent failure → enter tapped.
            return false;
        }
    }
}
