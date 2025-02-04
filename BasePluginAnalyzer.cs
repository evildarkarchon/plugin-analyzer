using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace PluginAnalyzer;

public abstract class BasePluginAnalyzer<TMod, TModGetter>(TModGetter plugin, ILinkCache<TMod, TModGetter> linkCache)
    where TMod : class, IMod, TModGetter
    where TModGetter : class, IModGetter
{
    protected abstract bool IsNavmesh(IMajorRecordGetter record);
    protected abstract bool IsPlaced(IMajorRecordGetter record);
    protected abstract bool CompareSpecialRecords(IMajorRecordGetter current, IMajorRecordGetter master);
    protected abstract bool ShouldIgnoreForItmComparison(IMajorRecordGetter record);
    protected abstract bool CompareGameSpecificRecord(IMajorRecordGetter current, IMajorRecordGetter master);

    protected readonly ILinkCache<TMod, TModGetter> LinkCache = linkCache;

    protected bool CompareGenericRecord(IMajorRecordGetter current, IMajorRecordGetter master)
    {
        if (current.GetType() != master.GetType())
            return false;

        // Compare properties using reflection
        foreach (var property in current.GetType().GetProperties())
        {
            // Get property values
            var currentValue = property.GetValue(current);
            var masterValue = property.GetValue(master);

            // If they're both null, treat as identical
            if (currentValue == null && masterValue == null)
                continue;

            // If either one is null, or if the values differ, not identical
            if (currentValue == null || masterValue == null || !currentValue.Equals(masterValue))
                return false;
        }

        return true; // All properties matched
    }

    private bool AreRecordsIdentical(IMajorRecordGetter current, IMajorRecordGetter master)
    {
        if (current.GetType() != master.GetType()) return false;

        // Basic equivalency checks
        if (current.GetType() != master.GetType()) return false;
        if (current.EditorID != master.EditorID) return false;
        if (current.IsDeleted != master.IsDeleted) return false;
        if (current.IsCompressed != master.IsCompressed) return false;

        if (CompareSpecialRecords(current, master)) return true;
        
        if (CompareGameSpecificRecord(current, master)) return true;

        // Cells and other special records that need binary comparison
        return !ShouldIgnoreForItmComparison(current) && CompareGenericRecord(current, master);
    }

    public async Task<AnalysisResults> Analyze()
    {
        var results = new AnalysisResults { PluginName = plugin.ModKey.FileName };

        await Task.Run(() =>
        {
            foreach (var record in plugin.EnumerateMajorRecords())
            {
                if (record.IsDeleted)
                {
                    if (IsNavmesh(record)) results.DeletedNavmeshesCount++;
                    else if (IsPlaced(record)) results.DeletedReferencesCount++;
                }

                var formKey = record.FormKey;
                if (!LinkCache.TryResolve<IMajorRecordGetter>(formKey, out var winning)) continue;

                var winningModKey = winning.FormKey.ModKey;
                if (winningModKey != plugin.ModKey && !ShouldIgnoreForItmComparison(record))
                {
                    // Get the original master record this overrides
                    if (TryGetMasterRecord(record, out var masterRecord))
                    {
                        if (AreRecordsIdentical(record, masterRecord))
                        {
                            results.IdenticalToMasterCount++;
                        }
                    }
                }

                if (record.FormKey.ID > winning.FormKey.ID)
                {
                    results.HigherIndexCount++;
                }
            }
        });
        return results;
    }

    private bool TryGetMasterRecord(IMajorRecordGetter record, [NotNullWhen(true)] out IMajorRecordGetter? masterRecord)
    {
        masterRecord = null;

        // Get all masters for this plugin
        var masters = plugin.MasterReferences.Select(m => m.Master).ToList();

        // Check each master in reverse order (latest master first)
        for (var i = masters.Count - 1; i >= 0; i--)
        {
            var master = masters[i];
            var masterFormKey = new FormKey(master, record.FormKey.ID);

            if (LinkCache.TryResolve<IMajorRecordGetter>(masterFormKey, out masterRecord))
            {
                return true;
            }
        }

        return false;
    }

    protected bool CompareFormLinks<T>(IFormLinkGetter<T>? current, IFormLinkGetter<T>? master)
        where T : class, IMajorRecordGetter
    {
        if (current == null && master == null) return true;
        if (current == null || master == null) return false;

        // If form IDs are identical, no need to resolve
        if (current.FormKey == master.FormKey) return true;

        // If different, try to resolve both and compare the actual records
        if (!current.TryResolve(LinkCache, out var currentRecord)) return false;
        if (!master.TryResolve(LinkCache, out var masterRecord)) return false;

        return currentRecord.FormKey == masterRecord.FormKey;
    }
}