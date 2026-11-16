using System.Runtime.InteropServices;
[assembly: ComVisible(false)]
namespace LocalCorrect.ComProbe {
    // Compiled as C# latest so Roslyn emits [module: RefSafetyRules(11)].
    // net48 has no such attribute. Activation of this class is the control for
    // whether that metadata alone faults the Framework CLR.
    [ComVisible(true), Guid("708F01D3-893E-43D0-8215-61722BC46F25"), ClassInterface(ClassInterfaceType.None)]
    public sealed class ProbeRefSafety {
        public int Ping() { return 1; }
    }
}
