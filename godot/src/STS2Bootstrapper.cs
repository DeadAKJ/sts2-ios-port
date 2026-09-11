#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    }

    public override void _Process(double delta)
    {
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

    public static void ClearMissedCacheAssets()
    {
        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            var missedField = typeof(MegaCrit.Sts2.Core.Assets.AssetCache).GetField("_missedCacheAssets", BindingFlags.Instance | BindingFlags.NonPublic);
            if (missedField?.GetValue(cache) is HashSet<string> missedSet)
            {
                missedSet.Clear();
            }
        }
        catch { }
    }

    private static void PrewarmCombatEssentials()
    {
        try
        {
            var cache = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
            string[] essentials = new string[]
            {
                "res://materials/transitions/fade_transition_mat.tres",
                "res://materials/transitions/ironclad_transition_mat.tres",
                "res://scenes/vfx/hit_spark_vfx.tscn",
                "res://scenes/vfx/block_spark_vfx.tscn",
                "res://scenes/vfx/block_broken_vfx.tscn",
                "res://scenes/vfx/damage_blocked_vfx.tscn",
                "res://scenes/vfx/damage_num_vfx.tscn",
                "res://scenes/vfx/cards/card_fly_vfx.tscn",
                "res://scenes/vfx/cards/card_fly_power_vfx.tscn",
                "res://scenes/vfx/cards/card_fly_shuffle_vfx.tscn",
                "res://scenes/vfx/cards/card_exhaust_vfx.tscn",
                "res://debug_audio/blunt_attack.mp3",
                "res://debug_audio/slash_attack.mp3",
                "res://debug_audio/heavy_attack.mp3",
                "res://debug_audio/card_select.mp3",
                "res://debug_audio/card_deal.mp3",
                "res://debug_audio/player_turn.mp3",
                "res://debug_audio/enemy_turn.mp3"
            };
            foreach (var path in essentials)
            {
                if (!cache.ContainsKey(path) && ResourceLoader.Exists(path))
                {
                    cache.GetAsset<Resource>(path);
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
                PrewarmCombatEssentials();
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen mapScreen)
            {
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
                             ?? new Marker2D { Name = "CenterPos" };
                if (!centerPos.IsInsideTree())
                {
                    visuals.AddChild(centerPos);
                }
                cvType.GetProperty("VfxSpawnPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(visuals, centerPos);
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
