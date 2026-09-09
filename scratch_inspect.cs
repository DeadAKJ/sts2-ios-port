using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom("lib/sts2.dll");
        var t = asm.GetType("MegaCrit.Sts2.Core.Nodes.CommonUi.NControllerManager");
        if (t == null)
        {
            Console.WriteLine("Type not found");
            return;
        }
        Console.WriteLine("Type: " + t.FullName);
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine("  Method: " + m.Name + " (" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine("  Field: " + f.Name + " : " + f.FieldType.Name);
        }
    }
}
