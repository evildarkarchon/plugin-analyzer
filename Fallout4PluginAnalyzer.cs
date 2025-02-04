using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace PluginAnalyzer;

/// <summary>
/// Provides analysis capabilities specific to Fallout 4 plugin files, determining properties
/// such as navigation mesh records and placed object records.
/// </summary>
public class Fallout4PluginAnalyzer(
    IFallout4ModGetter plugin,
    ILinkCache<IFallout4Mod, IFallout4ModGetter> linkCache)
    : BasePluginAnalyzer<IFallout4Mod, IFallout4ModGetter>(plugin, linkCache)
{
    protected override bool IsNavmesh(IMajorRecordGetter record)
    {
        return record is INavigationMeshGetter;
    }

    protected override bool IsPlaced(IMajorRecordGetter record)
    {
        return record is IPlacedGetter;
    }

    protected override bool CompareSpecialRecords(IMajorRecordGetter current, IMajorRecordGetter master)
    {
        switch (current)
        {
            // Handle Fallout 4-specific GMST
            case IGameSettingGetter gmstCurrent when
                master is IGameSettingGetter gmstMaster:
                return gmstCurrent.EditorID == gmstMaster.EditorID &&
                       gmstCurrent.ToString() == gmstMaster.ToString();
            // Handle Fallout 4-specific GLOB
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
        }

        // Handle Fallout 4-specific Form Lists
        if (current is not IFormListGetter flstCurrent ||
            master is not IFormListGetter flstMaster) return false;
        var currentItems = flstCurrent.Items.ToList();
        var masterItems = flstMaster.Items.ToList();

        if (currentItems.Count != masterItems.Count) return false;

        var sortedCurrent = currentItems.OrderBy(x => x.FormKey.ID).ToList();
        var sortedMaster = masterItems.OrderBy(x => x.FormKey.ID).ToList();

        return sortedCurrent.SequenceEqual(sortedMaster);
    }

    protected override bool ShouldIgnoreForItmComparison(IMajorRecordGetter record)
    {
        return record switch
        {
            // Placed objects need specific handling to avoid pattern conflicts
            IPlacedArrowGetter => true, // PARW
            IPlacedBeamGetter => true, // PBEA
            IPlacedFlameGetter => true, // PFLA
            IPlacedHazardGetter => true, // PHZD
            IPlacedMissileGetter => true, // PMIS
            IPlacedObjectGetter => true, // REFR
            IPlacedNpcGetter => true, // ACHR
            // Other record types
            IQuestGetter => true, // QUST
            ICellGetter => true, // CELL
            INavigationMeshGetter => true, // NAVMESH
            IWorldspaceGetter => true, // WRLD
            ILandscapeGetter => true, // LAND
            ILandscapeTextureGetter => true, // LTEX
            ILightGetter => true, // LIGHT
            IIdleAnimationGetter => true, // IDLE
            IPackageGetter => true, // PACK
            IDialogTopicGetter => true, // DIAL
            IDialogResponsesGetter => true, // INFO
            IRelationshipGetter => true, // RELA
            IEncounterZoneGetter => true, // ECZN
            ILocationGetter => true, // LCTN
            ILocationReferenceTypeGetter => true, // LCRT
            IImageSpaceGetter => true, // IMGS
            IImageSpaceAdapterGetter => true, // IMAD
            IMaterialObjectGetter => true, // MATO
            IMaterialSwapGetter => true, // MSWP
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

            ILeveledNpcGetter levNpcCurrent when
                master is ILeveledNpcGetter levNpcMaster =>
                CompareLeveledNpc(levNpcCurrent, levNpcMaster),

            IConstructibleObjectGetter cobj1 when
                master is IConstructibleObjectGetter cobj2 =>
                CompareConstructibleObject(cobj1, cobj2),

            _ => CompareGenericRecord(current, master)
        };
    }

    private bool CompareLeveledItem(ILeveledItemGetter current,
        ILeveledItemGetter master)
    {
        if (current.Flags != master.Flags ||
            current.ChanceNone != master.ChanceNone) return false;

        var currentEntries = current.Entries?.ToList() ?? [];
        var masterEntries = master.Entries?.ToList() ?? [];

        if (currentEntries.Count != masterEntries.Count) return false;

        currentEntries = currentEntries
            .OrderBy(e => e.Data?.Reference.FormKey.ID ?? 0)
            .ThenBy(e => e.Data?.Level ?? 0)
            .ToList();
        masterEntries = masterEntries
            .OrderBy(e => e.Data?.Reference.FormKey.ID ?? 0)
            .ThenBy(e => e.Data?.Level ?? 0)
            .ToList();

        for (var i = 0; i < currentEntries.Count; i++)
        {
            var currentEntry = currentEntries[i].Data;
            var masterEntry = masterEntries[i].Data;

            if (currentEntry == null || masterEntry == null) return false;

            if (currentEntry.Level != masterEntry.Level ||
                currentEntry.Count != masterEntry.Count ||
                !CompareFormLinks(currentEntry.Reference, masterEntry.Reference))
                return false;
        }

        return true;
    }

    private bool CompareLeveledNpc(ILeveledNpcGetter current,
        ILeveledNpcGetter master)
    {
        if (current.Flags != master.Flags ||
            current.ChanceNone != master.ChanceNone) return false;

        var currentEntries = current.Entries?.ToList() ?? [];
        var masterEntries = master.Entries?.ToList() ?? [];

        if (currentEntries.Count != masterEntries.Count) return false;

        currentEntries = currentEntries
            .OrderBy(e => e.Data?.Reference.FormKey.ID ?? 0)
            .ThenBy(e => e.Data?.Level ?? 0)
            .ToList();
        masterEntries = masterEntries
            .OrderBy(e => e.Data?.Reference.FormKey.ID ?? 0)
            .ThenBy(e => e.Data?.Level ?? 0)
            .ToList();

        for (var i = 0; i < currentEntries.Count; i++)
        {
            var currentEntry = currentEntries[i].Data;
            var masterEntry = masterEntries[i].Data;

            if (currentEntry == null || masterEntry == null) return false;

            if (currentEntry.Level != masterEntry.Level ||
                currentEntry.Count != masterEntry.Count ||
                !CompareFormLinks(currentEntry.Reference, masterEntry.Reference))
                return false;
        }

        return true;
    }

    private bool CompareConstructibleObject(IConstructibleObjectGetter current,
        IConstructibleObjectGetter master)
    {
        if (!CompareFormLinks(current.WorkbenchKeyword, master.WorkbenchKeyword) ||
            !CompareFormLinks(current.CreatedObject, master.CreatedObject))
            return false;

        var currentComponents = current.Components?.ToList() ?? [];
        var masterComponents = master.Components?.ToList() ?? [];

        if (currentComponents.Count != masterComponents.Count) return false;

        currentComponents = currentComponents
            .OrderBy(c => c.Component.FormKey.ID)
            .ToList();
        masterComponents = masterComponents
            .OrderBy(c => c.Component.FormKey.ID)
            .ToList();

        return !currentComponents.Where((t, i) =>
                !CompareFormLinks(t.Component, masterComponents[i].Component) || t.Count != masterComponents[i].Count)
            .Any();
    }
}