#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;

namespace STS2Mobile;

[JsonSerializable(typeof(MegaCrit.Sts2.Core.Assets.TpSheetData))]
[JsonSerializable(typeof(MegaCrit.Sts2.Core.Assets.TpSheetTexture))]
[JsonSerializable(typeof(MegaCrit.Sts2.Core.Assets.TpSheetSprite))]
[JsonSerializable(typeof(MegaCrit.Sts2.Core.Assets.TpSheetRect))]
[JsonSerializable(typeof(MegaCrit.Sts2.Core.Assets.TpSheetSize))]
[JsonSerializable(typeof(List<MegaCrit.Sts2.Core.Assets.TpSheetTexture>))]
[JsonSerializable(typeof(List<MegaCrit.Sts2.Core.Assets.TpSheetSprite>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class TpSheetJsonContext : JsonSerializerContext
{
}

[ScriptPath("res://src/STS2Bootstrapper.cs")]
public partial class STS2Bootstrapper : Node
{
    public static STS2Bootstrapper? Instance { get; private set; }
    public static bool IsRegistered { get; private set; }

    [UnmanagedCallersOnly(EntryPoint = "load_all_fmod_plugins")]
    public static IntPtr LoadAllFmodPlugins(IntPtr pInterface, IntPtr rCount)
    {
        if (rCount != IntPtr.Zero)
        {
            Marshal.WriteInt32(rCount, 0);
        }
        return IntPtr.Zero;
    }

    public static string LogFilePath { get; private set; } = "";
    private static bool _loggerInitialized = false;

    public override void _EnterTree()
    {
        Instance = this;
        InitFileLogger();
        ConfigureJsonSerialization();
        RegisterSts2Scripts();
        RegisterInputMapActions();
        ConfigureCommandLine();
        ConfigureSteamStubResolver();
        ConfigureMemoryManagement();
        HookSceneTree();
        EnforceFullscreenAndTouchSettings();
    }

    public override void _Ready()
    {
        base._Ready();
        EnforceFullscreenAndTouchSettings();
    }

    public void EnsureRegistered()
    {
        InitFileLogger();
        ConfigureJsonSerialization();
        RegisterSts2Scripts();
        RegisterInputMapActions();
        ConfigureCommandLine();
        ConfigureSteamStubResolver();
        ConfigureMemoryManagement();
        HookSceneTree();
        EnforceFullscreenAndTouchSettings();
    }

    public static void InitFileLogger()
    {
        if (_loggerInitialized) return;
        _loggerInitialized = true;
        try
        {
            string userDir = OS.GetUserDataDir();
            System.IO.Directory.CreateDirectory(userDir);
            LogFilePath = System.IO.Path.Combine(userDir, "sts2_game.log");
            System.IO.File.AppendAllText(LogFilePath, $"\n=== STS2 Session Started at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===\n");
            GD.PrintErr($"[STS2Bootstrapper] Logging initialized! Log path: {LogFilePath}");

            // Hook MegaCrit C# Log events
            MegaCrit.Sts2.Core.Logging.Log.LogCallback += (level, text, skipFrames) =>
            {
                string line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [{level}] {text}";
                GD.PrintErr(line);
                try { System.IO.File.AppendAllText(LogFilePath, line + "\n"); } catch { }

                // Prevent missed asset unload thrashing
                if (text != null && text.Contains("Asset was not cached:"))
                {
                    ClearMissedCacheAssets();
                }

                // Clean exit on main menu quit
                if (text != null && text.Contains("NGame.Quit called"))
                {
                    GD.PrintErr("[STS2Bootstrapper] Detected NGame.Quit called! Terminating process in 500ms after saves flush...");
                    System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                    {
                        try { System.Environment.Exit(0); } catch { }
                    });
                }
            };

            // Hook Unhandled Exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                string line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [FATAL EXCEPTION] {e.ExceptionObject}";
                GD.PrintErr(line);
                try { System.IO.File.AppendAllText(LogFilePath, line + "\n"); } catch { }
            };

            // Hook Unobserved Task Exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                string line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [TASK EXCEPTION] {e.Exception}";
                GD.PrintErr(line);
                try { System.IO.File.AppendAllText(LogFilePath, line + "\n"); } catch { }
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to initialize file logger: {ex}");
        }
    }

    private double _memoryLogTimer = 0;

    public override void _Notification(int what)
    {
        // 2009 is NotificationOsMemoryWarning in Godot
        if (what == 2009)
        {
            GD.PrintErr("[STS2Bootstrapper] OS Low Memory Warning received! Collecting GC heap...");
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
            }
            catch { }
        }
        else if (what == (int)Window.NotificationWMSizeChanged || what == 1005 /* NotificationResized */)
        {
            EnforceFullscreenAndTouchSettings();
        }
    }

    public override void _Process(double delta)
    {
        EnforceFullscreenAndTouchSettings();
        ClearMissedCacheAssets();

        _memoryLogTimer += delta;
        if (_memoryLogTimer >= 10.0)
        {
            _memoryLogTimer = 0;
            try
            {
                long staticMem = (long)OS.GetStaticMemoryUsage();
                long vram = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
                long gcMem = GC.GetTotalMemory(false);
                GD.PrintErr($"[STS2Bootstrapper] Memory Telemetry: Static={staticMem / (1024 * 1024)}MB, VRAM={vram / (1024 * 1024)}MB, GC={gcMem / (1024 * 1024)}MB");
            }
            catch { }
        }

        // On iOS without an external gamepad, force mouse/touch mode if NControllerManager accidentally engages controller mode
        try
        {
            var ctrlMgr = MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager.Instance;
            if (ctrlMgr != null && ctrlMgr.IsUsingController && Input.GetConnectedJoypads().Count == 0)
            {
                var field = typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager)
                    .GetField("<IsUsingController>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(ctrlMgr, false);
                    ctrlMgr.EmitSignal(MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager.SignalName.MouseDetected);
                    var method = typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager)
                        .GetMethod("ControlModeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                    method?.Invoke(ctrlMgr, null);
                    GD.Print("[STS2Bootstrapper] Reset NControllerManager to Touch/Mouse mode.");
                }
            }
        }
        catch { }
    }

    private static bool _fullscreenLogged = false;

    public static void EnforceFullscreenAndTouchSettings()
    {
        try
        {
            var window = Instance?.GetTree()?.Root;
            if (window != null)
            {
                if (window.ContentScaleAspect != Window.ContentScaleAspectEnum.Expand)
                {
                    window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
                    if (!_fullscreenLogged)
                    {
                        GD.PrintErr("[STS2Bootstrapper] Enforced Window.ContentScaleAspect = Expand (fullscreen edge-to-edge).");
                    }
                }
                if (window.ContentScaleMode != Window.ContentScaleModeEnum.CanvasItems)
                {
                    window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
                }
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }

            // Hide mouse cursor on mobile touch screen to avoid NCursorManager per-touch bitmap updates
            if (Input.MouseMode != Input.MouseModeEnum.Hidden)
            {
                Input.MouseMode = Input.MouseModeEnum.Hidden;
            }

            // Keep AspectRatioSetting on Auto so NGame.ApplyDisplaySettings doesn't revert to Keep (16:9 pillarboxing)
            if (MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.SettingsSave != null)
            {
                var settings = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave;
                if (settings.AspectRatioSetting != MegaCrit.Sts2.Core.Settings.AspectRatioSetting.Auto)
                {
                    settings.AspectRatioSetting = MegaCrit.Sts2.Core.Settings.AspectRatioSetting.Auto;
                    if (!_fullscreenLogged)
                    {
                        GD.PrintErr("[STS2Bootstrapper] Set SettingsSave.AspectRatioSetting = Auto to prevent 16:9 pillarboxing.");
                        _fullscreenLogged = true;
                    }
                }
            }
        }
        catch { }
    }

    public static void RegisterInputMapActions()
    {
        try
        {
            var actions = MegaCrit.Sts2.Core.ControllerInput.Controller.AllControllerInputs;
            int added = 0;
            foreach (var action in actions)
            {
                if (!InputMap.HasAction(action))
                {
                    InputMap.AddAction(action);
                    added++;
                }
            }
            GD.PrintErr($"[STS2Bootstrapper] Registered {added} missing controller actions in InputMap. Total now: {actions.Length}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to register InputMap actions: {ex}");
        }
    }

    private static bool _steamResolverConfigured = false;

    public static void ConfigureSteamStubResolver()
    {
        if (_steamResolverConfigured) return;
        try
        {
            var steamAssembly = typeof(Steamworks.SteamAPI).Assembly;
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(steamAssembly, (libraryName, assembly, searchPath) =>
            {
                GD.PrintErr($"[STS2Bootstrapper] Resolving DllImport for library: '{libraryName}'");
                if (libraryName == "steam_api" || libraryName == "steam_api64" || libraryName.Contains("steam_api"))
                {
                    if (System.Runtime.InteropServices.NativeLibrary.TryLoad("libsteam_api64.dylib", assembly, searchPath, out nint handle))
                        return handle;
                    if (System.Runtime.InteropServices.NativeLibrary.TryLoad("libsteam_api.dylib", assembly, searchPath, out handle))
                        return handle;
                    if (System.Runtime.InteropServices.NativeLibrary.TryLoad("@rpath/libsteam_api64.dylib", assembly, searchPath, out handle))
                        return handle;
                    if (System.Runtime.InteropServices.NativeLibrary.TryLoad("@rpath/libsteam_api.dylib", assembly, searchPath, out handle))
                        return handle;
                }
                return nint.Zero;
            });
            _steamResolverConfigured = true;
            GD.PrintErr("[STS2Bootstrapper] Steam DllImportResolver registered successfully.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to register Steam DllImportResolver: {ex}");
        }
    }

    public static void ConfigureCommandLine()
    {
        try
        {
            // 1. Force static constructor of CommandLineHelper to run so _args dictionary is created
            MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("");

            // 2. Fetch the private static _args field
            var cmdType = typeof(MegaCrit.Sts2.Core.Helpers.CommandLineHelper);
            var argsField = cmdType.GetField("_args", BindingFlags.Static | BindingFlags.NonPublic);
            if (argsField != null)
            {
                var dictObj = argsField.GetValue(null);
                if (dictObj is System.Collections.Generic.IDictionary<string, string?> genericDict)
                {
                    genericDict["force-steam"] = "off";
                    genericDict["--force-steam"] = "off";
                    genericDict["skip-steam"] = "true";
                    genericDict["--skip-steam"] = "true";
                    GD.PrintErr($"[STS2Bootstrapper] Set force-steam=off in IDictionary<string, string?>! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
                }
                else if (dictObj is Godot.Collections.Dictionary<string, string?> godotDict)
                {
                    godotDict["force-steam"] = "off";
                    godotDict["--force-steam"] = "off";
                    godotDict["skip-steam"] = "true";
                    godotDict["--skip-steam"] = "true";
                    GD.PrintErr($"[STS2Bootstrapper] Set force-steam=off in Godot Dictionary! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
                }
                else if (dictObj is System.Collections.IDictionary nonGenericDict)
                {
                    nonGenericDict["force-steam"] = "off";
                    nonGenericDict["--force-steam"] = "off";
                    nonGenericDict["skip-steam"] = "true";
                    nonGenericDict["--skip-steam"] = "true";
                    GD.PrintErr($"[STS2Bootstrapper] Set force-steam=off in non-generic IDictionary! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
                }
                else
                {
                    GD.PrintErr($"[STS2Bootstrapper] _args has unexpected type: {dictObj?.GetType().FullName}");
                }
            }
            else
            {
                GD.PrintErr("[STS2Bootstrapper] Could not find _args field in CommandLineHelper");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception setting force-steam=off: {ex}");
        }
    }

    public static void RegisterSts2Scripts()
    {
        if (IsRegistered) return;
        GD.PrintErr("[STS2Bootstrapper] Registering sts2 assembly scripts with Godot...");
        try
        {
            var sts2Assembly = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly;
            GD.PrintErr($"[STS2Bootstrapper] Located sts2 assembly: {sts2Assembly.FullName}");
            ScriptManagerBridge.LookupScriptsInAssembly(sts2Assembly);
            IsRegistered = true;
            GD.PrintErr("[STS2Bootstrapper] Successfully registered all sts2 scripts with Godot!");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to register sts2 scripts: {ex}");
        }
    }

    public static void ConfigureJsonSerialization()
    {
        try
        {
            AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);
            GD.PrintErr("[STS2Bootstrapper] AppContext switch JsonSerializer.IsReflectionEnabledByDefault set to true.");

            var atlasType = typeof(MegaCrit.Sts2.Core.Assets.AtlasManager);
            var field = atlasType.GetField("_jsonOptions", BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null)
            {
                var combinedResolver = JsonTypeInfoResolver.Combine(TpSheetJsonContext.Default, new DefaultJsonTypeInfoResolver());
                var opts = field.GetValue(null) as JsonSerializerOptions;
                if (opts != null && !opts.IsReadOnly)
                {
                    opts.TypeInfoResolver = combinedResolver;
                    GD.PrintErr("[STS2Bootstrapper] Attached source-generated & default resolver to existing AtlasManager._jsonOptions!");
                }
                else
                {
                    var newOpts = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        TypeInfoResolver = combinedResolver
                    };
                    field.SetValue(null, newOpts);
                    GD.PrintErr("[STS2Bootstrapper] Replaced AtlasManager._jsonOptions with new instance using combined resolver!");
                }
            }
            else
            {
                GD.PrintErr("[STS2Bootstrapper] Could not find field _jsonOptions in AtlasManager!");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception in ConfigureJsonSerialization: {ex}");
        }
    }

    private static bool _memoryConfigured = false;

    public static void ConfigureMemoryManagement()
    {
        if (_memoryConfigured) return;
        _memoryConfigured = true;

        try
        {
            // Disable background preloading of 778 assets to prevent iOS Jetsam OOM kills on startup
            MegaCrit.Sts2.Core.Assets.PreloadManager.Enabled = false;
            ClearMissedCacheAssets();
            GD.PrintErr("[STS2Bootstrapper] Set PreloadManager.Enabled = false (on-demand loading enabled to prevent iOS memory spikes).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to configure PreloadManager: {ex}");
        }
    }

    private static readonly Dictionary<string, Resource> _permanentAssetCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _preloadedCharacters = new(StringComparer.OrdinalIgnoreCase);
    private static bool _monsterIntentsPrewarmed = false;

    public static void ClearMissedCacheAssets()
    {
        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            if (cache == null) return;

            var cacheType = typeof(MegaCrit.Sts2.Core.Assets.AssetCache);
            var missedField = cacheType.GetField("_missedCacheAssets", BindingFlags.Instance | BindingFlags.NonPublic);
            if (missedField?.GetValue(cache) is HashSet<string> missedSet && missedSet.Count > 0)
            {
                missedSet.Clear();
            }

            var cacheField = cacheType.GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
            if (cacheField?.GetValue(cache) is System.Collections.IDictionary dict)
            {
                // Mirror all loaded assets into permanent cache so they never get garbage collected
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Key is string key && entry.Value is Resource res && GodotObject.IsInstanceValid(res))
                    {
                        if (!_permanentAssetCache.ContainsKey(key))
                        {
                            _permanentAssetCache[key] = res;
                        }
                    }
                }

                // If any asset was removed by MegaCrit, restore it immediately
                foreach (var kvp in _permanentAssetCache)
                {
                    if (!dict.Contains(kvp.Key) && GodotObject.IsInstanceValid(kvp.Value))
                    {
                        dict[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch { }
    }

    public static void PrewarmMonsterIntents()
    {
        if (_monsterIntentsPrewarmed) return;
        _monsterIntentsPrewarmed = true;

        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            if (cache == null) return;

            void TryLoad(string path)
            {
                try
                {
                    if (ResourceLoader.Exists(path))
                    {
                        var res = cache.GetAsset<Resource>(path);
                        if (res != null) _permanentAssetCache[path] = res;
                    }
                }
                catch { }
            }

            // Defend (00 - 45)
            for (int i = 0; i <= 45; i++)
                TryLoad($"res://images/atlases/intent_atlas.sprites/defend/intent_defend_{i:D2}.tres");

            // Buff (00 - 30)
            for (int i = 0; i <= 30; i++)
                TryLoad($"res://images/atlases/intent_atlas.sprites/buff/intent_buff_{i:D2}.tres");

            // Debuff (00 - 30)
            for (int i = 0; i <= 30; i++)
                TryLoad($"res://images/atlases/intent_atlas.sprites/debuff/intent_debuff_{i:D2}.tres");

            // MegaDebuff (00 - 15)
            for (int i = 0; i <= 15; i++)
                TryLoad($"res://images/atlases/intent_atlas.sprites/debuff/intent_megadebuff_{i:D2}.tres");

            // Status (00 - 20)
            for (int i = 0; i <= 20; i++)
                TryLoad($"res://images/atlases/intent_statuscard.sprites/status/intent_statuscard_{i:D2}.tres");
            for (int i = 0; i <= 20; i++)
                TryLoad($"res://images/atlases/intent_atlas.sprites/status/intent_statuscard_{i:D2}.tres");

            // Attacks (1 - 10)
            for (int i = 1; i <= 10; i++)
            {
                TryLoad($"res://images/atlases/intent_atlas.sprites/attack/intent_attack_{i}.png");
                TryLoad($"res://images/atlases/intent_atlas.sprites/attack/intent_attack_{i:D2}.tres");
            }

            // Special statuses
            for (int i = 0; i <= 20; i++)
            {
                TryLoad($"res://images/atlases/intent_atlas.sprites/sleep/intent_sleep_{i:D2}.tres");
                TryLoad($"res://images/atlases/intent_atlas.sprites/stun/intent_stun_{i:D2}.tres");
                TryLoad($"res://images/atlases/intent_atlas.sprites/curse/intent_curse_{i:D2}.tres");
                TryLoad($"res://images/atlases/intent_atlas.sprites/death/intent_death_{i:D2}.tres");
                TryLoad($"res://images/atlases/intent_atlas.sprites/escape/intent_escape_{i:D2}.tres");
            }

            ClearMissedCacheAssets();
            GD.PrintErr("[STS2Bootstrapper] Monster intent animations prewarmed successfully!");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Error prewarming monster intents: {ex.Message}");
        }
    }

    public static void PreloadCharacterAssets(MegaCrit.Sts2.Core.Models.CharacterModel character)
    {
        if (character == null) return;
        string charId = character.Id?.ToString() ?? character.GetType().Name;
        if (_preloadedCharacters.Contains(charId)) return;
        _preloadedCharacters.Add(charId);

        GD.PrintErr($"[STS2Bootstrapper] Preloading all assets for active character: {charId}...");

        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            if (cache == null) return;

            void TryLoad(string? path)
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    if (ResourceLoader.Exists(path))
                    {
                        var res = cache.GetAsset<Resource>(path);
                        if (res != null) _permanentAssetCache[path] = res;
                    }
                }
                catch { }
            }

            // 1. Character model base assets
            if (character.AssetPaths != null)
            {
                foreach (var path in character.AssetPaths)
                {
                    TryLoad(path);
                }
            }
            if (character.ExtraAssetPaths != null)
            {
                foreach (var path in character.ExtraAssetPaths)
                {
                    TryLoad(path);
                }
            }
            TryLoad(character.TrailPath);
            TryLoad(character.VisualsPath);
            TryLoad(character.IconTexturePath);
            TryLoad(character.EnergyCounterPath);

            // 2. Character Card Pool
            var cardPool = character.CardPool;
            if (cardPool != null)
            {
                TryLoad(cardPool.CardFrameMaterialPath);
                TryLoad(cardPool.FrameMaterialPath);
                TryLoad(cardPool.EnergyIconPath);

                var allCards = cardPool.AllCards;
                if (allCards != null)
                {
                    int cardCount = 0;
                    foreach (var card in allCards)
                    {
                        if (card == null) continue;
                        cardCount++;
                        TryLoad(card.PortraitPath);
                        TryLoad(card.PortraitPngPath);
                        TryLoad(card.FramePath);
                        TryLoad(card.BannerMaterialPath);
                        TryLoad(card.BannerTexturePath);
                        TryLoad(card.EnergyIconPath);
                        TryLoad(card.OverlayPath);

                        if (card.RunAssetPaths != null)
                        {
                            foreach (var p in card.RunAssetPaths) TryLoad(p);
                        }
                        if (card.ExtraRunAssetPaths != null)
                        {
                            foreach (var p in card.ExtraRunAssetPaths) TryLoad(p);
                        }
                    }
                    GD.PrintErr($"[STS2Bootstrapper] Preloaded {cardCount} cards for character {charId}!");
                }
            }

            // 3. Signature character VFX and powers
            string lowerName = charId.ToLowerInvariant();
            if (lowerName.Contains("silent"))
            {
                string[] silentAssets = new string[]
                {
                    "res://materials/cards/frames/card_frame_green_mat.tres",
                    "res://scenes/vfx/vfx_shiv_throw.tscn",
                    "res://scenes/vfx/thin_slice_vfx.tscn",
                    "res://scenes/vfx/vfx_dagger_spray_flurry.tscn",
                    "res://scenes/vfx/vfx_dagger_spray_impact.tscn",
                    "res://scenes/vfx/cards/exhaust_vfx.tscn",
                    "res://scenes/vfx/cards/card_exhaust_vfx.tscn",
                    "res://debug_audio/card_exhaust.mp3",
                    "res://images/powers/poison_power.png",
                    "res://images/powers/accuracy_power.png",
                    "res://images/powers/after_image_power.png",
                    "res://images/powers/infinite_blades_power.png",
                    "res://images/powers/noxious_fumes_power.png",
                    "res://images/powers/choke_power.png",
                    "res://images/powers/envenom_power.png",
                    "res://images/powers/thousand_cuts_power.png",
                    "res://images/powers/burst_power.png",
                    "res://images/powers/tools_of_the_trade_power.png",
                    "res://images/powers/corpse_explosion_power.png",
                    "res://images/powers/phantasmal_killer_power.png",
                    "res://images/powers/bullet_time_power.png",
                    "res://images/powers/tactician_power.png",
                    "res://images/powers/reflex_power.png",
                    "res://images/powers/wraith_form_power.png"
                };
                foreach (var p in silentAssets) TryLoad(p);
            }
            else if (lowerName.Contains("ironclad"))
            {
                string[] ironcladAssets = new string[]
                {
                    "res://materials/cards/frames/card_frame_red_mat.tres",
                    "res://scenes/vfx/vfx_attack_slash.tscn",
                    "res://scenes/vfx/vfx_attack_blunt.tscn",
                    "res://scenes/vfx/vfx_heavy_blunt.tscn",
                    "res://scenes/vfx/vfx_big_slash.tscn",
                    "res://scenes/vfx/vfx_big_slash_impact.tscn",
                    "res://scenes/vfx/vfx_fire_burning.tscn",
                    "res://images/powers/strength_power.png",
                    "res://images/powers/metallicize_power.png",
                    "res://images/powers/barricade_power.png",
                    "res://images/powers/flame_barrier_power.png",
                    "res://images/powers/feel_no_pain_power.png",
                    "res://images/powers/dark_embrace_power.png",
                    "res://images/powers/corruption_power.png",
                    "res://images/powers/demon_form_power.png",
                    "res://images/powers/inflame_power.png",
                    "res://images/powers/berserk_power.png",
                    "res://images/powers/rupture_power.png",
                    "res://images/powers/juggernaut_power.png",
                    "res://images/powers/brutality_power.png",
                    "res://images/powers/combust_power.png"
                };
                foreach (var p in ironcladAssets) TryLoad(p);
            }
            else if (lowerName.Contains("defect"))
            {
                string[] defectAssets = new string[]
                {
                    "res://materials/cards/frames/card_frame_blue_mat.tres",
                    "res://scenes/vfx/vfx_attack_lightning.tscn",
                    "res://images/powers/focus_power.png",
                    "res://images/powers/electrodynamics_power.png",
                    "res://images/powers/loop_power.png",
                    "res://images/powers/defragment_power.png",
                    "res://images/powers/biased_cognition_power.png",
                    "res://images/powers/echo_form_power.png",
                    "res://images/powers/creative_ai_power.png",
                    "res://images/powers/buffer_power.png",
                    "res://images/powers/static_discharge_power.png",
                    "res://images/powers/storm_power.png",
                    "res://images/powers/heatsinks_power.png"
                };
                foreach (var p in defectAssets) TryLoad(p);
            }

            ClearMissedCacheAssets();
            GD.PrintErr($"[STS2Bootstrapper] Completed character preloading for {charId}. Total cached assets: {_permanentAssetCache.Count}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Error preloading character assets: {ex.Message}");
        }
    }

    public static void TryPreloadCurrentRunCharacter()
    {
        try
        {
            var run = MegaCrit.Sts2.Core.Nodes.NRun.Instance;
            if (run != null)
            {
                var stateField = typeof(MegaCrit.Sts2.Core.Nodes.NRun).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
                if (stateField?.GetValue(run) is MegaCrit.Sts2.Core.Runs.IRunState runState)
                {
                    var playersProp = runState.GetType().GetProperty("Players") ?? typeof(MegaCrit.Sts2.Core.Runs.RunState).GetProperty("Players");
                    if (playersProp?.GetValue(runState) is System.Collections.IEnumerable players)
                    {
                        foreach (var player in players)
                        {
                            var charProp = player.GetType().GetProperty("Character");
                            if (charProp?.GetValue(player) is MegaCrit.Sts2.Core.Models.CharacterModel character)
                            {
                                PreloadCharacterAssets(character);
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void PrewarmCombatEssentials()
    {
        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            if (cache == null) return;

            string[] essentials = new string[]
            {
                // Core Transitions & UI Materials
                "res://materials/transitions/fade_transition_mat.tres",
                "res://materials/transitions/ironclad_transition_mat.tres",
                "res://materials/ui/hover_tip_debuff.tres",
                "res://materials/ui/card_hover_tip_mat.tres",
                "res://materials/cards/banners/card_banner_common_mat.tres",
                "res://materials/cards/banners/card_banner_uncommon_mat.tres",
                "res://materials/cards/banners/card_banner_rare_mat.tres",
                "res://materials/cards/frames/card_frame_red_mat.tres",
                "res://materials/cards/frames/card_frame_green_mat.tres",
                "res://materials/cards/frames/card_frame_blue_mat.tres",
                "res://materials/cards/frames/card_frame_colorless_mat.tres",

                // Card UI & Holders
                "res://scenes/cards/holders/selected_hand_card_holder.tscn",
                "res://scenes/ui/card_hover_tip.tscn",

                // Attack VFX (eliminates card play & attack lag spikes!)
                "res://scenes/vfx/vfx_attack_slash.tscn",
                "res://scenes/vfx/vfx_attack_blunt.tscn",
                "res://scenes/vfx/vfx_attack_lightning.tscn",
                "res://scenes/vfx/vfx_heavy_blunt.tscn",
                "res://scenes/vfx/vfx_big_slash.tscn",
                "res://scenes/vfx/vfx_big_slash_impact.tscn",
                "res://scenes/vfx/vfx_flying_slash.tscn",
                "res://scenes/vfx/vfx_giant_horizontal_slash.tscn",
                "res://scenes/vfx/vfx_dagger_spray.tscn",
                "res://scenes/vfx/vfx_dramatic_stab.tscn",
                "res://scenes/vfx/vfx_scratch.tscn",
                "res://scenes/vfx/vfx_bite.tscn",
                "res://scenes/vfx/hit_spark_vfx.tscn",
                "res://scenes/vfx/vfx_slime_impact.tscn",
                "res://scenes/vfx/vfx_goopy_impact.tscn",
                "res://scenes/vfx/vfx_fire_burning.tscn",

                // Defense & Damage VFX
                "res://scenes/vfx/vfx_block.tscn",
                "res://scenes/vfx/vfx_block_broken.tscn",
                "res://scenes/vfx/vfx_blocked_text.tscn",
                "res://scenes/vfx/block_spark_vfx.tscn",
                "res://scenes/vfx/block_broken_vfx.tscn",
                "res://scenes/vfx/damage_blocked_vfx.tscn",
                "res://scenes/vfx/vfx_damage_num.tscn",
                "res://scenes/vfx/damage_num_vfx.tscn",
                "res://scenes/vfx/vfx_heal_num.tscn",
                "res://scenes/vfx/vfx_cross_heal.tscn",
                "res://scenes/vfx/vfx_monster_death.tscn",

                // Powers & Combat UI VFX
                "res://scenes/vfx/power_applied_vfx.tscn",
                "res://scenes/vfx/power_removed_vfx.tscn",
                "res://scenes/vfx/power_flash_vfx.tscn",
                "res://scenes/vfx/ui/vfx_debuff_applied.tscn",
                "res://scenes/vfx/ui/vfx_buff_applied.tscn",
                "res://scenes/vfx/relic_flash_vfx.tscn",
                "res://scenes/vfx/relic_inventory_flash_vfx.tscn",
                "res://scenes/combat/power.tscn",
                "res://scenes/combat/combat_start_banner.tscn",
                "res://scenes/combat/player_turn_banner.tscn",
                "res://scenes/combat/enemy_turn_banner.tscn",

                // Cards & Trails
                "res://scenes/vfx/vfx_card_fly.tscn",
                "res://scenes/vfx/vfx_card_shuffle_fly.tscn",
                "res://scenes/vfx/vfx_card_upgrade.tscn",
                "res://scenes/vfx/vfx_potion_flash.tscn",
                "res://scenes/vfx/card_trail_ironclad.tscn",
                "res://scenes/vfx/card_trail_silent.tscn",
                "res://scenes/vfx/card_trail_defect.tscn",
                "res://scenes/vfx/card_trail_necrobinder.tscn",
                "res://scenes/vfx/card_trail_regent.tscn",
                "res://scenes/vfx/cards/card_fly_vfx.tscn",
                "res://scenes/vfx/cards/card_fly_power_vfx.tscn",
                "res://scenes/vfx/cards/card_fly_shuffle_vfx.tscn",
                "res://scenes/vfx/cards/card_exhaust_vfx.tscn",
                "res://scenes/vfx/cards/exhaust_vfx.tscn",

                // Common Power PNGs
                "res://images/powers/vulnerable_power.png",
                "res://images/powers/weak_power.png",
                "res://images/powers/frail_power.png",
                "res://images/powers/strength_power.png",
                "res://images/powers/ritual_power.png",
                "res://images/powers/metallicize_power.png",
                "res://images/powers/barricade_power.png",
                "res://images/powers/dexterity_power.png",
                "res://images/powers/constricted_power.png",
                "res://images/powers/poison_power.png",
                "res://images/powers/regen_power.png",
                "res://images/powers/artifact_power.png",
                "res://images/powers/intangible_power.png",
                "res://images/powers/thorns_power.png",
                "res://images/powers/shrink_power.png",
                "res://images/powers/flame_barrier_power.png",
                "res://images/powers/mayhem_power.png",

                // Combat Audio
                "res://debug_audio/blunt_attack.mp3",
                "res://debug_audio/slash_attack.mp3",
                "res://debug_audio/heavy_attack.mp3",
                "res://debug_audio/card_select.mp3",
                "res://debug_audio/card_deal.mp3",
                "res://debug_audio/card_exhaust.mp3",
                "res://debug_audio/player_turn.mp3",
                "res://debug_audio/enemy_turn.mp3",
                "res://debug_audio/battle_start_1.mp3",
                "res://debug_audio/victory.mp3"
            };

            foreach (var path in essentials)
            {
                if (!cache.ContainsKey(path) && ResourceLoader.Exists(path))
                {
                    var res = cache.GetAsset<Resource>(path);
                    if (res != null) _permanentAssetCache[path] = res;
                }
            }
            ClearMissedCacheAssets();
            GD.PrintErr("[STS2Bootstrapper] Pre-warmed combat essentials into AssetCache (zero attack/hit stutter).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to prewarm combat essentials: {ex.Message}");
        }
    }

    private static bool _treeHooked = false;

    public static void HookSceneTree()
    {
        if (_treeHooked) return;
        _treeHooked = true;
        try
        {
            if (Instance != null)
            {
                Instance.GetTree().NodeAdded += OnNodeAdded;
                GD.PrintErr("[STS2Bootstrapper] Hooked SceneTree.NodeAdded successfully!");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to hook SceneTree: {ex}");
        }
    }

    private static void OnNodeAdded(Node node)
    {
        try
        {
            ClearMissedCacheAssets();

            if (node is MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom)
            {
                try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch { }
                PrewarmCombatEssentials();
                PrewarmMonsterIntents();
                Callable.From(TryPreloadCurrentRunCharacter).CallDeferred();
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.NRun)
            {
                Callable.From(TryPreloadCurrentRunCharacter).CallDeferred();
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen)
            {
                PrewarmMonsterIntents();
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen mapScreen)
            {
                try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive; } catch { }
                try { GC.Collect(1, GCCollectionMode.Optimized); } catch { }
                mapScreen.Visible = false;
                mapScreen.ProcessMode = Node.ProcessModeEnum.Disabled;
                GD.PrintErr("[STS2Bootstrapper] Initialized NMapScreen: Visible = false, ProcessMode = Disabled");
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Screens.Map.NBossMapPoint bossPoint)
            {
                var bpType = typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NBossMapPoint);
                var phImage = FindChildRecursive<TextureRect>(bossPoint, "PlaceholderImage");
                var phOutline = FindChildRecursive<TextureRect>(bossPoint, "PlaceholderOutline");
                var spriteContainer = FindChildRecursive<Node2D>(bossPoint, "SpriteContainer");
                var spineSprite = FindChildRecursive<Node2D>(bossPoint, "SpineSprite");

                if (phImage != null)
                    bpType.GetField("_placeholderImage", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bossPoint, phImage);
                if (phOutline != null)
                    bpType.GetField("_placeholderOutline", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bossPoint, phOutline);
                if (spriteContainer != null)
                    bpType.GetField("_spriteContainer", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bossPoint, spriteContainer);
                if (spineSprite != null)
                    bpType.GetField("_spineSprite", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(bossPoint, spineSprite);

                var actField = bpType.GetField("_act", BindingFlags.Instance | BindingFlags.NonPublic);
                if (actField != null && actField.GetValue(bossPoint) == null)
                {
                    var rsField = bpType.GetField("_runState", BindingFlags.Instance | BindingFlags.NonPublic);
                    var rs = rsField?.GetValue(bossPoint) as MegaCrit.Sts2.Core.Runs.IRunState;
                    if (rs?.Act != null)
                    {
                        actField.SetValue(bossPoint, rs.Act);
                    }
                }

                GD.PrintErr("[STS2Bootstrapper] Successfully pre-initialized NBossMapPoint children and fields!");
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Combat.NCreature nCreature)
            {
                var ncType = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature);
                var visualsProp = ncType.GetProperty("Visuals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (nCreature.Visuals == null)
                {
                    GD.PrintErr($"[STS2Bootstrapper] NCreature {nCreature.Name} has null Visuals, instantiating fallback visuals...");
                    try
                    {
                        var fallbackScene = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/creature_visuals/fallback.tscn");
                        if (fallbackScene != null)
                        {
                            var fb = fallbackScene.Instantiate<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(PackedScene.GenEditState.Disabled);
                            visualsProp?.SetValue(nCreature, fb);
                        }
                    }
                    catch (Exception fex)
                    {
                        GD.PrintErr($"[STS2Bootstrapper] Failed to instantiate fallback visuals: {fex.Message}");
                    }
                }

                if (nCreature.Visuals != null)
                {
                    GuardCreatureVisuals(nCreature.Visuals);
                }

                // Guard hitbox and selection reticle to eliminate combat hover lag
                if (nCreature.IsNodeReady())
                {
                    GuardCreatureHitboxAndReticle(nCreature);
                }
                else
                {
                    nCreature.Ready += () => GuardCreatureHitboxAndReticle(nCreature);
                }
                nCreature.ChildEnteredTree += (child) =>
                {
                    GuardCreatureHitboxAndReticle(nCreature);
                    if (nCreature.Visuals != null) GuardCreatureVisuals(nCreature.Visuals);
                };
                Callable.From(() => GuardCreatureHitboxAndReticle(nCreature)).CallDeferred();
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals nVisuals)
            {
                GuardCreatureVisuals(nVisuals);
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager targetManager)
            {
                targetManager.TargetingBegan += () => GD.PrintErr("[STS2Bootstrapper] Card targeting began");
                targetManager.CreatureHovered += (c) => GD.PrintErr($"[STS2Bootstrapper] Creature hovered: {c?.Entity?.Name ?? c?.Name}");
                targetManager.CreatureUnhovered += (c) => GD.PrintErr($"[STS2Bootstrapper] Creature unhovered: {c?.Entity?.Name ?? c?.Name}");
                targetManager.TargetingEnded += () => GD.PrintErr("[STS2Bootstrapper] Card targeting ended");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception in OnNodeAdded: {ex}");
        }
    }

    private static void GuardCreatureHitboxAndReticle(MegaCrit.Sts2.Core.Nodes.Combat.NCreature nCreature)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(nCreature)) return;

            var ncType = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature);

            // 1. Prevent _selectionReticle and its children from intercepting touches/mouse events
            var reticleField = ncType.GetField("_selectionReticle", BindingFlags.Instance | BindingFlags.NonPublic);
            var reticle = reticleField?.GetValue(nCreature) as Control 
                       ?? nCreature.GetNodeOrNull<Control>("%SelectionReticle")
                       ?? nCreature.GetNodeOrNull<Control>("SelectionReticle");

            if (reticle != null)
            {
                SetMouseFilterIgnoreRecursive(reticle);
                GD.PrintErr($"[STS2Bootstrapper] Set MouseFilter=Ignore on SelectionReticle for {nCreature.Name}");
            }

            // 2. Prevent IntentContainer and Visuals from intercepting touches
            var intents = nCreature.IntentContainer 
                       ?? nCreature.GetNodeOrNull<Control>("%Intents")
                       ?? nCreature.GetNodeOrNull<Control>("Intents");
            if (intents != null)
            {
                SetMouseFilterIgnoreRecursive(intents);
            }

            if (nCreature.Visuals != null)
            {
                SetMouseFilterIgnoreRecursive(nCreature.Visuals);
            }

            // 3. Prevent HP bar from blocking creature touches
            var stateDisplayField = ncType.GetField("_stateDisplay", BindingFlags.Instance | BindingFlags.NonPublic);
            var stateDisplay = stateDisplayField?.GetValue(nCreature) as Control
                            ?? nCreature.GetNodeOrNull<Control>("%HealthBar")
                            ?? nCreature.GetNodeOrNull<Control>("HealthBar");
            if (stateDisplay != null)
            {
                stateDisplay.MouseFilter = Control.MouseFilterEnum.Pass;
                var hpBarHitbox = stateDisplay.GetNodeOrNull<Control>("%HpBarHitbox") 
                               ?? stateDisplay.GetNodeOrNull<Control>("HpBarHitbox");
                if (hpBarHitbox != null)
                {
                    hpBarHitbox.MouseFilter = Control.MouseFilterEnum.Pass;
                }
                var nameplate = stateDisplay.GetNodeOrNull<Control>("%NameplateContainer")
                             ?? stateDisplay.GetNodeOrNull<Control>("NameplateContainer");
                if (nameplate != null)
                {
                    SetMouseFilterIgnoreRecursive(nameplate);
                }
            }

            // 4. Guard Hitbox against false MouseExited events
            var hitbox = nCreature.Hitbox 
                      ?? nCreature.GetNodeOrNull<Control>("%Hitbox")
                      ?? nCreature.GetNodeOrNull<Control>("Hitbox");

            if (hitbox != null)
            {
                hitbox.MouseFilter = Control.MouseFilterEnum.Stop;

                // Disconnect original MouseExited connection to avoid hover oscillation
                var connections = hitbox.GetSignalConnectionList(Control.SignalName.MouseExited);
                foreach (var conn in connections)
                {
                    if (conn.ContainsKey("callable"))
                    {
                        var callable = conn["callable"].As<Callable>();
                        hitbox.Disconnect(Control.SignalName.MouseExited, callable);
                    }
                }

                var onUnfocusMethod = ncType.GetMethod("OnUnfocus", BindingFlags.Instance | BindingFlags.NonPublic);

                // Connect debounced MouseExited: only unfocus if pointer has actually left the hitbox bounds
                hitbox.Connect(Control.SignalName.MouseExited, Callable.From(() =>
                {
                    if (nCreature.IsInsideTree() && hitbox.IsInsideTree())
                    {
                        var mousePos = nCreature.GetViewport().GetMousePosition();
                        if (hitbox.GetGlobalRect().HasPoint(mousePos))
                        {
                            // Finger is still physically inside monster bounds; discard false exit
                            return;
                        }
                    }
                    onUnfocusMethod?.Invoke(nCreature, null);
                }));

                GD.PrintErr($"[STS2Bootstrapper] Hitbox guarded against false exits for {nCreature.Name}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception in GuardCreatureHitboxAndReticle: {ex}");
        }
    }

    private static void SetMouseFilterIgnoreRecursive(Node node)
    {
        if (node is Control ctrl)
        {
            ctrl.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        foreach (var child in node.GetChildren())
        {
            SetMouseFilterIgnoreRecursive(child);
        }
    }

    private static void GuardCreatureVisuals(MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals visuals)
    {
        try
        {
            var cvType = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals);
            if (visuals.VfxSpawnPosition == null)
            {
                var centerPos = visuals.GetNodeOrNull<Marker2D>("%CenterPos")
                             ?? visuals.GetNodeOrNull<Marker2D>("CenterPos")
                             ?? visuals.GetNodeOrNull<Marker2D>("%VfxSpawnPos")
                             ?? visuals.GetNodeOrNull<Marker2D>("VfxSpawnPos")
                             ?? new Marker2D { Name = "CenterPos" };
                if (!centerPos.IsInsideTree())
                {
                    visuals.AddChild(centerPos);
                }
                cvType.GetProperty("VfxSpawnPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(visuals, centerPos);
                cvType.GetField("<VfxSpawnPosition>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(visuals, centerPos);
            }

            if (visuals.Bounds == null)
            {
                var bounds = visuals.GetNodeOrNull<Control>("%Bounds")
                          ?? visuals.GetNodeOrNull<Control>("Bounds")
                          ?? new Control { Name = "Bounds", CustomMinimumSize = new Vector2(100, 200) };
                bounds.MouseFilter = Control.MouseFilterEnum.Ignore;
                if (!bounds.IsInsideTree())
                {
                    visuals.AddChild(bounds);
                }
                cvType.GetProperty("Bounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(visuals, bounds);
                cvType.GetField("<Bounds>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(visuals, bounds);
            }
            else
            {
                visuals.Bounds.MouseFilter = Control.MouseFilterEnum.Ignore;
            }

            if (visuals.IntentPosition == null)
            {
                var intentPos = visuals.GetNodeOrNull<Marker2D>("%IntentPos")
                             ?? visuals.GetNodeOrNull<Marker2D>("IntentPos")
                             ?? new Marker2D { Name = "IntentPos", Position = new Vector2(0, -200) };
                if (!intentPos.IsInsideTree())
                {
                    visuals.AddChild(intentPos);
                }
                cvType.GetProperty("IntentPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(visuals, intentPos);
                cvType.GetField("<IntentPosition>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(visuals, intentPos);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception guarding visuals: {ex.Message}");
        }
    }

    private static T? FindChildRecursive<T>(Node parent, string name) where T : Node
    {
        var found = parent.GetNodeOrNull<T>("%" + name) ?? parent.GetNodeOrNull<T>(name);
        if (found != null) return found;
        foreach (var child in parent.GetChildren())
        {
            if ((child.Name == name || child.Name == "%" + name) && child is T tChild) return tChild;
            var rec = FindChildRecursive<T>(child, name);
            if (rec != null) return rec;
        }
        return null;
    }
}
