using System.Runtime.InteropServices;

namespace BionicAotProbe;

public static class Probe
{
    [UnmanagedCallersOnly(EntryPoint = "bionic_probe_add")]
    public static int Add(int a, int b) => a + b;
}
