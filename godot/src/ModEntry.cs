using System;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using HarmonyLib;
using STS2Mobile.Patches;

namespace STS2Mobile;

public static class ModEntry
{
    private static Harmony _harmony;
    private static bool _applied = false;

    [UnmanagedCallersOnly]
    public static int InitializeGodotSharp(
        IntPtr godotDllHandle,
        IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize
    )
    {
        try
        {
            DllImportResolver dllImportResolver = new GodotDllImportResolver(
                godotDllHandle
            ).OnResolveDllImport;
            var coreApiAssembly = typeof(GodotObject).Assembly;
            NativeLibrary.SetDllImportResolver(coreApiAssembly, dllImportResolver);

            NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
            ManagedCallbacks.Create(outManagedCallbacks);

            Console.Error.WriteLine("[STS2iOS] GodotSharp bootstrapped successfully for iOS");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[STS2iOS] GodotSharp bootstrap failed: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    public static void Apply()
    {
        if (_applied)
            return;
        _applied = true;

        PatchHelper.Log("Initializing STS2iOS Mobile Patches...");

        _harmony = new Harmony("com.sts2.ios");

        try
        {
            AppPaths.EnsureDirectories();

            ModelDbInitPatch.Apply(_harmony);
            PlatformPatches.Apply(_harmony);
            SettingsPatches.Apply(_harmony);
            UiScalePatches.Apply(_harmony);
            MobileLayoutPatches.Apply(_harmony);
            EventLayoutPatches.Apply(_harmony);
            MerchantLayoutPatches.Apply(_harmony);
            AppLifecyclePatches.Apply(_harmony);
            TouchInputPatches.Apply(_harmony);
            CardRewardPatches.Apply(_harmony);
            EarlyAccessDisclaimerPatches.Apply(_harmony);
            CombatBackgroundPatches.Apply(_harmony);
            LanMultiplayerPatcher.Apply(_harmony);
            ModLoaderPatches.Apply(_harmony);
            SaveDiagnosticPatches.Apply(_harmony);

            PatchHelper.Log("All iOS game patches applied successfully.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Error applying patches: {ex.Message}");
        }
    }
}
