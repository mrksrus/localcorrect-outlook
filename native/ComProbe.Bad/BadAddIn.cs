using System;
using System.Runtime.InteropServices;
[assembly: ComVisible(false)]
namespace LocalCorrect.ComProbe {
    // The shape shipped in rc.2 and rc.4: Office's IID, but not ComImport, and
    // `ref Array` instead of a SAFEARRAY of VARIANT. Used only to see whether
    // that shape faults clr.dll under COM activation.
    [ComVisible(true), Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface BadExtensibility {
        void OnConnection(object application, int connectMode, object addInInst, ref Array custom);
        void OnDisconnection(int removeMode, ref Array custom);
        void OnAddInsUpdate(ref Array custom);
        void OnStartupComplete(ref Array custom);
        void OnBeginShutdown(ref Array custom);
    }
    [ComVisible(true), Guid("708F01D3-893E-43D0-8215-61722BC46F24"), ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class BadAddIn : BadExtensibility {
        public void OnConnection(object application, int connectMode, object addInInst, ref Array custom) { }
        public void OnDisconnection(int removeMode, ref Array custom) { }
        public void OnAddInsUpdate(ref Array custom) { }
        public void OnStartupComplete(ref Array custom) { }
        public void OnBeginShutdown(ref Array custom) { }
    }
}
