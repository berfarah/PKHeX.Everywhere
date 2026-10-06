using AwesomeAssertions;
using PKHeX.Core;
using PKHeX.Facade.Pokemons;
using PKHeX.Facade.Tests.Base;

namespace PKHeX.Facade.Tests;

public class CommitTests
{
    private const string NewTrainerName = "Tester";
    private const string EditedNickname = "Edited";

    [Theory]
    [SupportedSaveFiles]
    public void Export_AfterTrainerRename_WritesExistingPokemonBackUnchanged(string saveFile)
    {
        var game = SaveFilePath.Load(saveFile);
        var before = Snapshot(game.SaveFile);

        game.Trainer.Name = NewTrainerName;

        game.SaveAndReload(reloaded => Snapshot(reloaded.SaveFile).Should().BeEquivalentTo(before));
    }

    [Theory]
    [SupportedSaveFiles]
    public void EditingOnePokemon_AfterTrainerRename_LeavesTheOthersUnchanged(string saveFile)
    {
        var game = SaveFilePath.Load(saveFile);
        var target = game.Trainer.PokemonBox.All.First(p => p.Pkm.Species != 0);
        var before = Snapshot(game.SaveFile).Where(s => s.Nickname != target.Nickname || s.Data != Hex(target.Pkm)).ToList();

        game.Trainer.Name = NewTrainerName;
        var edited = target.Clone();
        edited.ChangeNickname(EditedNickname);
        game.Trainer.AddOrUpdate(target.UniqueId, edited, PokemonSource.Box);

        Snapshot(game.SaveFile).Where(s => s.Nickname != EditedNickname).Should().BeEquivalentTo(before);
    }

    [Theory]
    [SupportedSaveFiles(Except = [SaveFilePath.LetsGoEevee])] // its boxes hold only the party
    [InlineData(SaveFilePath.Yellow)]
    public void Export_AfterEditingOneBoxPokemonInPlace_LeavesEveryOtherSlotAsLoaded(string saveFile)
    {
        var original = SaveFilePath.Load(saveFile).SaveFile;
        var game = SaveFilePath.Load(saveFile);
        var (index, target) = game.Trainer.PokemonBox.Boxed().First(p => p.Pokemon.IsEditable);

        target.ChangeNickname(EditedNickname);

        game.SaveAndReload(reloaded =>
        {
            reloaded.SaveFile.GetBoxSlotAtIndex(index).Nickname.Should().Be(EditedNickname);
            BoxSlots(reloaded.SaveFile).Where((_, i) => i != index).Should().Equal(BoxSlots(original).Where((_, i) => i != index));
            PartySlots(reloaded.SaveFile).Should().Equal(PartySlots(original));
        });
    }

    [Fact]
    public void Export_AfterTurningAGen2BoxPokemonIntoAnEggInPlace_KeepsTheEgg()
    {
        var game = SaveFilePath.Load(SaveFilePath.Crystal);
        var (index, target) = game.Trainer.PokemonBox.Boxed().First(p => !p.Pokemon.Pkm.IsEgg);

        target.Pkm.IsEgg = true;

        game.SaveAndReload(reloaded => reloaded.SaveFile.GetBoxSlotAtIndex(index).IsEgg.Should().BeTrue());
    }

    [Theory]
    [InlineData(SaveFilePath.LetsGoPikachu)]
    [InlineData(SaveFilePath.LetsGoEevee)]
    public void AddingFromFile_AdaptsThePokemonToTheSave(string saveFile)
    {
        var game = SaveFilePath.Load(saveFile);
        game.Trainer.Name = NewTrainerName;
        var pokemon = PokemonFile.LoadFor(GameVersion.GP, game);
        pokemon.Owner.Name.Should().NotBe(NewTrainerName);

        game.Trainer.PokemonBox.AddOnEmptySlot(pokemon, out var index).Should().BeTrue();

        var added = game.Trainer.PokemonBox.All[index];
        added.Owner.HandlingTrainerName.Should().Be(NewTrainerName);
        added.Owner.CurrentHandler.Should().Be(Owner.Handler.SomeoneElse);
    }

    [Theory]
    [SupportedSaveFiles]
    public void CommittingAnEditedBoxPokemon_MarksTheNewSpeciesSeenAndCaught(string saveFile)
    {
        var game = SaveFilePath.Load(saveFile);
        if (!game.SaveFile.HasPokeDex) return;

        // A slot can only become another member of its own evolution family, so look for one whose
        // family has a species the dex hasn't registered yet.
        var candidate = game.Trainer.PokemonBox.Boxed()
            .Where(p => p.Pokemon.IsEditable)
            .Select(p => (p.Pokemon, To: p.Pokemon.Options().Species
                .FirstOrDefault(c => c.Id != p.Pokemon.Species.Id && !game.SaveFile.GetCaught((ushort)c.Id))))
            .FirstOrDefault(p => p.To is not null);
        if (candidate.Pokemon is null) return;

        var (target, to) = candidate;
        var edited = target.Clone();
        edited.Update(new PokemonPatch { Species = to!.Id });
        game.Trainer.AddOrUpdate(target.UniqueId, edited, PokemonSource.Box);

        game.SaveFile.GetSeen((ushort)to.Id).Should().BeTrue();
        game.SaveFile.GetCaught((ushort)to.Id).Should().BeTrue();
    }

    [Fact]
    public void CommittingAnEditedGen3BoxPokemon_KeepsTheThreeSeenCopiesInAgreement()
    {
        var game = SaveFilePath.Load(SaveFilePath.Emerald);
        var save = (SAV3)game.SaveFile;

        var (_, target) = game.Trainer.PokemonBox.Boxed().First(p => p.Pokemon.IsEditable);
        var to = target.Options().Species.First(c => c.Id != target.Species.Id);

        // The fixture has a complete Pokédex, so clear the target species first to see the commit set it.
        save.SetSeen((ushort)to.Id, false);
        save.SetCaught((ushort)to.Id, false);

        var edited = target.Clone();
        edited.Update(new PokemonPatch { Species = to!.Id });
        game.Trainer.AddOrUpdate(target.UniqueId, edited, PokemonSource.Box);

        save.GetSeen((ushort)to.Id).Should().BeTrue();

        // Gen 3 keeps the seen flags in three places and SetSeen mirrors all of them; a save the game
        // reads back would disagree with itself otherwise.
        var bit = to.Id - 1;
        var ofs = bit >> 3;
        FlagUtil.GetFlag(save.Large, save.LargeBlock.SeenOffset2 + ofs, bit & 7).Should().BeTrue();
        FlagUtil.GetFlag(save.Large, save.LargeBlock.SeenOffset3 + ofs, bit & 7).Should().BeTrue();
    }

    private record Slot(string Nickname, string Data, bool Legal);

    private static List<Slot> Snapshot(SaveFile save) => save.PartyData
        .Concat(save.BoxData)
        .Where(pkm => pkm.Species != 0)
        .Select(pkm => new Slot(pkm.Nickname, Hex(pkm), new LegalityAnalysis(pkm).Valid))
        .ToList();

    private static List<string> BoxSlots(SaveFile save) => Enumerable.Range(0, save.SlotCount).Select(i => Hex(save.GetBoxSlotAtIndex(i))).ToList();

    private static List<string> PartySlots(SaveFile save) => save.PartyData.Select(Hex).ToList();

    private static string Hex(PKM pkm) => Convert.ToHexString(pkm.Data);
}
