using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace PluginAnalyzer;
/// <summary>
/// A specialized plugin analyzer for Skyrim. This class analyzes a Skyrim plugin's records, performing tasks
/// such as identifying navigation meshes and placed objects.
/// </summary>
public class SkyrimPluginAnalyzer(ISkyrimModGetter plugin, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    : BasePluginAnalyzer<ISkyrimMod, ISkyrimModGetter>(plugin, linkCache)
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
            // Handle Skyrim-specific Form Lists
            case IFormListGetter flstCurrent when
                master is IFormListGetter flstMaster:
            {
                var currentItems = flstCurrent.Items.ToList();
                var masterItems = flstMaster.Items.ToList();

                if (currentItems.Count != masterItems.Count) return false;

                var sortedCurrent = currentItems.OrderBy(x => x.FormKey.ID).ToList();
                var sortedMaster = masterItems.OrderBy(x => x.FormKey.ID).ToList();

                return sortedCurrent.SequenceEqual(sortedMaster);
            }
            // Handle Skyrim-specific GMST
            case IGameSettingGetter gmstCurrent when
                master is IGameSettingGetter gmstMaster:
                return gmstCurrent.EditorID == gmstMaster.EditorID &&
                       gmstCurrent.ToString() == gmstMaster.ToString();
        }

        // Handle Skyrim-specific GLOB
        if (current is not IGlobalGetter globCurrent ||
            master is not IGlobalGetter globMaster) return false;
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

    protected override bool ShouldIgnoreForItmComparison(IMajorRecordGetter record)
    {
        return record switch
        {
            // Placed objects need specific handling to avoid pattern conflicts
            IPlacedArrowGetter => true, // PARW
            IPlacedBarrierGetter => true, // PBAR
            IPlacedBeamGetter => true, // PBEA
            IPlacedConeGetter => true, // PCON
            IPlacedFlameGetter => true, // PFLA
            IPlacedHazardGetter => true, // PHZD
            IPlacedMissileGetter => true, // PMIS
            IPlacedObjectGetter => true, // REFR
            IPlacedTrapGetter => true, // PTRA
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
            IDialogGetter => true, // DIAL
            IRelationshipGetter => true, // RELA
            IEncounterZoneGetter => true, // ECZN
            ILocationGetter => true, // LCTN
            ILocationReferenceTypeGetter => true, // LCRT
            IImageSpaceGetter => true, // IMGS
            IImageSpaceAdapterGetter => true, // IMAD
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

            ILeveledSpellGetter levSpellCurrent when
                master is ILeveledSpellGetter levSpellMaster =>
                CompareLeveledSpell(levSpellCurrent, levSpellMaster),

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

        // Sort entries by reference FormKey and level for consistent comparison
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

    private bool CompareLeveledSpell(ILeveledSpellGetter current,
        ILeveledSpellGetter master)
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
            !CompareFormLinks(current.CreatedObject, master.CreatedObject) ||
            current.CreatedObjectCount != master.CreatedObjectCount)
            return false;

        var currentItems = current.Items?.ToList() ?? [];
        var masterItems = master.Items?.ToList() ?? [];

        if (currentItems.Count != masterItems.Count) return false;

        currentItems = currentItems
            .OrderBy(i => i.Item.Item.FormKey.ID)
            .ToList();
        masterItems = masterItems
            .OrderBy(i => i.Item.Item.FormKey.ID)
            .ToList();

        return !currentItems.Where((t, i) => !CompareFormLinks(t.Item.Item, masterItems[i].Item.Item) || t.Item.Count != masterItems[i].Item.Count).Any();
    }
}