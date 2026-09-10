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
    }

    public void EnsureRegistered()
    {
        RegisterSts2Scripts();
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
