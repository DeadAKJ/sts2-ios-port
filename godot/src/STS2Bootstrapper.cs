using System;
using System.Reflection;
using Godot;
using Godot.Bridge;

namespace STS2Mobile;

[ScriptPath("res://src/STS2Bootstrapper.cs")]
public partial class STS2Bootstrapper : Node
{
    public static STS2Bootstrapper? Instance { get; private set; }
    public static bool IsRegistered { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        RegisterSts2Scripts();
        RegisterInputMapActions();
        ConfigureSteamStubResolver();
        ConfigureCommandLine();
    }

    public void EnsureRegistered()
    {
        RegisterSts2Scripts();
        RegisterInputMapActions();
        ConfigureSteamStubResolver();
        ConfigureCommandLine();
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
            GD.Print($"[STS2Bootstrapper] Registered {added} missing controller actions in InputMap. Total now: {actions.Length}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to register InputMap actions: {ex}");
        }
    }

    public static void ConfigureSteamStubResolver()
    {
        try
        {
            var steamAssembly = typeof(Steamworks.SteamAPI).Assembly;
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(steamAssembly, (libraryName, assembly, searchPath) =>
            {
                GD.Print($"[STS2Bootstrapper] Resolving DllImport for library: '{libraryName}'");
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
            GD.Print("[STS2Bootstrapper] Steam DllImportResolver registered successfully.");
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
                    GD.Print($"[STS2Bootstrapper] Set force-steam=off in IDictionary<string, string?>! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
                }
                else if (dictObj is Godot.Collections.Dictionary<string, string?> godotDict)
                {
                    godotDict["force-steam"] = "off";
                    godotDict["--force-steam"] = "off";
                    godotDict["skip-steam"] = "true";
                    godotDict["--skip-steam"] = "true";
                    GD.Print($"[STS2Bootstrapper] Set force-steam=off in Godot Dictionary! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
                }
                else if (dictObj is System.Collections.IDictionary nonGenericDict)
                {
                    nonGenericDict["force-steam"] = "off";
                    nonGenericDict["--force-steam"] = "off";
                    nonGenericDict["skip-steam"] = "true";
                    nonGenericDict["--skip-steam"] = "true";
                    GD.Print($"[STS2Bootstrapper] Set force-steam=off in non-generic IDictionary! HasArg('force-steam')={MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("force-steam")}, GetValue('force-steam')='{MegaCrit.Sts2.Core.Helpers.CommandLineHelper.GetValue("force-steam")}'");
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
        GD.Print("[STS2Bootstrapper] Registering sts2 assembly scripts with Godot...");
        try
        {
            var sts2Assembly = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly;
            GD.Print($"[STS2Bootstrapper] Located sts2 assembly: {sts2Assembly.FullName}");
            ScriptManagerBridge.LookupScriptsInAssembly(sts2Assembly);
            IsRegistered = true;
            GD.Print("[STS2Bootstrapper] Successfully registered all sts2 scripts with Godot!");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2Bootstrapper] Failed to register sts2 scripts: {ex}");
        }
    }
}
