using System.Diagnostics.CodeAnalysis;
using CommandLine;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Noggog;

namespace PluginAnalyzer;

/// <summary>
/// Represents the options and parameters that can be passed to the application for analyzing plugins.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class Options
{
    [Value(0, MetaName = "plugin", HelpText = "The plugin file to analyze")]
    public string? Plugin { get; set; }

    [Option('p', "plugin", HelpText = "The plugin file to analyze")]
    public string? PluginOption { get; set; }

    [Option('g', "game", HelpText = "The game type (e.g., SkyrimSE, Fallout4)")]
    public GameRelease? Game { get; set; }

    [Option("game-path", HelpText = "Path to game directory or Data folder")]
    public string? GamePath { get; set; }
}

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public class Program
{
    /// <summary>
    /// The entry point of the application.
    /// Parses command-line arguments and performs plugin analysis based on the provided options.
    /// </summary>
    /// <param name="args">An array of command-line arguments passed to the application.</param>
    /// <returns>
    /// An asynchronous task that returns an integer exit code.
    /// The return value is 0 for successful execution or a non-zero value in case of errors.
    /// </returns>
    private static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
            .MapResult(async opts =>
                {
                    try
                    {
                        var pluginPath = opts.PluginOption ?? opts.Plugin;
                        if (string.IsNullOrEmpty(pluginPath))
                        {
                            Console.WriteLine("Error: Plugin file or directory must be specified.");
                            return 1;
                        }

                        // Check if the path is a directory or a file
                        if (Directory.Exists(pluginPath))
                        {
                            var directory = new DirectoryInfo(pluginPath);
                            Console.WriteLine($"Analyzing all plugins in directory: {directory.FullName}");

                            // Fix file filter to be case-insensitive and more robust
                            var pluginFiles = directory.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                                .Where(file => file.Extension.Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
                                               file.Extension.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                                               file.Extension.Equals(".esl", StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                            if (pluginFiles.Length == 0)
                            {
                                Console.WriteLine("No plugin files (.esp or .esm) found in the directory.");
                                return 1;
                            }

                            foreach (var pluginFile in pluginFiles)
                            {
                                Console.WriteLine($"\nAnalyzing plugin: {pluginFile.Name}");
                                var gamePath = opts.GamePath != null ? new DirectoryInfo(opts.GamePath) : null;
                                var detectedGame = opts.Game ?? DetectGame(gamePath);

                                if (detectedGame == null)
                                {
                                    Console.WriteLine(
                                        "Error: Could not determine game type. Please specify --game or provide a valid --game-path.");
                                    continue; // Skip this plugin if game type cannot be determined
                                }

                                await AnalyzePlugin(pluginFile, detectedGame.Value);
                            }
                        }
                        else if (File.Exists(pluginPath))
                        {
                            // Handle file case: Single plugin
                            var pluginFile = new FileInfo(pluginPath);
                            var gamePath = opts.GamePath != null ? new DirectoryInfo(opts.GamePath) : null;
                            var detectedGame = opts.Game ?? DetectGame(gamePath);

                            if (detectedGame == null)
                            {
                                Console.WriteLine(
                                    "Error: Could not determine game type. Please specify --game or provide a valid --game-path.");
                                return 1;
                            }

                            await AnalyzePlugin(pluginFile, detectedGame.Value);
                        }
                        else
                        {
                            Console.WriteLine("Error: Specified plugin path is invalid.");
                            return 1;
                        }

                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        if (ex.InnerException != null)
                            Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                        return 1;
                    }
                },
                _ => Task.FromResult(1));
    }

    /// <summary>
    /// Detects the game version based on the provided directory path.
    /// Validates the presence of specific game files or directories to ascertain the game type.
    /// </summary>
    /// <param name="path">The directory path to analyze for game detection. Can be null.</param>
    /// <returns>
    /// A nullable GameRelease enum value representing the detected game version,
    /// or null if the game could not be identified.
    /// </returns>
    private static GameRelease? DetectGame(DirectoryInfo? path)
    {
        if (path == null) return null;

        var dataPath = path.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)
            ? path
            : path.GetDirectories("Data").FirstOrDefault();

        if (dataPath == null) return null;

        var parentPath = dataPath.Parent!.FullName;
        if (File.Exists(Path.Combine(parentPath, "SkyrimSE.exe")))
        {
            if (Directory.Exists(Path.Combine(parentPath, "gogscripts")) ||
                File.Exists(Path.Combine(parentPath, "goggame-1746476928.info")))
            {
                return GameRelease.SkyrimSEGog;
            }

            return GameRelease.SkyrimSE;
        }

        if (File.Exists(Path.Combine(parentPath, "TESV.exe")))
            return GameRelease.SkyrimLE;
        if (File.Exists(Path.Combine(parentPath, "Fallout4.exe")))
            return GameRelease.Fallout4;
        if (File.Exists(Path.Combine(parentPath, "Fallout4VR.exe")))
            return GameRelease.Fallout4VR;
        if (File.Exists(Path.Combine(parentPath, "Oblivion.exe")))
            return GameRelease.Oblivion;

        var masters = new Dictionary<string, GameRelease>
        {
            { "Skyrim.esm", GameRelease.SkyrimLE },
            { "Fallout4.esm", GameRelease.Fallout4 },
            { "Oblivion.esm", GameRelease.Oblivion }
        };

        foreach (var master in masters)
        {
            string? choice;
            switch (master.Key)
            {
                case "Skyrim.esm" when File.Exists(Path.Combine(dataPath.FullName, master.Key)):
                    Console.WriteLine("Could not detect Skyrim version, Which version do you want to analyze?");
                    Console.WriteLine("1. Skyrim SE (Steam) *Default*");
                    Console.WriteLine("2. Skyrim LE");
                    Console.WriteLine("3. Skyrim SE (GOG)");
                    Console.WriteLine("4. Skyrim VR");
                    Console.Write("Enter your choice: ");
                    choice = Console.ReadLine();
                    return choice switch
                    {
                        "1" => GameRelease.SkyrimSE,
                        "2" => GameRelease.SkyrimLE,
                        "3" => GameRelease.SkyrimSEGog,
                        "4" => GameRelease.SkyrimVR,
                        _ => GameRelease.SkyrimSE
                    };
                case "Fallout4.esm" when File.Exists(Path.Combine(dataPath.FullName, master.Key)):
                    Console.WriteLine("Could not detect Fallout 4 version, Which version do you want to analyze?");
                    Console.WriteLine("1. Fallout 4 *Default*");
                    Console.WriteLine("2. Fallout 4 VR");
                    Console.Write("Enter your choice: ");
                    choice = Console.ReadLine();
                    return choice switch
                    {
                        "1" => GameRelease.Fallout4,
                        "2" => GameRelease.Fallout4VR,
                        _ => GameRelease.Fallout4
                    };
            }

            if (File.Exists(Path.Combine(dataPath.FullName, master.Key)))
            {
                return master.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Analyzes a given plugin file for the specified game release, extracting relevant information and producing analysis results.
    /// </summary>
    /// <param name="pluginFile">The plugin file to be analyzed, represented as a <see cref="FileInfo"/> object.</param>
    /// <param name="gameRelease">The release version of the game for which the plugin is intended, represented as a <see cref="GameRelease"/> enum.</param>
    /// <returns>
    /// An asynchronous task representing the analysis process. The method does not return a value upon completion,
    /// but outputs results or error information during the analysis.
    /// </returns>
    private static async Task AnalyzePlugin(FileInfo pluginFile, GameRelease gameRelease)
    {
        if (!pluginFile.Exists)
        {
            Console.WriteLine($"Error: File {pluginFile.FullName} does not exist.");
            return;
        }

        // Skip main master plugins for each game
        if (gameRelease == GameRelease.Fallout4 || gameRelease == GameRelease.Fallout4VR &&
            pluginFile.Name.Equals("Fallout4.esm", StringComparison.OrdinalIgnoreCase) ||
            gameRelease is GameRelease.SkyrimLE or GameRelease.SkyrimSE or GameRelease.SkyrimVR
                or GameRelease.SkyrimSEGog
            && pluginFile.Name.Equals("Skyrim.esm", StringComparison.OrdinalIgnoreCase) ||
            gameRelease == GameRelease.Oblivion &&
            pluginFile.Name.Equals("Oblivion.esm", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Skipping main master plugin: {pluginFile.Name}");
            return;
        }

        var modKey = ModKey.FromFileName(pluginFile.Name);
        try
        {
            var dataFolderPath = new DirectoryPath(pluginFile.Directory?.FullName ??
                                                   throw new InvalidOperationException(
                                                       "Plugin directory path is null"));

            AnalysisResults results;
            switch (gameRelease)
            {
                case GameRelease.SkyrimLE:
                case GameRelease.SkyrimSE:
                case GameRelease.SkyrimVR:
                case GameRelease.SkyrimSEGog:
                    var loadOrderSkyrim = LoadOrder.Import<ISkyrimModGetter>(
                        dataFolderPath,
                        [new ModListing(modKey, enabled: true, existsOnDisk: true)],
                        gameRelease);

                    var modsSkyrim = loadOrderSkyrim.ListedOrder
                        .Select(x => x.Mod)
                        .Where(x => x != null)
                        .ToList();

                    var pluginSkyrim = modsSkyrim.FirstOrDefault(x => x?.ModKey == modKey) ??
                                       throw new InvalidOperationException(
                                           $"Could not find plugin {modKey} in load order");

                    var cacheSkyrim = loadOrderSkyrim.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
                    results = await new SkyrimPluginAnalyzer(pluginSkyrim, cacheSkyrim).Analyze();
                    break;

                case GameRelease.Fallout4 or GameRelease.Fallout4VR:
                    var loadOrderFallout = LoadOrder.Import<IFallout4ModGetter>(
                        dataFolderPath,
                        [new ModListing(modKey, enabled: true, existsOnDisk: true)],
                        gameRelease);

                    var modsFallout = loadOrderFallout.ListedOrder
                        .Select(x => x.Mod)
                        .Where(x => x != null)
                        .ToList();

                    var pluginFallout = modsFallout.FirstOrDefault(x => x?.ModKey == modKey) ??
                                        throw new InvalidOperationException(
                                            $"Could not find plugin {modKey} in load order");

                    var cacheFallout = loadOrderFallout.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
                    results = await new Fallout4PluginAnalyzer(pluginFallout, cacheFallout).Analyze();
                    break;

                case GameRelease.Oblivion:
                    var loadOrderOblivion = LoadOrder.Import<IOblivionModGetter>(
                        dataFolderPath,
                        [new ModListing(modKey, enabled: true, existsOnDisk: true)],
                        gameRelease);

                    var modsOblivion = loadOrderOblivion.ListedOrder
                        .Select(x => x.Mod)
                        .Where(x => x != null)
                        .ToList();

                    var pluginOblivion = modsOblivion.FirstOrDefault(x => x?.ModKey == modKey) ??
                                         throw new InvalidOperationException(
                                             $"Could not find plugin {modKey} in load order");

                    var cacheOblivion = loadOrderOblivion.ToImmutableLinkCache<IOblivionMod, IOblivionModGetter>();
                    results = await new OblivionPluginAnalyzer(pluginOblivion, cacheOblivion).Analyze();
                    break;

                case GameRelease.EnderalLE:
                case GameRelease.EnderalSE:
                case GameRelease.Starfield:
                default:
                    throw new NotSupportedException($"Game {gameRelease} is not supported");
            }

            PrintResults(results);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error analyzing plugin: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner error: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Prints the analysis results of a plugin to the console.
    /// Displays detailed statistics about the plugin, such as identical records,
    /// deleted entries, and higher index entries.
    /// </summary>
    /// <param name="results">The analysis results of the plugin, providing detailed metrics.</param>
    private static void PrintResults(AnalysisResults results)
    {
        Console.WriteLine($"Analysis Results for {results.PluginName}:");
        Console.WriteLine($"Identical to Master Records: {results.IdenticalToMasterCount}");
        Console.WriteLine($"Deleted References: {results.DeletedReferencesCount}");
        Console.WriteLine($"Deleted Navmeshes: {results.DeletedNavmeshesCount}");
        Console.WriteLine($"Higher Index Than Master Entries (HITMEs): {results.HigherIndexCount}");
    }
}

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

/// <summary>
/// Represents the results of a plugin analysis, providing details about the analyzed plugin
/// and various counts related to the plugin's properties and records.
/// </summary>
public class AnalysisResults
{
    public required string PluginName { get; init; }
    public int IdenticalToMasterCount { get; set; }
    public int DeletedReferencesCount { get; set; }
    public int DeletedNavmeshesCount { get; set; }
    public int HigherIndexCount { get; set; }
}