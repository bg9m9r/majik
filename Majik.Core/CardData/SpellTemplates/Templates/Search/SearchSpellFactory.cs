using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

internal static class SearchSpellFactory
{
    internal static SpellDefinition SearchLibrarySpell(Player caster, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"tutor {kindRaw}", () =>
        {
            // MVP tutor: deterministic — first library card matching predicate.
            // Real implementation: prompt agent for choice; this default keeps
            // tests deterministic until SpellCastFlow learns library-target prompts.
            bool Pred(ICard c) => kindRaw.ToLowerInvariant() switch
            {
                "basic land" => c.HasType(CardType.Land),
                "land" => c.HasType(CardType.Land),
                "creature" => c.HasType(CardType.Creature),
                "artifact" => c.HasType(CardType.Artifact),
                "enchantment" => c.HasType(CardType.Enchantment),
                "instant" => c.HasType(CardType.Instant),
                "sorcery" => c.HasType(CardType.Sorcery),
                "planeswalker" => c.HasType(CardType.Planeswalker),
                _ => false,
            };
            var pick = caster.Zones.Library.GetCards().FirstOrDefault(Pred);
            if (pick == null) return;
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
            // CR 701.19c — shuffle after a search effect.
            // (No IZone.Shuffle yet; GameDriver owns shuffle. Skip for MVP —
            // search ordering not exposed via library iteration today.)
        }) });

    // Basic land names per CR 305.6.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase) { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    internal static SpellDefinition SearchLandToBattlefieldSpell(
        Player caster, string kindRaw, bool tapped) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"tutor land -> battlefield{(tapped ? " tapped" : "")}", () =>
        {
            bool Pred(ICard c)
            {
                if (!c.HasType(CardType.Land)) return false;
                if (kindRaw.Contains("basic", StringComparison.OrdinalIgnoreCase))
                    return BasicLandNames.Contains(c.Name);
                return true;
            }

            var pick = caster.Zones.Library.GetCards().FirstOrDefault(Pred);
            if (pick == null) return;
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            if (tapped && pick is Permanent perm)
                perm.Tap();
            // CR 701.19c — shuffle after a search effect (skipped for MVP;
            // same rationale as SearchLibrarySpell above).
        }) });

    /// <summary>
    /// Green Sun's Zenith template — {X}{G} sorcery (Rule 107.4b X cost).
    /// Tutors the first library card whose color matches <paramref name="colorRaw"/> and
    /// whose mana value ≤ X, placing it directly onto the battlefield (CR 701.19a).
    ///
    /// Color is determined by <see cref="CardColors.GetColors"/>, which derives color
    /// from the card's mana cost pips (CR 105.2a).
    ///
    /// Post-resolution self-return-to-library (the "Shuffle Green Sun's Zenith into
    /// its owner's library" clause, CR 608.2c override) is DEFERRED — v1 lets the
    /// spell go to the graveyard like any other sorcery. Engine infrastructure for
    /// a generic "ShuffleSourceToLibraryOnResolve" hook in SpellCastFlow is needed
    /// to implement it correctly.
    /// </summary>
    internal static SpellDefinition GreenSunsZenithSpell(Player caster, string colorRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p =>
        {
            var x = p.X ?? 0;
            // Map the oracle-text color word to the ManaColor enum value.
            var targetColor = colorRaw.ToLowerInvariant() switch
            {
                "white"  => ManaColor.White,
                "blue"   => ManaColor.Blue,
                "black"  => ManaColor.Black,
                "red"    => ManaColor.Red,
                "green"  => ManaColor.Green,
                _        => ManaColor.Green,
            };
            return new IEffect[] { new Effect($"GSZ x={x}", () =>
            {
                // CR 701.19a — search is a library action; pick the first qualifying card.
                // Real implementation would prompt the agent; v1 is deterministic (first match).
                var pick = caster.Zones.Library.GetCards()
                    .FirstOrDefault(c =>
                        c.HasType(CardType.Creature) &&
                        CardColors.GetColors(c).Contains(targetColor) &&
                        ManaCost.Parse(c.ManaCost).TotalValue <= x);
                if (pick != null)
                {
                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Battlefield.AddCard(pick);
                    pick.SetZone(ZoneType.Battlefield);
                }
                // CR 701.19c — shuffle after a search effect (deferred, same rationale
                // as other search spells in this binder).
            }) };
        });
}
