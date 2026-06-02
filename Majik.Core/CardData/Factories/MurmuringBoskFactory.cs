using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murmuring Bosk (Lorwyn) — the reveal-a-Treefolk
/// painland.
///
/// Oracle text (verified against Scryfall):
/// <code>
/// ({T}: Add {G}.)
/// As this land enters, you may reveal a Treefolk card from your hand. If you
/// don't, this land enters tapped.
/// {T}: Add {W} or {B}. This land deals 1 damage to you.
/// </code>
///
/// Type line is <c>Land — Forest</c> (the printed Forest subtype, like the
/// Lorwyn "reveal" dual cycle — Gilt-Leaf Palace, Wanderwine Hub, …).
///
/// ## Implemented (v1)
/// - <b>Identity + painless {G}</b> — loaded from
///   <c>Majik.Core/CardData/Cards/murmuring-bosk.json</c> via
///   <see cref="CardDefinitionFactory"/>: a <see cref="Land"/> with the Forest
///   subtype and a single painless <see cref="ManaAbility"/> producing {G}
///   (CR 605.1a — mana abilities don't use the stack). The Forest subtype
///   does <i>not</i> auto-attach an intrinsic {G} mana ability in this engine
///   (printed oracle abilities are the source of truth — see
///   <see cref="ShockLandCycleFactory"/>'s dual-subtype note), so the {G}
///   mode is wired explicitly from the JSON, not from the subtype.
/// - <b>{T}: Add {W} or {B}. This land deals 1 damage to you.</b> — the
///   coloured "Add {W} or {B}" modal is split into two pain
///   <see cref="ManaAbility"/> instances (one per produced colour), exactly
///   like <see cref="PainLandCycleFactory"/>'s coloured modes. Each is built
///   via the additional-cost overload of <see cref="ManaAbility"/>:
///   <c>additionalCostPayer = controller.LoseLife(1)</c> running after the
///   {T} tap (CR 120.3 — damage to a player reduces life by that amount).
///   CR 119.4 does NOT gate this damage — the painland can drop you to 0 or
///   below (SBAs handle the loss afterward), so the activation has no
///   life-floor check, unlike a "Pay 1 life" cost.
/// - <b>ETB "you may reveal a Treefolk; if you don't, it enters tapped"
///   (CR 614.1c)</b> via <see cref="ConditionalEntersTappedReplacement"/> on
///   the supplied <see cref="ReplacementBus"/>. Predicate (enters untapped
///   iff true):
///     - Requires a Treefolk card in the controller's hand (CR 701.16 —
///       "you can't reveal a card you don't have"; with no Treefolk to
///       reveal the optional can't be taken, so the land enters tapped and
///       the agent is never prompted).
///     - When a Treefolk is present, consults the registered
///       <see cref="IPlayerAgent"/> via
///       <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>
///       with intent <see cref="BotIntent.CardAdvantage"/>. Revealing a card
///       you already hold is pure upside (no cost — it just lets the land
///       enter untapped), so heuristic / scripted bots reveal by default;
///       remote-agent UIs surface the printed question verbatim. On a "yes"
///       the land enters untapped (no payment — revealing is free); on "no"
///       / no-agent it enters tapped.
///
/// ## Deferred (v1 gaps)
/// - <b>The reveal itself isn't surfaced as a public event.</b> The predicate
///   only consults the agent's decision and gates tapped-ness; it does not
///   publish a "card revealed" event, so cards that care about revealed
///   information don't observe it. Same data-light simplification the other
///   ETB-replacement land factories take. The Treefolk card stays in hand
///   (revealing never moves it).
/// - <b>Pain-damage event provenance</b>: the 1 damage goes through
///   <see cref="Player.LoseLife"/>, not a <c>DamageDealtEvent</c> — damage-
///   prevention subscribers don't intercept it. Same shape as
///   <see cref="PainLandCycleFactory"/>.
/// - <b>Single-arg dispatcher path</b> — constructs without a
///   <see cref="ReplacementBus"/>; the reveal-or-tapped replacement is
///   omitted (shape-only posture matching <see cref="ShockLandCycleFactory"/>
///   and <see cref="CheckLandCycleFactory"/>). The mana abilities are still
///   attached. Lands enter untapped on this path; the full overload wires
///   the replacement when the bus is supplied.
/// </summary>
[CardName("Murmuring Bosk")]
public static class MurmuringBoskFactory
{
    public const string CardName = "Murmuring Bosk";

    /// <summary>Coloured pain modes — {T}: Add {W} or {B}, 1 damage to you.</summary>
    private static readonly string[] PainColours = { "W", "B" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("murmuring-bosk");

    /// <summary>
    /// Construct Murmuring Bosk with no ETB wiring (single-arg dispatcher
    /// path). The reveal-or-tapped replacement is omitted — no
    /// <see cref="ReplacementBus"/> available — matching the Shock / Check
    /// land cycle shape-only posture. The {G} painless mode and the {W}/{B}
    /// pain modes are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Murmuring Bosk. When <paramref name="replacements"/> is
    /// supplied the ETB "you may reveal a Treefolk; if you don't, it enters
    /// tapped" replacement is registered (CR 614.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the reveal-or-tapped ETB
    /// replacement is wired.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (Land — Forest) + the painless {T}: Add {G} mana ability
        // come from the JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {W} or {B}. This land deals 1 damage to you.
        // Split into two pain ManaAbility instances (one per produced
        // colour), same shape as PainLandCycleFactory's coloured modes.
        // ----------------------------------------------------------------
        foreach (var colour in PainColours)
        {
            AttachPainColouredMana(land, owner, colour);
        }

        // ----------------------------------------------------------------
        // ETB: "As this land enters, you may reveal a Treefolk card from
        // your hand. If you don't, this land enters tapped." (CR 614.1c)
        //
        // Modelled as a ConditionalEntersTappedReplacement: the predicate
        // returns true (untapped) iff the controller has a Treefolk in
        // hand and the agent elects to reveal it.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, _) => TryRevealTreefolk(controller)));
        }

        return land;
    }

    /// <summary>
    /// Attach a <c>{T}: Add &lt;colour&gt;. This land deals 1 damage to you.</c>
    /// pain mana ability. Built via the additional-cost overload of
    /// <see cref="ManaAbility"/>: tapping pays {T}; the
    /// <c>additionalCostPayer</c> then reduces the controller's life by 1
    /// (CR 120.3 — damage to a player causes loss of life equal to that
    /// damage). No life-floor gate (CR 119.4 governs "pay life" costs, not
    /// damage) — the painland can deal lethal damage to you.
    /// </summary>
    private static void AttachPainColouredMana(Land land, Player controller, string colour)
    {
        var mana = ManaCost.Parse(colour);
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: mana,
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Honours CR 701.16
    /// ("you can't reveal a card you don't have") by gating on a Treefolk
    /// in hand first, then consults the controller's registered agent for
    /// the optional reveal.
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (a Treefolk was
    /// revealed). <c>false</c> ⇒ enters tapped (no Treefolk in hand,
    /// declined, or no agent registered).</returns>
    private static bool TryRevealTreefolk(Player controller)
    {
        // CR 701.16 — nothing to reveal ⇒ the optional can't be taken.
        // Enter tapped without prompting the agent.
        var hasTreefolk = controller.Zones.Hand.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Treefolk));
        if (!hasTreefolk) return false;

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
                question: "Reveal a Treefolk card so this land enters untapped?",
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
