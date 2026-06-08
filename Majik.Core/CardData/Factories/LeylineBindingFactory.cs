using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline Binding (Dominaria United, {5}{W}).
///
/// Enchantment. Oracle text (verified against the embedded seed):
///   "Flash
///    Domain — This spell costs {1} less to cast for each basic land type
///    among lands you control.
///    When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield."
///
/// Leyline Binding is the "Oblivion Ring" exile-until-leaves template
/// (CR 701.21) on a Flash (CR 702.8) body with a Domain (CR 702.16 /
/// CR 117.7) cost reducer. The exile-on-ETB / return-on-LTB backbone is
/// byte-identical to <see cref="CastOutFactory"/> / <see cref="BanishingLightFactory"/>:
/// both share the printed "target nonland permanent an opponent controls"
/// target and the same declarative <c>exile_until_leaves</c> verb (with
/// <c>opponentControlsOnly: true</c>) sourced from the embedded JSON
/// definition (<c>leyline-binding.json</c>).
///
/// ## Implemented (v1)
/// - <b>Enchantment {5}{W}</b>. Base shape (name / Enchantment / cost)
///   materialised from the embedded JSON definition via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="CastOutFactory"/> / <see cref="DetentionSphereFactory"/>.
/// - <b>Flash</b> (CR 702.8) — <see cref="KeywordAbility"/> marker; the
///   spell-casting rules consult the marker to allow casting at instant
///   speed.
/// - <b>Domain cost reduction (CR 702.16 / CR 117.7)</b>: a
///   <see cref="DomainCostReductionAbility"/> with <c>multiplier: 1</c>
///   ("{1} less per basic land type among lands you control"). Floor-at-zero
///   + coloured-pip preservation (the single W pip never reduces, CR 117.7c)
///   are enforced upstream by <see cref="CostReduction.GetEffectiveCost"/>;
///   with all five basics out the {5} generic collapses to nothing and the
///   spell costs just {W} — the format-defining turn-2 play.
/// - <b>ETB + LTB exile-until-leaves pair</b> — sourced from the
///   declarative <c>exile_until_leaves</c> verb
///   (<see cref="Majik.Core.CardData.Definitions.ExileUntilLeavesEffectDef"/>),
///   identical backbone to Cast Out / Banishing Light. The verb attaches both
///   linked abilities at build time:
///   <list type="bullet">
///     <item>ETB (CR 603.6a / CR 701.21): single 1..1 "target nonland
///       permanent an opponent controls" target; on resolve a CR 608.2b
///       legality re-check then a raw exile, captured in a per-Leyline-Binding
///       closure shared with the LTB ability.</item>
///     <item>LTB (CR 603.6c): when Leyline Binding leaves the battlefield the
///       captured card returns to the battlefield under its owner's control
///       (CR 110.2).</item>
///   </list>
/// </summary>
[CardName("Leyline Binding")]
public static class LeylineBindingFactory
{
    public const string CardName = "Leyline Binding";
    public const string PrintedManaCost = "{5}{W}";
    public const string Slug = "leyline-binding";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Leyline Binding with no runtime services. The ETB / LTB exile
    /// triggers + Flash + Domain cost reducer are attached to the card shape;
    /// neither triggered ability is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher / cost
    /// tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Leyline Binding with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.16 (Domain) + CR 117.7 — "This spell costs {1} less to cast
        // for each basic land type among lands you control." multiplier: 1.
        // Floor-at-zero + coloured-pip preservation enforced upstream by
        // CostReduction.GetEffectiveCost.
        card.AddAbility(new DomainCostReductionAbility(multiplier: 1));

        // CR 701.21 — exile target nonland permanent an opponent controls until
        // this leaves. Sourced from the declarative exile_until_leaves verb
        // (leyline-binding.json), identical backbone to Cast Out. The verb
        // attached both linked abilities at build time; register them with a
        // live TriggerManager (same posture as OblivionRingFactory).
        if (triggers != null)
        {
            foreach (var ability in card.Abilities.OfType<ITriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(ability);
            }
        }

        return card;
    }
}
