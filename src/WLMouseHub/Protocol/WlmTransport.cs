using System;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using WLMouseHub.Hid;

namespace WLMouseHub.Protocol;

public class WlmException : Exception
{
    public WlmException(string message) : base(message) { }
}

/// <summary>The device answered the command with an error status (for example an empty macro slot, or an unsupported feature).</summary>
public sealed class WlmRejectedException : WlmException
{
    public byte Status { get; }

    public WlmRejectedException(byte cls, byte cmd, byte status)
        : base($"The mouse rejected command {cls:X2}/{cmd:X2} (status {status:X2}).") => Status = status;
}

/// <summary>
/// Request/response channel over the 64-byte vendor feature report.
/// Buffer layout (Windows, report id at [0]): [1] status, [3] target, [4] length, [5] class, [6] command, [7..] payload.
/// </summary>
public sealed class WlmTransport : IDisposable
{
    public const int ReportSize = 65;
    private const byte StatusOk = 0xA1;

    private readonly SafeFileHandle _handle;
    private readonly object _gate = new();

    public WlmTransport(string path)
    {
        _handle = HidNative.CreateFile(path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
            throw new WlmException("Cannot open the mouse configuration interface. Close the web hub if it is connected.");
    }

    /// <summary>Sends one command and returns the 65-byte reply.</summary>
    public byte[] Transact(byte target, byte length, byte cls, byte cmd, params byte[] payload)
    {
        byte[] request = Build(target, length, cls, cmd, payload);
        lock (_gate)
        {
            int rejected = 0;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!HidNative.HidD_SetFeature(_handle, request, ReportSize))
                    throw new WlmException("Write to the mouse failed (device unplugged?)");

                for (int poll = 0; poll < 40; poll++)
                {
                    Thread.Sleep(poll < 5 ? 4 : 15);
                    var reply = new byte[ReportSize];
                    if (!HidNative.HidD_GetFeature(_handle, reply, ReportSize)) continue;
                    bool ours = reply[5] == cls && reply[6] == cmd;
                    if (reply[1] == StatusOk && ours) return reply;
                    if (reply[1] > StatusOk)
                    {
                        // An error status for this very command twice in a row is a real answer, not a glitch.
                        if (ours && ++rejected >= 2) throw new WlmRejectedException(cls, cmd, reply[1]);
                        break; // resend
                    }
                }
            }
        }
        throw new WlmException($"No answer to command {cls:X2}/{cmd:X2}. If the mouse is wireless, move it to wake it up.");
    }

    /// <summary>Sends a command the device does not acknowledge (factory reset).</summary>
    public void SendOnly(byte target, byte length, byte cls, byte cmd, params byte[] payload)
    {
        lock (_gate)
        {
            if (!HidNative.HidD_SetFeature(_handle, Build(target, length, cls, cmd, payload), ReportSize))
                throw new WlmException("Write to the mouse failed");
            Thread.Sleep(50);
        }
    }

    /// <summary>Writes a prepared 65-byte report (firmware packets carry their own layout).</summary>
    public void SetRaw(byte[] report)
    {
        lock (_gate)
        {
            if (!HidNative.HidD_SetFeature(_handle, report, ReportSize))
                throw new WlmException("Write to the device failed");
        }
    }

    /// <summary>Reads the current 65-byte reply buffer.</summary>
    public byte[] GetRaw()
    {
        var reply = new byte[ReportSize];
        lock (_gate)
        {
            if (!HidNative.HidD_GetFeature(_handle, reply, ReportSize))
                throw new WlmException("Read from the device failed");
        }
        return reply;
    }

    private static byte[] Build(byte target, byte length, byte cls, byte cmd, byte[] payload)
    {
        if (payload.Length > ReportSize - 7) throw new ArgumentException("payload too long");
        var request = new byte[ReportSize];
        request[3] = target;
        request[4] = length;
        request[5] = cls;
        request[6] = cmd;
        Array.Copy(payload, 0, request, 7, payload.Length);
        return request;
    }

    public void Dispose() => _handle.Dispose();
}
