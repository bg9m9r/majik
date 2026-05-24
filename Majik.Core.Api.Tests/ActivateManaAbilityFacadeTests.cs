using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Integration coverage for <see cref="ActivateManaAbilityCommand"/> end-to-
/// end through <see cref="GameFacade"/>: the engine actually taps the
/// permanent, deposits mana into the player's pool, fires the mana-ability
/// event, and (per CR 605.3a) keeps priority on the same player so they
/// can chain into a cast.
/// </summary>
public class ActivateManaAbilityFacadeTests
{
    [Fact]
    public async Task ActivateMountain_TapsAndAddsRedManaAndHoldsPriority()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        var manaEvents = 0;
        using (var _ = facade.Subscribe(e =>
        {
            if (e.Type == nameof(Majik.Core.Events.ManaAbilityActivatedEvent)) manaEvents++;
        }))
        {
            await facade.StartAsync();

            // Engine is now awaiting Alice's priority choice. Activate the
            // mountain; the same player should still be on prompt afterward.
            await facade.SubmitAsync(new ActivateManaAbilityCommand(mountain.InstanceId, "R")
            {
                PlayerId = facade.Alice.Id,
            });

            mountain.IsTapped.Should().BeTrue("CR 605 — tap is the activation cost.");
            facade.Alice.ManaPool.Red.Should().Be(1,
                "CR 605 — the generated mana lands in the player's pool.");
            manaEvents.Should().Be(1, "ManaAbilityActivatedEvent must surface to subscribers.");

            // After a mana ability, the same player keeps priority. The
            // engine immediately re-prompts Alice — we should be able to
            // submit another command from her (a Pass closes the round).
            await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });
            await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });

            facade.IsRoundComplete.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ActivateAlreadyTappedMountain_NoCrashNoPoolChange_StillHoldsPriority()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        mountain.Tap();
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        await facade.StartAsync();

        // Engine swallows the InvalidPlayerActionException from
        // ManaAbilityActivator.CanActivate. The round must continue.
        await facade.SubmitAsync(new ActivateManaAbilityCommand(mountain.InstanceId, "R")
        {
            PlayerId = facade.Alice.Id,
        });
        facade.Alice.ManaPool.Red.Should().Be(0, "tapped source must not produce mana.");

        // Priority loop is still healthy — both players can pass to close
        // the round (no engine fault from the rejected activation).
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });

        facade.IsRoundComplete.Should().BeTrue();
    }

    [Fact]
    public void Snapshot_Mountain_ProducedManaColorsIsR()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        var state = facade.GetState();
        var dto = state.Players.Single(p => p.Id == facade.Alice.Id)
            .Battlefield.Cards.Single(c => c.Name == "Mountain");

        dto.ProducedManaColors.Should().Be("R",
            "CR 605 — the snapshot derives produced colour straight from the " +
            "card's IManaAbility instances, not oracle-text parsing.");
    }

    [Fact]
    public void Snapshot_DualLand_ProducedManaColorsIsBGInWubrgOrder()
    {
        // Synthetic dual with two ManaAbility instances (B and G) — mirrors
        // the shape OracleManaBinder produces for shock-land oracle text.
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var dual = new Land("Synth Dual");
        dual.SetOwner(facade.Alice);
        dual.ChangeController(facade.Alice);
        dual.AddAbility(new ManaAbility(dual, facade.Alice, ManaCost.Parse("B")));
        dual.AddAbility(new ManaAbility(dual, facade.Alice, ManaCost.Parse("G")));
        facade.Alice.Zones.Battlefield.AddCard(dual);

        var state = facade.GetState();
        var dto = state.Players.Single(p => p.Id == facade.Alice.Id)
            .Battlefield.Cards.Single(c => c.Name == "Synth Dual");

        dto.ProducedManaColors.Should().Be("BG", "colours emit in fixed WUBRG order.");
    }

    [Fact]
    public void Snapshot_Creature_NoManaAbility_ProducedManaColorsIsEmpty()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(facade.Alice);
        bear.ChangeController(facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(bear);

        var state = facade.GetState();
        var dto = state.Players.Single(p => p.Id == facade.Alice.Id)
            .Battlefield.Cards.Single(c => c.Name == "Grizzly Bears");

        dto.ProducedManaColors.Should().Be("");
    }

    private static Land BuildBasicLand(string name, CardSubtype subtype, Player controller)
    {
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.ChangeController(controller);
        land.SetZone(ZoneType.Battlefield);
        OracleManaBinder.BindBasicLandMana(land, controller);
        return land;
    }
}
