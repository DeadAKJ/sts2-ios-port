using System;
using Godot;

namespace STS2Mobile;

public partial class PatchLoader : Node
{
    public override void _EnterTree()
    {
        Console.Error.WriteLine("[STS2iOS] PatchLoader auto-executing on _EnterTree");
        try
        {
            ModEntry.Apply();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[STS2iOS] ModEntry.Apply failed: {ex}");
        }
    }
}
