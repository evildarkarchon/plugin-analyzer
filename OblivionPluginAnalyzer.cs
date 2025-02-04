using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace PluginAnalyzer;

/// <summary>
/// Provides analysis functionality for Oblivion plugins by inheriting from the base plugin analyzer.
/// This includes specialized processing of Oblivion-specific plugin records, such as identifying placed objects.
/// </summary>
public class OblivionPluginAnalyzer(
    IOblivionModGetter plugin,
    ILinkCache<IOblivionMod, IOblivionModGetter> linkCache)
    : BasePluginAnalyzer<IOblivionMod, IOblivionModGetter>(plugin, linkCache)
{
    protected override bool IsNavmesh(IMajorRecordGetter record)
    {
        return false; // Oblivion doesn't use navmeshes
    }

    protected override bool IsPlaced(IMajorRecordGetter record)
    {
        return record is IPlacedGetter;
    }

    protected override bool CompareSpecialRecords(IMajorRecordGetter current, IMajorRecordGetter master)
    {
        switch (current)
        {
            // Handle Oblivion-specific GMST
            case IGameSettingGetter gmstCurrent when
                master is IGameSettingGetter gmstMaster:
                return gmstCurrent.EditorID == gmstMaster.EditorID &&
                       gmstCurrent.ToString() == gmstMaster.ToString();
            // Handle Oblivion-specific GLOB
            case IGlobalGetter globCurrent when
                master is IGlobalGetter globMaster:
            {
                if (globCurrent.EditorID != globMaster.EditorID) return false;

                float? currentVal = globCurrent switch
                {
                    IGlobalFloatGetter g => g.Data!.Value,
                    IGlobalShortGetter g => g.Data!.Value,
                    _ => null
                };

                float? masterVal = globMaster switch
                {
                    IGlobalFloatGetter g => g.Data!.Value,
                    IGlobalShortGetter g => g.Data!.Value,
                    _ => null
                };

                if (!currentVal.HasValue || !masterVal.HasValue) return false;
                return Math.Abs(currentVal.Value - masterVal.Value) < 0.000001f;
            }
            default:
                return false;
        }
    }

    protected override bool ShouldIgnoreForItmComparison(IMajorRecordGetter record)
    {
        return record switch
        {
            // Placed objects need specific handling to avoid pattern conflicts
            IPlacedCreatureGetter => true, // ACRE
            IPlacedNpcGetter => true, // ACHR
            IPlacedObjectGetter => true, // REFR
            // Other record types
            IQuestGetter => true, // QUST
            ICellGetter => true, // CELL
            IWorldspaceGetter => true, // WRLD
            ILandscapeGetter => true, // LAND
            ILightGetter => true, // LIGHT
            IIdleAnimationGetter => true, // IDLE
            IPathGridGetter => true, // PGRD
            IDialogTopicGetter => true, // DIAL
            _ => false
        };
    }

    protected override bool CompareGameSpecificRecord(IMajorRecordGetter current, IMajorRecordGetter master)
    {
        return current switch
        {
            ILeveledItemGetter levItemCurrent when
                master is ILeveledItemGetter levItemMaster =>
                CompareLeveledItem(levItemCurrent, levItemMaster),

            ILeveledSpellGetter levSpellCurrent when
                master is ILeveledSpellGetter levSpellMaster =>
                CompareLeveledSpell(levSpellCurrent, levSpellMaster),

            _ => CompareGenericRecord(current, master)
        };
    }

    private bool CompareLeveledItem(ILeveledItemGetter current,
        ILeveledItemGetter master)
    {
        if (current.Flags != master.Flags ||
            current.ChanceNone != master.ChanceNone) return false;

        var currentEntries = current.Entries.ToList();
        var masterEntries = master.Entries.ToList();

        if (currentEntries.Count != masterEntries.Count) return false;

        currentEntries = currentEntries
            .OrderBy(e => e.Reference.FormKey.ID)
            .ThenBy(e => e.Level)
            .ToList();
        masterEntries = masterEntries
            .OrderBy(e => e.Reference.FormKey.ID)
            .ThenBy(e => e.Level)
            .ToList();

        for (var i = 0; i < currentEntries.Count; i++)
        {
            var currentEntry = currentEntries[i];
            var masterEntry = masterEntries[i];

            if (currentEntry.Level != masterEntry.Level ||
                currentEntry.Count != masterEntry.Count ||
                !CompareFormLinks(currentEntry.Reference, masterEntry.Reference))
                return false;
        }

        return true;
    }

    private bool CompareLeveledSpell(ILeveledSpellGetter current,
        ILeveledSpellGetter master)
    {
        if (current.Flags != master.Flags ||
            current.ChanceNone != master.ChanceNone) return false;

        var currentEntries = current.Entries.ToList();
        var masterEntries = master.Entries.ToList();

        if (currentEntries.Count != masterEntries.Count) return false;

        currentEntries = currentEntries
            .OrderBy(e => e.Reference.FormKey.ID)
            .ThenBy(e => e.Level)
            .ToList();
        masterEntries = masterEntries
            .OrderBy(e => e.Reference.FormKey.ID)
            .ThenBy(e => e.Level)
            .ToList();

        for (var i = 0; i < currentEntries.Count; i++)
        {
            var currentEntry = currentEntries[i];
            var masterEntry = masterEntries[i];

            if (currentEntry.Level != masterEntry.Level ||
                !CompareFormLinks(currentEntry.Reference, masterEntry.Reference))
                return false;
        }

        return true;
    }
}