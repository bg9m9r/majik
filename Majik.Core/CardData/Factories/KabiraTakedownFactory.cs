using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Kabira Takedown // Kabira Plateau (Zendikar Rising, {1}{W}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Kabira Takedown deals damage equal to the number of creatures you
///    control to target creature or planeswalker."
///
/// Back face — <see cref="KabiraPlateauFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="EmeriasCallFactory"/> / <see cref="EmeriaShatteredSkyclaveFactory"/>
/// and <see cref="ShatterskullSmashingFactory"/> /
/// <see cref="ShatterskullTheHammerPassFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>kabira-takedown.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the JSON
/// schema models neither MDFC faces nor count-based variable damage).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{1}{W}</c>, mono-white (one {W} pip).
/// - <see cref="MdfcState"/> attached (front = "Kabira Takedown",
///   back = "Kabira Plateau"); starts on the front face.
/// - No modes, no X. Single 1..1 "target creature or planeswalker"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>),
///   gathering all battlefield creatures / planeswalkers (mirrors
///   <see cref="BitterTriumphFactory"/>).
/// - Resolution:
///     <list type="bullet">
///       <item>Damage dealt = the number of creatures the CASTER controls on
///         the battlefield, counted AT RESOLUTION (CR 608.2 — the spell's
///         effect is determined as it resolves). Counted via
///         <see cref="Player.Zones"/> battlefield enumeration.</item>
///       <item>The damage is dealt to the single target via
///         <see cref="Fx.DealDamageAny"/> — creature targets take marked
///         damage (CR 119.2); planeswalker targets lose loyalty (CR 306.7).</item>
///       <item>CR 608.2b — an illegal-at-resolution target (off battlefield,
///         no longer a creature/planeswalker) is silently dropped and the
///         spell does nothing.</item>
///       <item>If the caster controls zero creatures the damage is 0 — a
///         no-op (<see cref="Fx.DealDamageAny"/> early-returns on amount ≤ 0).</item>
///     </list>
///
/// ## References
///
/// - <see cref="BitterTriumphFactory"/> — "target creature or planeswalker"
///   single-target request shape with the same live CandidateGatherer.
/// - <see cref="EmeriasCallFactory"/> — companion ZNR MDFC front face
///   (JSON identity + MdfcState + code-attached resolve body).
/// </summary>
[CardName("Kabira Takedown")]
public static class KabiraTakedownFactory
{
    public const string CardName = "Kabira Takedown";
    public const string BackName = "Kabira Plateau";

    /// <summary>
    /// Construct Kabira Takedown as an Instant (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("kabira-takedown");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker WITH a castable back-face
        // descriptor. The back face is the LAND back face played with no stack;
        // MdfcCastFlow offers the controller a face choice at cast time and
        // materializes a fresh back-face land instance when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                KabiraPlateauFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "deal damage equal to the number of creatures
    /// you control to target creature or planeswalker"
    /// <see cref="SpellDefinition"/>.
    ///
    /// No modes, no X. One 1..1 "target creature or planeswalker" request.
    /// The damage amount is computed AT RESOLUTION as the caster's
    /// battlefield creature count (CR 608.2).
    /// </summary>
    /// <param name="caster">Spell controller — the "you" whose creatures are
    /// counted for the damage amount.</param>
    /// <param name="resolver">Target resolver — maps the chosen target token
    /// to the live game object (creature or planeswalker). Pass <c>o =&gt; o</c>
    /// for tests that hand permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer (agent-prompt MVP). All creatures +
                    // planeswalkers on the battlefield across every player —
                    // mirrors BitterTriumphFactory.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = resolver(raw);

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal damage equal to creatures you control to target",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // CR 608.2 — "the number of creatures you control"
                            // is determined as the spell resolves.
                            var amount = CountCreaturesControlled(caster);
                            if (amount <= 0) return;

                            // CR 119.2 / CR 306.7 — creature takes marked
                            // damage; planeswalker loses loyalty.
                            Fx.DealDamageAny(target, amount);
                        }),
                };
            });
    }

    /// <summary>
    /// CR 608.2 — count the creatures the <paramref name="caster"/> controls on
    /// the battlefield (the "number of creatures you control" for the damage
    /// amount).
    /// </summary>
    private static int CountCreaturesControlled(Player caster) =>
        caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count();
}
