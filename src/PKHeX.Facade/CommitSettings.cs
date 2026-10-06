using PKHeX.Core;

namespace PKHeX.Facade;

internal static class CommitSettings
{
    /// <summary>
    /// Settings for writing a slot back on commit. Pokémon reach a commit already adapted to the save,
    /// so adapting them again would change their bytes; the Pokédex still has to learn about a species
    /// a slot now holds, which is the one thing PKHeX would otherwise only do when importing.
    /// </summary>
    public static readonly EntityImportSettings Slot = new(
        UpdateToSaveFile: EntityImportOption.Disable,
        UpdatePokeDex: EntityImportOption.Enable,
        UpdateRecord: EntityImportOption.Disable);
}
