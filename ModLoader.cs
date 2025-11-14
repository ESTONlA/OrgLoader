using OrgApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OrgLoader
{
    public class ModInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string MainDll { get; set; } = "mod.dll";
        public string[] Dependencies { get; set; } = Array.Empty<string>();
        public string[] GameVersions { get; set; } = Array.Empty<string>();
    }

    public class ModLoader
    {
        private readonly OrgApiImpl _api;
        private readonly List<Assembly> _loadedModAssemblies = new();
        private readonly List<OrgMod> _loadedMods = new();
        private readonly Dictionary<string, ModInfo> _modInfos = new();

        public ModLoader()
        {
            _api = new OrgApiImpl();
        }

        public bool Initialize()
        {
            try
            {
                LogMessage("Initializing OrgLoader...");

                if (!_api.Initialize())
                {
                    LogError("Failed to initialize API");
                    return false;
                }

                // Set the global API instance
                OrgAPI.SetInstance(_api);

                LogMessage("Searching for mods...");

                // Load all mods
                if (!LoadAllMods())
                {
                    LogError("Failed to load mods");
                    return false;
                }

                if (_loadedMods.Count > 0)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    LogMessage($"Successfully loaded {_loadedMods.Count} mod(s)");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    LogMessage("No mods found. Place mod DLLs in the 'mods' folder.");
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                LogMessage("OrgLoader initialization complete!");
                Console.ResetColor();
                Console.WriteLine("".PadRight(70, '='));
                Console.WriteLine();

                return true;
            }
            catch (Exception ex)
            {
                LogError($"ModLoader initialization failed: {ex.Message}");
                return false;
            }
        }

        public void Shutdown()
        {
            try
            {
                LogMessage("Shutting down modloader...");

                // Shutdown all mods in reverse order
                for (int i = _loadedMods.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var modName = _loadedMods[i].GetModName();
                        LogMessage($"Unloading {modName}...");
                        _loadedMods[i].Shutdown();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error shutting down mod {_loadedMods[i].GetModName()}: {ex.Message}");
                    }
                }

                _loadedMods.Clear();
                _loadedModAssemblies.Clear();
                _modInfos.Clear();

                _api.Shutdown();
                LogMessage("Modloader shutdown complete");
            }
            catch (Exception ex)
            {
                LogError($"Error during shutdown: {ex.Message}");
            }
        }

        private bool LoadAllMods()
        {
            try
            {
                var modsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "mods");
                
                if (!Directory.Exists(modsDirectory))
                {
                    LogMessage("Mods directory doesn't exist, creating it...");
                    Directory.CreateDirectory(modsDirectory);
                    LogMessage($"Created mods directory: {modsDirectory}");
                    return true;
                }

                LogMessage($"Scanning mods directory: {modsDirectory}");

                // Scan for .dll files directly in the mods folder
                var dllFiles = Directory.GetFiles(modsDirectory, "*.dll");
                
                if (dllFiles.Length == 0)
                {
                    LogMessage("No mod DLL files found in mods folder");
                    LogMessage("Place your mod .dll files directly in the 'mods' folder");
                    return true;
                }

                LogMessage($"Found {dllFiles.Length} potential mod DLL(s)");

                foreach (var dllPath in dllFiles)
                {
                    try
                    {
                        LoadMod(dllPath);
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to load mod from {Path.GetFileName(dllPath)}: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error scanning mods directory: {ex.Message}");
                return false;
            }
        }

        private void LoadMod(string dllPath)
        {
            var dllFileName = Path.GetFileName(dllPath);
            var modName = Path.GetFileNameWithoutExtension(dllPath);
            LogMessage($"Loading {dllFileName}...");

            // Look for optional manifest.json with same name as DLL
            var manifestPath = Path.Combine(Path.GetDirectoryName(dllPath), $"{modName}.json");
            ModInfo modInfo = null;

            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    modInfo = JsonSerializer.Deserialize<ModInfo>(manifestJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    LogMessage($"Using manifest: {Path.GetFileName(manifestPath)}");
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to parse manifest for {modName}: {ex.Message}");
                }
            }

            // Use defaults if no manifest found
            modInfo ??= new ModInfo { Name = modName, MainDll = dllFileName };

            // Verify the DLL file exists
            if (!File.Exists(dllPath))
            {
                LogError($"Mod DLL not found: {dllPath}");
                return;
            }

            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                _loadedModAssemblies.Add(assembly);

                // Find mod classes
                var modTypes = assembly.GetTypes()
                    .Where(t => typeof(OrgMod).IsAssignableFrom(t) && !t.IsAbstract)
                    .ToList();

                if (modTypes.Count == 0)
                {
                    LogError($"No mod classes found in {dllPath}");
                    return;
                }

                if (modTypes.Count > 1)
                {
                    LogWarning($"Multiple mod classes found in {dllPath}, using the first one");
                }

                var modType = modTypes[0];
                var modInstance = (OrgMod)Activator.CreateInstance(modType);
                
                if (modInstance == null)
                {
                    LogError($"Failed to create instance of {modType.Name}");
                    return;
                }

                // Extract OrgMod attribute information
                var orgModAttribute = modType.GetCustomAttribute<OrgModAttribute>();
                
                string displayName = orgModAttribute?.Name ?? modInstance.GetModName();
                string version = orgModAttribute?.Version ?? modInstance.GetModVersion();
                string author = orgModAttribute?.Author ?? modInstance.GetModAuthor();
                string description = orgModAttribute?.Description ?? modInstance.GetModDescription();

                // Display MelonLoader-style mod info
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║ Loading Mod: {displayName.PadRight(44)} ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
                Console.ResetColor();
                Console.WriteLine($"║ Name:        {displayName.PadRight(44)} ║");
                Console.WriteLine($"║ Author:      {author.PadRight(44)} ║");
                Console.WriteLine($"║ Version:     {version.PadRight(44)} ║");
                if (!string.IsNullOrEmpty(description))
                {
                    // Split long descriptions
                    var descLines = SplitDescription(description, 44);
                    Console.WriteLine($"║ Description: {descLines[0].PadRight(44)} ║");
                    for (int i = 1; i < descLines.Count; i++)
                    {
                        Console.WriteLine($"║              {descLines[i].PadRight(44)} ║");
                    }
                }
                Console.WriteLine($"║ Assembly:    {Path.GetFileName(dllPath).PadRight(44)} ║");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                // Set API and initialize
                modInstance.SetApi(_api);
                
                LogMessage($"Initializing {displayName}...");
                
                if (!modInstance.Initialize())
                {
                    LogError($"Failed to initialize mod: {displayName}");
                    return;
                }

                _loadedMods.Add(modInstance);
                _modInfos[displayName] = modInfo;

                Console.ForegroundColor = ConsoleColor.Green;
                LogMessage($"Successfully loaded {displayName} v{version} by {author}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                LogError($"Failed to load mod assembly {dllPath}: {ex.Message}");
            }
        }

        private List<string> SplitDescription(string description, int maxLength)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(description))
            {
                lines.Add("");
                return lines;
            }

            var words = description.Split(' ');
            var currentLine = "";

            foreach (var word in words)
            {
                if ((currentLine + " " + word).Length <= maxLength)
                {
                    currentLine += (currentLine.Length > 0 ? " " : "") + word;
                }
                else
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        // Word is too long, truncate it
                        lines.Add(word.Substring(0, Math.Min(word.Length, maxLength)));
                        currentLine = "";
                    }
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine);
            }

            if (lines.Count == 0)
            {
                lines.Add("");
            }

            return lines;
        }

        
        // MelonLoader-style logging helpers
        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("[INFO] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERROR] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        private void LogWarning(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[WARNING] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public OrgApiImpl GetApi() => _api;
        
        public IReadOnlyList<OrgMod> GetLoadedMods() => _loadedMods.AsReadOnly();
        
        public IReadOnlyDictionary<string, ModInfo> GetModInfos() => _modInfos.AsReadOnly();
    }
}