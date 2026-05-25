using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.51 — Convoke. "Your creatures can help cast this spell. Each
/// creature you tap while casting this spell pays for {1} or one mana of
/// that creature's color."
///
/// <para>This class is the alternative-cost DISCOVERY SHIM that surfaces a
/// Convoke spell on the bot's alt-cost probe rail
/// (<see cref="Majik.Core.Players.Agents.ConvokeAltCostProbe"/>) alongside
/// Pitch / Delve / Overload / Improvise. The actual cost-payment +
/// reduction live in <see cref="ConvokeAdditionalCost"/> (the working
/// primitive routed through <see cref="SpellCastFlow"/>'s CR 601.2f
/// additional-cost rail). The shim exists in two shapes:</para>
///
/// <list type="bullet">
///   <item>One-arg constructor (legacy): builds a marker-only alt-cost
///         whose <see cref="AlternativeManaCost"/> returns the printed
///         cost unchanged. Used by factory builders that just need a
///         "this card has Convoke" alt-cost surface
///         (<see cref="Majik.Core.CardData.Factories.ChordOfCallingFactory.BuildAlternativeCost"/>).</item>
///   <item>Three-arg constructor: wraps a real per-cast
///         <see cref="ConvokeAdditionalCost"/> with a creature selection;
///         <see cref="AlternativeManaCost"/> reports the post-convoke
///         effective cost so EV evaluators rank casting options correctly.
///         The bot's probe yields this shape.</item>
/// </list>
///
/// <para>Cast-time consumers MUST unpack <see cref="AdditionalCost"/> and
/// route it through <see cref="SpellCastFlow.CastAsync"/>'s
/// <c>additionalCosts</c> parameter — Convoke is technically an additional
/// cost modifier (not a true alternative cost like Flashback). The
/// <see cref="IAlternativeCost"/> wrapper exists only for bot-discovery
/// uniformity, mirroring
/// <see cref="Majik.Core.Players.Agents.ImproviseAlternativeCost"/>.</para>
///
/// <para><see cref="ReduceCost"/> is preserved as a pure-function legacy
/// reducer for callers/tests that pre-date the additional-cost rail. It
/// uses a colour-AGNOSTIC WUBRG strategy and is therefore weaker than
/// <see cref="ConvokeAdditionalCost.ApplyTo"/>; new code should call the
/// latter (which honours per-creature colour identity per CR 702.51b).</para>
///
/// <para>CR rule references: 702.51 (Convoke definition), 117.7
/// (cost-reduction floor), 601.2f (additional-cost timing).</para>
/// </summary>
public sealed class ConvokeAlternativeCost : IAlternativeCost
{
    /// <summary>
    /// The underlying <see cref="ConvokeAdditionalCost"/> when the
    /// alt-cost was built from a real per-cast creature-tap selection
    /// (probe-discovery path, three-arg constructor). Null on the legacy
    /// marker-only shim (one-arg constructor) — in which case
    /// <see cref="AlternativeManaCost"/> returns the printed cost
    /// unchanged and the cast flow falls through to full mana payment.
    ///
    /// <para>Cast-time consumers route the additional cost via
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the alt-cost shim itself stays
    /// off the IAlternativeCost mana-substitution rail (the cast flow's
    /// alternativeCost surface does NOT subtract per-tap reductions —
    /// that lives in the additional-cost loop, identical to Improvise).</para>
    /// </summary>
    public ConvokeAdditionalCost? AdditionalCost { get; }

    /// <summary>The spell's printed mana cost.</summary>
    public ManaCost PrintedCost { get; }

    /// <summary>The mana cost the agent will be prompted for. With a real
    /// <see cref="AdditionalCost"/> selection this is the printed cost
    /// post-convoke reduction; on the legacy shim this is the printed
    /// cost unchanged.</summary>
    public ManaCost AlternativeManaCost { get; }

    public string Description =>
        AdditionalCost == null
            ? "Convoke"
            : $"Convoke — tap {AdditionalCost.ReductionAmount} creature(s), pay {AlternativeManaCost}";

    /// <summary>
    /// Legacy marker-only constructor — kept for back-compat with the
    /// shape-only / template-builder path
    /// (<see cref="Majik.Core.CardData.Factories.ChordOfCallingFactory.BuildAlternativeCost"/>).
    /// Returns the printed cost unchanged; callers wanting actual
    /// per-creature reduction should use the three-arg constructor or
    /// route a <see cref="ConvokeAdditionalCost"/> through SpellCastFlow's
    /// additional-cost rail directly.
    /// </summary>
    public ConvokeAlternativeCost(ManaCost printedCost)
    {
        PrintedCost = printedCost ?? throw new ArgumentNullException(nameof(printedCost));
        AlternativeManaCost = printedCost;
        AdditionalCost = null;
    }

    /// <summary>
    /// Discovery-surface adapter that wraps a <see cref="ConvokeAdditionalCost"/>
    /// as an <see cref="IAlternativeCost"/> so the bot's existing alt-cost rail
    /// (<see cref="Majik.Core.Players.Agents.AlternativeCostProbeRegistry"/>)
    /// can stream Convoke candidates alongside Pitch / Delve / Overload /
    /// Escape / Improvise without inventing a parallel API.
    ///
    /// <para>This is a SHIM — at cast time the caller MUST unpack
    /// <see cref="AdditionalCost"/> and route it through
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
    /// parameter (Convoke is an additional cost, not a true alternative —
    /// CR 702.51). The shim's <see cref="AlternativeManaCost"/> reports the
    /// post-convoke effective cost so EV-style evaluators can rank casting
    /// options correctly; its <see cref="OnResolved"/> is a no-op because
    /// the actual tap-side-effect ran during the additional-cost loop at
    /// cast time.</para>
    /// </summary>
    public ConvokeAlternativeCost(ICard source, ManaCost printedCost, IReadOnlyList<Creature> chosen)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        PrintedCost = printedCost ?? throw new ArgumentNullException(nameof(printedCost));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));

        AdditionalCost = new ConvokeAdditionalCost(source, chosen);

        // CR 702.51b — apply the convoke reduction (generic OR creature-
        // coloured pip per tap). Mirrors ImproviseAlternativeCost shape.
        AlternativeManaCost = AdditionalCost.ApplyTo(printedCost);
    }

    /// <summary>
    /// Future-hook: given the printed cost + a creature pick list, return
    /// the cost AFTER applying Convoke reductions per CR 702.51b. v1
    /// returns <paramref name="printedCost"/> unchanged when
    /// <paramref name="tappedCreatures"/> is empty or null. When creatures
    /// are supplied, applies the deterministic strategy: each creature
    /// removes one generic pip first; once generic is exhausted, each
    /// creature removes one coloured pip of any of its colours (in
    /// W,U,B,R,G order).
    ///
    /// Not called from the cast flow today — exposed so tests + future
    /// callers can exercise the reduction in isolation. CR 117.7c floor
    /// (no negative pips) is honoured.
    /// </summary>
    public static ManaCost ReduceCost(ManaCost printedCost, IReadOnlyList<Creature>? tappedCreatures)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        if (tappedCreatures == null || tappedCreatures.Count == 0) return printedCost;

        var generic = printedCost.Generic;
        var w = printedCost.White;
        var u = printedCost.Blue;
        var b = printedCost.Black;
        var r = printedCost.Red;
        var g = printedCost.Green;

        foreach (var c in tappedCreatures)
        {
            if (c == null) continue;
            if (generic > 0) { generic--; continue; }

            // Out of generic — try to discount a coloured pip matching one
            // of the creature's colours. We can't easily inspect a
            // Creature's colour identity here without coupling to the
            // colour subsystem; v1 picks any remaining coloured pip in
            // WUBRG order. Real implementation will consult
            // creature.ManaCost / colour identity per CR 702.51b.
            if (w > 0) { w--; continue; }
            if (u > 0) { u--; continue; }
            if (b > 0) { b--; continue; }
            if (r > 0) { r--; continue; }
            if (g > 0) { g--; continue; }
            // All pips paid; further creatures contribute nothing.
            break;
        }

        return BuildCost(generic, w, u, b, r, g, printedCost.HasX);
    }

    private static ManaCost BuildCost(int gen, int w, int u, int bl, int r, int g, bool hasX)
    {
        var s = (hasX ? "X" : "") + (gen > 0 ? gen.ToString() : "")
              + new string('W', w) + new string('U', u) + new string('B', bl)
              + new string('R', r) + new string('G', g);
        return ManaCost.Parse(s);
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card != null
        && card.Zone == ZoneType.Hand
        && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster)
    {
        // No post-resolution side-effect. Convoke only affects cost
        // payment; resolution proceeds normally (CR 702.51c).
    }
}
