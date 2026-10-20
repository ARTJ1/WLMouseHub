using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WLMouseHub.Hid;

/// <summary>One HID top-level collection as Windows exposes it.</summary>
public sealed record HidDeviceInfo(
    string Path, ushort VendorId, ushort ProductId,
    ushort UsagePage, ushort Usage, int FeatureLength, string Product)
{
    public static List<HidDeviceInfo> Enumerate(ushort? vendorId = null)
    {
        var result = new List<HidDeviceInfo>();
        HidNative.HidD_GetHidGuid(out var guid);
        IntPtr set = HidNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            var data = new HidNative.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                string? path = GetPath(set, ref data);
                if (path == null) continue;
                var info = Probe(path, vendorId);
                if (info != null) result.Add(info);
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    private static string? GetPath(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA data)
    {
        HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidDeviceInfo? Probe(string path, ushort? vendorId)
    {
        // Access 0 lets us query attributes even for collections Windows holds exclusively (mouse, keyboard).
        using SafeFileHandle h = HidNative.CreateFile(path, 0,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;

        var attr = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (!HidNative.HidD_GetAttributes(h, ref attr)) return null;
        if (vendorId.HasValue && attr.VendorID != vendorId.Value) return null;

        HidNative.HIDP_CAPS caps = default;
        if (HidNative.HidD_GetPreparsedData(h, out IntPtr pre))
        {
            HidNative.HidP_GetCaps(pre, out caps);
            HidNative.HidD_FreePreparsedData(pre);
        }

        var name = new byte[256];
        string product = HidNative.HidD_GetProductString(h, name, name.Length)
            ? Encoding.Unicode.GetString(name).TrimEnd('\0')
            : "";

        return new HidDeviceInfo(path, attr.VendorID, attr.ProductID,
            caps.UsagePage, caps.Usage, caps.FeatureReportByteLength, product);
    }
}
