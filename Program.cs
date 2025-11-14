using OrgApi;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OrgLoader
{
    public class Program
    {
        private static ModLoader _modLoader;
        private static bool _isRunning = true;
        private static bool _isInjected = false;
        private static readonly string TARGET_GAME = "Ogre Chambers 2";
        private static readonly string[] TARGET_EXECUTABLES = { 
            "OgreChambers2.exe", 
            "ogre chambers 2.exe", 
            "ogre_chambers_2.exe", 
            "Ogre_Chambers_2222_Demo.exe", 
            "Ogre Chambers 2222 Demo.exe", 
            "Game.exe" 
        };

        // P/Invoke for DLL injection and console
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("user32.dll")]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint MEM_COMMIT = 0x00001000;
        private const uint PAGE_READWRITE = 0x04;

        public static void Main(string[] args)
        {
            try
            {
                // Check if we're being injected into the game process
                var currentProcess = Process.GetCurrentProcess();
                if (IsTargetGameProcess(currentProcess))
                {
                    // We're inside the game! Start the modloader
                    RunAsInjectedLoader();
                }
                else
                {
                    // We're the monitoring application, wait for the game to start
                    RunAsGameMonitor();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Critical error in OrgLoader: {ex.Message}");
            }
        }

        /// <summary>
        /// Run as the game monitor - watches for Ogre Chambers 2 to start
        /// </summary>
        private static void RunAsGameMonitor()
        {
            AllocConsole();
            Console.Title = "OrgLoader v1.0.0 - Ogre Chambers 2";

            // Show beautiful MelonLoader-style header immediately
            PrintHeader();

            // Create Mods folder and initialize ModLoader to show mod information
            try
            {
                LogInfo("Creating Mods directory if it doesn't exist...");
                var modsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "mods");
                if (!Directory.Exists(modsDirectory))
                {
                    Directory.CreateDirectory(modsDirectory);
                    LogInfo($"Created mods directory: {modsDirectory}");
                }
                else
                {
                    LogInfo($"Mods directory found: {modsDirectory}");
                }

                // Initialize and scan for mods to display their info
                LogInfo("Scanning for available mods...");
                _modLoader = new ModLoader();
                var modInitialized = _modLoader.Initialize();
                
                if (!modInitialized)
                {
                    LogWarning("ModLoader initialization had issues, but continuing...");
                }
                
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                      Game Monitor                           ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();
                
                LogInfo($"Waiting for '{TARGET_GAME}' to start...");
                LogInfo("Start your game normally - OrgLoader will inject automatically!");
                Console.WriteLine();
                
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Monitoring for executables:");
                foreach (var exe in TARGET_EXECUTABLES)
                {
                    Console.WriteLine($"  → {exe}");
                }
                Console.ResetColor();
                Console.WriteLine();
                LogInfo("Press Ctrl+C to exit...");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                LogError($"Error during initialization: {ex.Message}");
            }

            // Set up cancellation
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

            // Monitor for the game process
            Task.Run(() => MonitorForGame(cts.Token));

            // Keep running until cancelled
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }
            }
            catch (OperationCanceledException)
            {
                LogInfo("Shutting down monitor...");
            }

            FreeConsole();
        }

        /// <summary>
        /// Run as the injected modloader inside the game process
        /// </summary>
        private static void RunAsInjectedLoader()
        {
            try
            {
                AllocConsole();
                Console.Title = "OrgLoader v1.0.0 - Ogre Chambers 2";

                // MelonLoader-style header
                PrintHeader();

                _isInjected = true;

                // Initialize the mod loader
                _modLoader = new ModLoader();
                
                if (!_modLoader.Initialize())
                {
                    LogError("Failed to initialize modloader!");
                    return;
                }

                LogInfo("OrgLoader successfully initialized!");
                Console.WriteLine();

                // Start the main loop
                var mainThread = new Thread(MainLoop)
                {
                    IsBackground = false,
                    Name = "OrgLoader Main Thread"
                };
                mainThread.Start();

                // Set up shutdown handling
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                Console.CancelKeyPress += OnCancelKeyPress;

                // Wait for shutdown
                mainThread.Join();
            }
            catch (Exception ex)
            {
                LogError($"Error in injected loader: {ex.Message}");
            }
        }

        /// <summary>
        /// Print MelonLoader-style header
        /// </summary>
        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                                                              ║");
            Console.WriteLine("║                          OrgLoader                          ║");
            Console.WriteLine("║                        Version 1.0.0                       ║");
            Console.WriteLine("║                    Ogre Chambers 2 Edition                  ║");
            Console.WriteLine("║                                                              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            LogInfo("Detecting GameMaker Studio game...");
            LogInfo($"Game Process: {Process.GetCurrentProcess().ProcessName}");
            LogInfo($"Game Directory: {Directory.GetCurrentDirectory()}");
            LogInfo($"Unity Version: GameMaker Studio (VM)");
            Console.WriteLine();
        }

        /// <summary>
        /// MelonLoader-style logging functions
        /// </summary>
        public static void LogInfo(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("[INFO] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public static void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERROR] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public static void LogWarning(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[WARNING] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public static void LogDebug(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("[DEBUG] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        /// <summary>
        /// Monitor for the target game process to start
        /// </summary>
        private static async Task MonitorForGame(CancellationToken cancellationToken)
        {
            var lastCheck = DateTime.Now;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Check every 2 seconds
                    await Task.Delay(2000, cancellationToken);

                    // Show periodic status
                    if (DateTime.Now - lastCheck > TimeSpan.FromSeconds(15))
                    {
                        LogInfo($"Still monitoring for {TARGET_GAME}...");
                        lastCheck = DateTime.Now;
                    }

                    // Look for target game processes
                    foreach (var targetExe in TARGET_EXECUTABLES)
                    {
                        var processName = Path.GetFileNameWithoutExtension(targetExe);
                        var processes = Process.GetProcessesByName(processName);

                        foreach (var process in processes)
                        {
                            try
                            {
                                if (IsTargetGameProcess(process))
                                {
                                    Console.WriteLine();
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    LogInfo($"Game detected: {TARGET_GAME} (PID: {process.Id})");
                                    Console.ResetColor();
                                    LogInfo("Preparing injection...");

                                    if (await InjectIntoProcess(process))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        LogInfo("Injection successful! Modloader is now active in the game.");
                                        Console.ResetColor();
                                        LogInfo("Modloader will continue running in the background.");
                                        Console.WriteLine();
                                        Console.ForegroundColor = ConsoleColor.DarkGray;
                                        Console.WriteLine("Monitor will continue running for potential game restarts...");
                                        Console.WriteLine("Press Ctrl+C to stop monitoring.");
                                        Console.ResetColor();
                                        Console.WriteLine();
                                        return; // Successfully injected, stop monitoring
                                    }
                                    else
                                    {
                                        LogError("Injection failed, continuing to monitor...");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogWarning($"Error checking process {process.ProcessName}: {ex.Message}");
                            }
                            finally
                            {
                                process.Dispose();
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Error in monitoring loop: {ex.Message}");
                    await Task.Delay(5000, cancellationToken); // Wait longer on error
                }
            }
        }

        /// <summary>
        /// Check if a process is our target game
        /// </summary>
        private static bool IsTargetGameProcess(Process process)
        {
            try
            {
                var processName = process.ProcessName.ToLower();
                var windowTitle = process.MainWindowTitle?.ToLower() ?? "";
                
                // Check executable name
                if (TARGET_EXECUTABLES.Any(exe => processName.Contains(Path.GetFileNameWithoutExtension(exe).ToLower())))
                {
                    return true;
                }

                // Check window title
                if (windowTitle.Contains("ogre") && windowTitle.Contains("chambers"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Inject the modloader DLL into the target process
        /// </summary>
        private static async Task<bool> InjectIntoProcess(Process targetProcess)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule.FileName;
                
                // Open the target process
                IntPtr processHandle = OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | 
                    PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                    false, targetProcess.Id);

                if (processHandle == IntPtr.Zero)
                {
                    Console.WriteLine("❌ Failed to open target process");
                    return false;
                }

                try
                {
                    // Get LoadLibrary address
                    IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                    IntPtr loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryA");

                    if (loadLibraryAddr == IntPtr.Zero)
                    {
                        Console.WriteLine("❌ Failed to get LoadLibrary address");
                        return false;
                    }

                    // Allocate memory in target process
                    byte[] dllPathBytes = System.Text.Encoding.ASCII.GetBytes(currentExePath + "\0");
                    IntPtr allocMemAddr = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)dllPathBytes.Length, MEM_COMMIT, PAGE_READWRITE);

                    if (allocMemAddr == IntPtr.Zero)
                    {
                        Console.WriteLine("❌ Failed to allocate memory in target process");
                        return false;
                    }

                    // Write DLL path to target process
                    bool writeResult = WriteProcessMemory(processHandle, allocMemAddr, dllPathBytes, (uint)dllPathBytes.Length, out _);
                    if (!writeResult)
                    {
                        Console.WriteLine("❌ Failed to write DLL path to target process");
                        return false;
                    }

                    // Create remote thread to load the DLL
                    IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddr, 0, IntPtr.Zero);
                    if (threadHandle == IntPtr.Zero)
                    {
                        Console.WriteLine("❌ Failed to create remote thread");
                        return false;
                    }

                    CloseHandle(threadHandle);
                    Console.WriteLine("✅ Remote thread created successfully");
                    return true;
                }
                finally
                {
                    CloseHandle(processHandle);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Injection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Main game loop when running as injected modloader
        /// </summary>
        private static void MainLoop()
        {
            try
            {
                Console.WriteLine("🔄 Main loop started");
                
                while (_isRunning)
                {
                    try
                    {
                        // Call step events for all mods
                        _modLoader?.GetApi()?.OnStep();
                        
                        // Small delay to prevent excessive CPU usage
                        Thread.Sleep(16); // ~60 FPS
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Error in main loop: {ex.Message}");
                        Thread.Sleep(100); // Longer delay if there's an error
                    }
                }
                
                Console.WriteLine("🔄 Main loop ended");
            }
            catch (Exception ex)
            {
                ShowError($"Fatal error in main loop: {ex.Message}");
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Shutdown();
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Prevent immediate termination
            Shutdown();
        }

        public static void Shutdown()
        {
            if (!_isRunning) return;
            
            Console.WriteLine("🔄 OrgLoader shutting down...");
            _isRunning = false;
            
            try
            {
                _modLoader?.Shutdown();
                Console.WriteLine("✅ Shutdown complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error during shutdown: {ex.Message}");
            }
        }

        private static void ShowInfo(string message)
        {
            Console.WriteLine($"ℹ️  {message}");
            try
            {
                if (!_isInjected)
                {
                    MessageBox(IntPtr.Zero, message, "OrgLoader", 0x40); // MB_ICONINFORMATION
                }
            }
            catch { /* Ignore if MessageBox fails */ }
        }

        private static void ShowError(string message)
        {
            Console.WriteLine($"❌ {message}");
            try
            {
                MessageBox(IntPtr.Zero, message, "OrgLoader Error", 0x10); // MB_ICONERROR
            }
            catch { /* Ignore if MessageBox fails */ }
        }
    }
}