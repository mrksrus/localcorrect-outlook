using System;
using System.Runtime.InteropServices;
namespace LocalCorrect.Outlook {
    // These GUIDs already belong to Office (extensibility and the ribbon typelib).
    // ComImport makes the CLR adopt those imported interfaces. Without it the CLR
    // exports our own definition under the same IID, and Outlook's first call
    // (OnConnection, before any line of ours) enters a stub whose SAFEARRAY
    // argument does not match the type library. That access violation is inside
    // clr.dll, so no managed log exists and try/catch never runs.
    // `custom` is a SAFEARRAY of VARIANT: object[], not System.Array.
    [ComImport, ComVisible(true), Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IDTExtensibility2 {
        [DispId(1)] void OnConnection([MarshalAs(UnmanagedType.IDispatch)] object application, int connectMode, [MarshalAs(UnmanagedType.IDispatch)] object addInInst, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(2)] void OnDisconnection(int removeMode, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(3)] void OnAddInsUpdate([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(4)] void OnStartupComplete([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(5)] void OnBeginShutdown([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
    }
    [ComImport, ComVisible(true), Guid("000C0396-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IRibbonExtensibility {
        [DispId(1)] [return: MarshalAs(UnmanagedType.BStr)] string GetCustomUI([MarshalAs(UnmanagedType.BStr)] string ribbonId);
    }
    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow { void GetWindow(out IntPtr hwnd); void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode); }
}
