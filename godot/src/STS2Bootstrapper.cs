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

    public override void _Process(double delta)
    {
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
            if (node is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen mapScreen)
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
                if (nCreature.Visuals == null)
                {
                    GD.PrintErr($"[STS2Bootstrapper] NCreature {nCreature.Name} has null Visuals, instantiating fallback visuals...");
                    try
                    {
                        var fallbackScene = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/creature_visuals/fallback.tscn");
                        if (fallbackScene != null)
                        {
                            nCreature.Visuals = fallbackScene.Instantiate<MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals>(PackedScene.GenEditState.Disabled);
                        }
                    }
                    catch (Exception fex)
                    {
                        GD.PrintErr($"[STS2Bootstrapper] Failed to instantiate fallback visuals: {fex.Message}");
                    }
                }

                if (nCreature.Visuals != null)
                {
                    if (nCreature.Visuals.VfxSpawnPosition == null)
                    {
                        var centerPos = nCreature.Visuals.GetNodeOrNull<Marker2D>("%CenterPos")
                                     ?? nCreature.Visuals.GetNodeOrNull<Marker2D>("CenterPos")
                                     ?? new Marker2D { Name = "CenterPos" };
                        if (!centerPos.IsInsideTree())
                        {
                            nCreature.Visuals.AddChild(centerPos);
                        }
                        nCreature.Visuals.VfxSpawnPosition = centerPos;
                        GD.PrintErr($"[STS2Bootstrapper] Guarded VfxSpawnPosition on {nCreature.Name}");
                    }

                    if (nCreature.Visuals.Bounds == null)
                    {
                        var bounds = nCreature.Visuals.GetNodeOrNull<Control>("%Bounds")
                                  ?? nCreature.Visuals.GetNodeOrNull<Control>("Bounds")
                                  ?? new Control { Name = "Bounds", CustomMinimumSize = new Vector2(100, 200) };
                        if (!bounds.IsInsideTree())
                        {
                            nCreature.Visuals.AddChild(bounds);
                        }
                        nCreature.Visuals.Bounds = bounds;
                    }

                    if (nCreature.Visuals.IntentPosition == null)
                    {
                        var intentPos = nCreature.Visuals.GetNodeOrNull<Marker2D>("%IntentPos")
                                     ?? nCreature.Visuals.GetNodeOrNull<Marker2D>("IntentPos")
                                     ?? new Marker2D { Name = "IntentPos", Position = new Vector2(0, -200) };
                        if (!intentPos.IsInsideTree())
                        {
                            nCreature.Visuals.AddChild(intentPos);
                        }
                        nCreature.Visuals.IntentPosition = intentPos;
                    }
                }
            }
            else if (node is MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals nVisuals)
            {
                if (nVisuals.VfxSpawnPosition == null)
                {
                    var centerPos = nVisuals.GetNodeOrNull<Marker2D>("%CenterPos")
                                 ?? nVisuals.GetNodeOrNull<Marker2D>("CenterPos")
                                 ?? new Marker2D { Name = "CenterPos" };
                    if (!centerPos.IsInsideTree())
                    {
                        nVisuals.AddChild(centerPos);
                    }
                    nVisuals.VfxSpawnPosition = centerPos;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Exception in OnNodeAdded: {ex}");
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
