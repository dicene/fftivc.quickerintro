using fftivc.quickerintro.Configuration;
using fftivc.quickerintro.Template;
using fftivc.utility.modloader.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

#if DEBUG
using System.Diagnostics;
using System.Drawing;
#endif

namespace fftivc.quickerintro
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        /// <summary>
        /// Writes a diagnostic message to the Reloaded log, gated on the Debug Logging setting.
        /// </summary>
        private void LogDebug(string message)
        {
            if (_configuration.DebugLoggingEnabled)
                _logger.WriteLine($"[{_modConfig.ModId}] {message}");
        }

        /// <summary>
        /// Writes a coloured diagnostic message to the Reloaded log, gated on the Debug Logging setting.
        /// </summary>
        private void LogDebug(string message, Color color)
        {
            if (_configuration.DebugLoggingEnabled)
                _logger.WriteLineAsync($"[{_modConfig.ModId}] {message}", color);
        }

        [Function(CallingConventions.Microsoft)]
        private delegate byte UIAgentFFTOLauncher_ClearGameMode(UIntPtr UIAgentFFTOLauncher, byte a2);
        private IHook<UIAgentFFTOLauncher_ClearGameMode> UIAgentFFTOLauncher_ClearGameMode_Hook;

        private byte UIAgentFFTOLauncher_ClearGameMode_Replacement(UIntPtr UIAgentFFTOLauncher, byte a2)
        {
            var startingGameMode = Memory.Instance.Read<byte>(UIAgentFFTOLauncher + 0x4A);
            var ret = UIAgentFFTOLauncher_ClearGameMode_Hook.OriginalFunction(UIAgentFFTOLauncher, a2);
            var updatedGameMode = Memory.Instance.Read<byte>(UIAgentFFTOLauncher + 0x4A);

            LogDebug($"UIAgentFFTOLauncher_ClearGameMode: UIAgentFFTOLauncher:0x{UIAgentFFTOLauncher:X}, a2:0x{a2:X}, return:0x{ret}. GameMode from {startingGameMode} -> {updatedGameMode}.");

            if (_configuration.AutoModeSelectionEnabled)
            {
                LogDebug($"Automatically selecting: {_configuration.AutoModeSelection}");

                Memory.Instance.SafeWrite(UIAgentFFTOLauncher + 0x4A, new Span<byte>([(byte)_configuration.AutoModeSelection]));
            }

            return ret;
        }

        [Function(CallingConventions.Microsoft)]
        private delegate void InitMovie();
        private IHook<InitMovie> InitMovie_Hook;

        private void InitMovie_Replacement()
        {
            if (_configuration.SkipIntroMovieEnabled)
            {
                LogDebug($"Skipping intro movie.");
                return;
            }

            InitMovie_Hook.OriginalFunction();
            return;
        }

        // Title menu row tags (confirmed at runtime):
        //   tag 0 = New Game, tag 2 = Continue, tag 3 = Load Game, tag 5 = Select Version.
        private const int ContinueRowTag = 2;
        private const int LoadGameRowTag = 3;

        // ffto::Game::UI::UIAgentFFTOTitle_HandleSlotSelectionByMode(agent, mode)
        // The action the title menu runs when a row is activated (the row's tag is passed as `mode`).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HandleSlotSelectionByMode(UIntPtr agent, int mode);
        private HandleSlotSelectionByMode? _handleSlotSelectionByMode;

        // ffto::Game::UI::UIAgentFFTOTitle_RefreshTitleSlotList - runs when the title menu is (re)built.
        [Function(CallingConventions.Microsoft)]
        private delegate void RefreshTitleSlotList(UIntPtr agent);
        private IHook<RefreshTitleSlotList> RefreshTitleSlotList_Hook;

        private bool _autoOpenLoadGameMenuTriggered;

        private void RefreshTitleSlotList_Replacement(UIntPtr agent)
        {
            RefreshTitleSlotList_Hook.OriginalFunction(agent);

            if (_autoOpenLoadGameMenuTriggered || _handleSlotSelectionByMode == null)
                return;

            // Continue takes precedence when both toggles are enabled.
            var rowTag = _configuration.AutoContinueEnabled ? ContinueRowTag
                       : _configuration.AutoOpenLoadGameMenuEnabled ? LoadGameRowTag
                       : -1;
            if (rowTag < 0)
                return;

            // Only act once the title menu is idle (mode byte at agent+0x58 == 0).
            if (Memory.Instance.Read<byte>(agent + 0x58) != 0)
                return;

            _autoOpenLoadGameMenuTriggered = true;
            LogDebug($"AutoOpenLoadGameMenu: activating the {(rowTag == ContinueRowTag ? "Continue" : "Load Game")} row (tag {rowTag}).");
            _handleSlotSelectionByMode(agent, rowTag);
        }

        // ffto::Game::UI::UIAddonFFTOSaveLoad_RebuildSlotListForMode(addon) - runs when the save/load list is (re)built.
        [Function(CallingConventions.Microsoft)]
        private delegate void RebuildSlotListForMode(UIntPtr addon);
        private IHook<RebuildSlotListForMode> RebuildSlotListForMode_Hook;

        // Generic panel-state switch: SwitchPanelState(stateRecord, state). State 5 = OnEnterLoadConfirmPage.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SwitchPanelState(UIntPtr stateRecord, uint state);
        private SwitchPanelState? _switchPanelState;

        private bool _autoLoadSaveTriggered;

        private void RebuildSlotListForMode_Replacement(UIntPtr addon)
        {
            RebuildSlotListForMode_Hook.OriginalFunction(addon);

            if (!_configuration.AutoLoadLastSaveEnabled || _autoLoadSaveTriggered)
                return;
            if (_switchPanelState == null)
                return;

            // Addon mode (+0x1B0): 0 = new game, 1 = save (slot list), 2 = load (platform saves).
            var mode = Memory.Instance.Read<int>(addon + 0x1B0);

            // Visible list is the vector {allocator, begin, end, cap} at +0x1F8 -> begin +0x200, end +0x208.
            var begin = Memory.Instance.Read<UIntPtr>(addon + 0x200);
            var end = Memory.Instance.Read<UIntPtr>(addon + 0x208);
            var count = (long)((ulong)end - (ulong)begin) / 8;

            // The Load Game list is mode 1 (the imported save records).
            if (mode != 1 || count <= 0)
                return;

            // Pick the last manual save: record byte@0xE9 == 0 (auto-save flag), greatest timestamp qword@0x20.
            var bestIdx = -1;
            var bestSlot = -1;
            long bestTime = long.MinValue;
            for (long i = 0; i < count && i < 64; i++)
            {
                var rec = Memory.Instance.Read<UIntPtr>((nuint)begin + (nuint)(i * 8));
                if (rec == 0 || Memory.Instance.Read<byte>(rec + 0xE9) != 0)
                    continue;

                var ts = Memory.Instance.Read<long>(rec + 0x20);
                if (bestIdx < 0 || ts > bestTime)
                {
                    bestIdx = (int)i;
                    bestSlot = Memory.Instance.Read<int>(rec);
                    bestTime = ts;
                }
            }

            if (bestIdx < 0)
            {
                LogDebug($"AutoOpenLoadGameMenu: no manual save found in the Load Game list ({count} rows).");
                return;
            }

            _autoLoadSaveTriggered = true;

            // Move the selection to the chosen row.
            Memory.Instance.SafeWrite(addon + 0xC8, BitConverter.GetBytes(bestIdx));

            var agent = Memory.Instance.Read<UIntPtr>(addon + 0x10);
            LogDebug($"AutoOpenLoadGameMenu: loading last manual save slot {bestSlot} (row {bestIdx} of {count}).");

            // Set the selected slot + flag, then enter page state 5 (OnEnterLoadConfirmPage) directly.
            // Flag 0 is the path the game itself uses for a manual load (RequestBattleResumeSave + iOS_ResumeByAutoSave).
            Memory.Instance.SafeWrite(agent + 0xE4, BitConverter.GetBytes(bestSlot));
            Memory.Instance.SafeWrite(agent + 0xF4, new byte[] { 0 });
            _switchPanelState!(agent + 0x48, 5);
        }

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

#if DEBUG
            // Attaches debugger in debug mode; ignored in release.
            Debugger.Launch();
#endif

            var startupScannerController = _modLoader.GetController<IStartupScanner>();
            
            if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
            {
                _logger.WriteLineAsync($"[{_modConfig.ModId}] Unable to find startupScanner. Ensure Reloaded.Memory.SigScan is installed.", Color.OrangeRed);

                return;
            }

            // Fetch IFFTOModPackManager to add files
            if (_modLoader.GetController<IFFTOModPackManager>()?.TryGetTarget(out IFFTOModPackManager modPackManager) != true)
            {
                _logger.WriteLineAsync($"[{_modConfig.ModId}] Unable to find IFFTOModPackManager. Ensure fftivc.utility.modloader is installed.", Color.OrangeRed);
                return;
            }

            // Replace the game's start-up UI flow (shortens the boot/logo timelines). The data ships with
            // the mod, so resolve it relative to this assembly's own directory (the mod package folder).
            var modDirectory = Path.GetDirectoryName(typeof(Mod).Assembly.Location)!;
            var dataDirectory = Path.Combine(modDirectory, "FFTIVC", "data", "enhanced");

            modPackManager.AddModdedFile(_modConfig.ModId, FFTOGameMode.Enhanced,
                Path.Combine(dataDirectory, "ui", "ffto", "start_flow", "start_flow.uib"),
                "ui/ffto/start_flow/start_flow.uib");

            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? C6 06 ?? 80 3E ?? 48 8B 6C 24 ?? 48 8B 74 24 ?? 0F 94 C3 48 8B 7C 24 ?? 8B C3 48 8B 5C 24 ?? 48 83 C4 ?? 41 5E C3", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for UIAgentFFTOLauncher_ClearGameMode function!", Color.OrangeRed);
                    return;
                }

                var UIAgentFFTOLauncher_ClearGameMode_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);

                var offset = Memory.Instance.Read<Int32>((nuint)(UIAgentFFTOLauncher_ClearGameMode_address + 1));
                UIAgentFFTOLauncher_ClearGameMode_address = (UIAgentFFTOLauncher_ClearGameMode_address + 5 + offset);

                UIAgentFFTOLauncher_ClearGameMode_Hook = _hooks!.CreateHook<UIAgentFFTOLauncher_ClearGameMode>(UIAgentFFTOLauncher_ClearGameMode_Replacement, UIAgentFFTOLauncher_ClearGameMode_address).Activate();

                if (!UIAgentFFTOLauncher_ClearGameMode_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook UIAgentFFTOLauncher_ClearGameMode function at 0x{UIAgentFFTOLauncher_ClearGameMode_address:X}.", Color.OrangeRed);
                    return;
                }

                LogDebug($"Hooked UIAgentFFTOLauncher_ClearGameMode function at 0x{UIAgentFFTOLauncher_ClearGameMode_address:X}.", Color.LightGreen);
            });

            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8B 48 ?? 48 8B 59 ?? 80 7B ?? ?? 75 ?? 48 8B 03 48 8B CB FF 50 ?? 84 C0 75", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for InitMovie function!", Color.OrangeRed);
                    return;
                }

                var InitMovie_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);

                // Pattern matches a "call rel32" site; the rel32 operand is at +1 and the instruction is 5 bytes.
                var offset = Memory.Instance.Read<Int32>((nuint)(InitMovie_address + 1));
                InitMovie_address = (InitMovie_address + 5 + offset);

                InitMovie_Hook = _hooks!.CreateHook<InitMovie>(InitMovie_Replacement, InitMovie_address).Activate();

                if (!InitMovie_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook InitMovie function at 0x{InitMovie_address:X}.", Color.OrangeRed);
                    return;
                }

                LogDebug($"Hooked InitMovie function at 0x{InitMovie_address:X}.", Color.LightGreen);
            });

            // Resolve the title-menu row-action dispatcher so we can "click" a row programmatically.
            startupScanner.AddMainModuleScan("40 53 48 83 EC 20 45 33 C0 48 8B D9 85 D2 0F 84 ?? ?? ?? ?? 83 EA 01 0F 84 ?? ?? ?? ?? 83 EA 01 0F 84 ?? ?? ?? ?? 83 EA 01 0F 84 ?? ?? ?? ?? 83 EA 01 0F 84", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for HandleSlotSelectionByMode function!", Color.OrangeRed);
                    return;
                }

                var HandleSlotSelectionByMode_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);
                _handleSlotSelectionByMode = Marshal.GetDelegateForFunctionPointer<HandleSlotSelectionByMode>(HandleSlotSelectionByMode_address);

                LogDebug($"Resolved HandleSlotSelectionByMode function at 0x{HandleSlotSelectionByMode_address:X}.", Color.LightGreen);
            });

            startupScanner.AddMainModuleScan("48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 60 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 50 48 8B 05 ?? ?? ?? ?? 48 8B F9 48 85 C0", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for UIAgentFFTOTitle_RefreshTitleSlotList function!", Color.OrangeRed);
                    return;
                }

                var RefreshTitleSlotList_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);

                RefreshTitleSlotList_Hook = _hooks!.CreateHook<RefreshTitleSlotList>(RefreshTitleSlotList_Replacement, RefreshTitleSlotList_address).Activate();

                if (!RefreshTitleSlotList_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook UIAgentFFTOTitle_RefreshTitleSlotList function at 0x{RefreshTitleSlotList_address:X}.", Color.OrangeRed);
                    return;
                }

                LogDebug($"Hooked UIAgentFFTOTitle_RefreshTitleSlotList function at 0x{RefreshTitleSlotList_address:X}.", Color.LightGreen);
            });

            startupScanner.AddMainModuleScan("48 8B C4 48 89 58 08 48 89 68 10 48 89 70 18 48 89 78 20 41 56 48 83 EC 30 48 8B D9 E8 ?? ?? ?? ?? 8B 83 B0 01 00 00 FF C8 F7 D8 1B C0 89 83 B4", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for UIAddonFFTOSaveLoad_RebuildSlotListForMode function!", Color.OrangeRed);
                    return;
                }

                var RebuildSlotListForMode_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);

                RebuildSlotListForMode_Hook = _hooks!.CreateHook<RebuildSlotListForMode>(RebuildSlotListForMode_Replacement, RebuildSlotListForMode_address).Activate();

                if (!RebuildSlotListForMode_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook UIAddonFFTOSaveLoad_RebuildSlotListForMode function at 0x{RebuildSlotListForMode_address:X}.", Color.OrangeRed);
                    return;
                }

                LogDebug($"Hooked UIAddonFFTOSaveLoad_RebuildSlotListForMode function at 0x{RebuildSlotListForMode_address:X}.", Color.LightGreen);
            });

            // Resolve the panel-state switch so we can enter the load page directly.
            startupScanner.AddMainModuleScan("48 89 5C 24 08 57 48 83 EC 20 45 33 C0 48 8B F9 48 8B 09 8B DA 41 8D 50 02 FF 57 08", result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for SwitchPanelState function!", Color.OrangeRed);
                    return;
                }

                var SwitchPanelState_address = (Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset);
                _switchPanelState = Marshal.GetDelegateForFunctionPointer<SwitchPanelState>(SwitchPanelState_address);

                LogDebug($"Resolved SwitchPanelState function at 0x{SwitchPanelState_address:X}.", Color.LightGreen);
            });

        }

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            LogDebug($"Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}