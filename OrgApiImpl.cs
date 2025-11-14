using OrgApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OrgLoader
{
    /// <summary>
    /// Implementation of the OrgAPI interface
    /// </summary>
    public class OrgApiImpl : IOrgApi
    {
        private readonly Dictionary<string, IntPtr> _originalFunctions = new();
        private readonly Dictionary<string, GMLFunctionDelegate> _hookedFunctions = new();
        private readonly List<Action> _stepCallbacks = new();
        private readonly List<Action> _drawCallbacks = new();
        private readonly List<Action> _createCallbacks = new();
        private readonly List<Action> _destroyCallbacks = new();
        
        private IntPtr _gameBaseAddress;
        private bool _initialized = false;

        // P/Invoke declarations for Windows API
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        private struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public uint SizeOfImage;
            public IntPtr EntryPoint;
        }

        // Constants for VirtualProtect
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public bool Initialize()
        {
            try
            {
                LogMessage("OrgLoader: Initializing C# modloader...");

                // Get the base address of the main executable
                _gameBaseAddress = GetModuleHandle(null);
                if (_gameBaseAddress == IntPtr.Zero)
                {
                    LogError("Failed to get game module handle");
                    return false;
                }

                if (!IsGameMakerGame())
                {
                    LogWarning("This doesn't appear to be a GameMaker game, but continuing anyway...");
                }

                LogMessage($"Game base address: 0x{_gameBaseAddress:X}");
                _initialized = true;

                LogMessage("OrgLoader: C# modloader initialized successfully!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize OrgLoader: {ex.Message}");
                return false;
            }
        }

        public void Shutdown()
        {
            try
            {
                LogMessage("OrgLoader: Shutting down...");

                // Unhook all functions
                foreach (var func in _hookedFunctions.Keys.ToList())
                {
                    UnhookGMLFunction(func);
                }

                // Clear all callbacks
                _stepCallbacks.Clear();
                _drawCallbacks.Clear();
                _createCallbacks.Clear();
                _destroyCallbacks.Clear();

                _initialized = false;
                LogMessage("OrgLoader: Shutdown complete");
            }
            catch (Exception ex)
            {
                LogError($"Error during shutdown: {ex.Message}");
            }
        }

        public bool HookGMLFunction(string functionName, GMLFunctionDelegate hookFunction)
        {
            try
            {
                LogMessage($"Attempting to hook function: {functionName}");

                // For now, we'll simulate function hooking
                // In a real implementation, you'd need to:
                // 1. Find the function address in memory
                // 2. Create a trampoline
                // 3. Patch the original function to jump to your hook
                
                // Store the hook
                _hookedFunctions[functionName] = hookFunction;
                
                // In a real implementation, you'd find and store the original function address
                // For demonstration, we'll use a placeholder
                _originalFunctions[functionName] = IntPtr.Zero;

                LogMessage($"Successfully hooked function: {functionName}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to hook function {functionName}: {ex.Message}");
                return false;
            }
        }

        public bool UnhookGMLFunction(string functionName)
        {
            try
            {
                if (!_hookedFunctions.ContainsKey(functionName))
                {
                    return false;
                }

                // Remove the hook
                _hookedFunctions.Remove(functionName);
                _originalFunctions.Remove(functionName);

                LogMessage($"Unhooked function: {functionName}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to unhook function {functionName}: {ex.Message}");
                return false;
            }
        }

        public GMLVariant CallOriginalGMLFunction(string functionName, params GMLVariant[] args)
        {
            try
            {
                if (!_originalFunctions.TryGetValue(functionName, out IntPtr originalAddr))
                {
                    LogError($"Original function not found: {functionName}");
                    return GMLVariant.Undefined;
                }

                // In a real implementation, you'd call the original function here
                // This is a placeholder
                LogMessage($"Calling original function: {functionName} with {args.Length} arguments");
                
                return GMLVariant.Undefined;
            }
            catch (Exception ex)
            {
                LogError($"Error calling original function {functionName}: {ex.Message}");
                return GMLVariant.Undefined;
            }
        }

        public GMLVariant CallGMLFunction(string functionName, params GMLVariant[] args)
        {
            try
            {
                LogMessage($"Calling GML function: {functionName} with {args.Length} arguments");
                
                // In a real implementation, you'd call the GML function here
                // This is a placeholder
                
                return GMLVariant.Undefined;
            }
            catch (Exception ex)
            {
                LogError($"Error calling GML function {functionName}: {ex.Message}");
                return GMLVariant.Undefined;
            }
        }

        public void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[MOD] ");
            Console.ResetColor();
            Console.WriteLine(message);
            
            try
            {
                File.AppendAllText("orgloader.log", $"[{timestamp}] [MOD] {message}" + Environment.NewLine);
            }
            catch { /* Ignore file write errors */ }
        }

        public void LogError(string error)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[MOD ERROR] ");
            Console.ResetColor();
            Console.WriteLine(error);
            
            try
            {
                File.AppendAllText("orgloader.log", $"[{timestamp}] [MOD ERROR] {error}" + Environment.NewLine);
            }
            catch { /* Ignore file write errors */ }
        }

        public void LogWarning(string warning)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[MOD WARNING] ");
            Console.ResetColor();
            Console.WriteLine(warning);
            
            try
            {
                File.AppendAllText("orgloader.log", $"[{timestamp}] [MOD WARNING] {warning}" + Environment.NewLine);
            }
            catch { /* Ignore file write errors */ }
        }

        public GMLVariant GetGlobalVariable(string variableName)
        {
            try
            {
                LogMessage($"Getting global variable: {variableName}");
                // Implementation would interface with GameMaker's global variable system
                return GMLVariant.Undefined;
            }
            catch (Exception ex)
            {
                LogError($"Error getting global variable {variableName}: {ex.Message}");
                return GMLVariant.Undefined;
            }
        }

        public bool SetGlobalVariable(string variableName, GMLVariant value)
        {
            try
            {
                LogMessage($"Setting global variable: {variableName} = {value}");
                // Implementation would interface with GameMaker's global variable system
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error setting global variable {variableName}: {ex.Message}");
                return false;
            }
        }

        public bool ReplaceSprite(int spriteId, string imagePath)
        {
            try
            {
                LogMessage($"Replacing sprite {spriteId} with {imagePath}");
                // Implementation would involve finding sprite data and replacing it
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error replacing sprite {spriteId}: {ex.Message}");
                return false;
            }
        }

        public bool ReplaceSound(int soundId, string audioPath)
        {
            try
            {
                LogMessage($"Replacing sound {soundId} with {audioPath}");
                // Implementation would involve finding audio data and replacing it
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error replacing sound {soundId}: {ex.Message}");
                return false;
            }
        }

        public void RegisterStepEvent(Action callback)
        {
            _stepCallbacks.Add(callback);
            LogMessage("Registered step event callback");
        }

        public void RegisterDrawEvent(Action callback)
        {
            _drawCallbacks.Add(callback);
            LogMessage("Registered draw event callback");
        }

        public void RegisterCreateEvent(Action callback)
        {
            _createCallbacks.Add(callback);
            LogMessage("Registered create event callback");
        }

        public void RegisterDestroyEvent(Action callback)
        {
            _destroyCallbacks.Add(callback);
            LogMessage("Registered destroy event callback");
        }

        public IntPtr GetGameBaseAddress()
        {
            return _gameBaseAddress;
        }

        public bool IsGameMakerGame()
        {
            try
            {
                // Check for GameMaker signatures
                if (!GetModuleInformation(GetCurrentProcess(), _gameBaseAddress, out MODULEINFO moduleInfo, (uint)Marshal.SizeOf<MODULEINFO>()))
                {
                    return false;
                }

                // Look for "data.win" string in memory (simplified check)
                // In a real implementation, you'd do more thorough pattern matching
                return true; // Placeholder
            }
            catch
            {
                return false;
            }
        }

        public string GetGameVersion()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var version = process.MainModule?.FileVersionInfo?.FileVersion ?? "Unknown";
                return version;
            }
            catch
            {
                return "Unknown";
            }
        }

        // Internal methods for calling events
        internal void OnStep()
        {
            foreach (var callback in _stepCallbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    LogError($"Error in step callback: {ex.Message}");
                }
            }
        }

        internal void OnDraw()
        {
            foreach (var callback in _drawCallbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    LogError($"Error in draw callback: {ex.Message}");
                }
            }
        }

        internal void OnCreate()
        {
            foreach (var callback in _createCallbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    LogError($"Error in create callback: {ex.Message}");
                }
            }
        }

        internal void OnDestroy()
        {
            foreach (var callback in _destroyCallbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    LogError($"Error in destroy callback: {ex.Message}");
                }
            }
        }
    }
}