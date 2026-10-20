using System;
using System.Collections.Generic;
using System.Threading;

namespace WLMouseHub.Protocol;

/// <summary>One picture stored on (or going to) the receiver screen.</summary>
/// <param name="Type">4 = GIF file bytes (224x224), 1 = raw RGB565 big-endian (360x360).</param>
public sealed record ScreenImage(int Width, int Height, int Type, byte[] Data);

public enum ScreenFlag { Status, Effect, Voice }

/// <summary>
/// Receiver (dongle) screen of the HUAN family, command class 5 on the receiver (target 0), 64-byte channel.
/// Recovered from WL MOUSE HUB: setLocateImageLCD / setImageToFw / setUpdateFinishState / getImageInfo / setDeleteUsrImage.
/// </summary>
public sealed partial class WlmMouse
{
    public const int ScreenSizeLimitKb = 650;
    public const int GifSide = 224;
    public const int PictureSide = 360;
    private const byte Dongle = 0;
    private const int ChunkOut = 32;   // image bytes per write packet on the 64-byte channel
    private const int ChunkIn = 51;    // image bytes per read packet

    /// <summary>The screen is on the receiver, so it is reachable only through a receiver connection.</summary>
    public bool ScreenSupported => Model.ScreenSetEnable == "1" && !IsWired && !Model.UsesWideReceiverChannel;

    // ---- switches ----------------------------------------------------------------------

    public bool GetScreenFlag(ScreenFlag f)
    {
        var r = f == ScreenFlag.Voice ? _io.Transact(Dongle, 1, 6, 0x80) : _io.Transact(Dongle, 1, 5, (byte)(f == ScreenFlag.Status ? 0x8A : 0x86));
        return r[7] != 0;
    }

    public void SetScreenFlag(ScreenFlag f, bool on)
    {
        byte v = (byte)(on ? 1 : 0);
        if (f == ScreenFlag.Voice) _io.Transact(Dongle, 1, 6, 0x00, v);
        else _io.Transact(Dongle, 1, 5, (byte)(f == ScreenFlag.Status ? 0x0A : 0x06), v);
    }

    // ---- images ------------------------------------------------------------------------

    /// <summary>Reads every stored picture: [0] is the main one, the rest are the random ones.</summary>
    public List<ScreenImage> GetScreenImages(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var list = new List<ScreenImage>();
        var info = _io.Transact(Dongle, 1, 5, 0x07, 2);
        int count = info[8];
        if (count is 0 or 255) return list;
        count = Math.Min(count, 8);

        for (int d = 1; d <= count; d++)
        {
            var head = _io.Transact(Dongle, 11, 5, 0x88, 0, (byte)d);
            int size = (head[9] << 24) | (head[10] << 16) | (head[11] << 8) | head[12];
            int w = (head[13] << 8) | head[14], h = (head[15] << 8) | head[16], type = head[17];
            if (size <= 0 || size > 2 * 1024 * 1024) continue;
            var data = new byte[size];
            int packets = (size + ChunkIn - 1) / ChunkIn;
            for (int b = 0; b < packets; b++)
            {
                ct.ThrowIfCancellationRequested();
                int offset = b * ChunkIn, n = Math.Min(ChunkIn, size - offset);
                var r = ReadImageChunk(d, offset, n);
                Array.Copy(r, 13, data, offset, n);
                if (b % 40 == 0) progress?.Report(((d - 1) * 100 + b * 100 / packets) / count);
            }
            list.Add(new ScreenImage(w, h, type, data));
        }
        progress?.Report(100);
        return list;
    }

    private byte[] ReadImageChunk(int index, int offset, int n)
    {
        var req = LcdPacket((byte)(n + 6), 0x88, 1, (byte)index,
            (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            _io.SetRaw(req);
            for (int poll = 0; poll < 12; poll++)
            {
                var r = _io.GetRaw();
                // The reply echoes the requested offset at [9..12].
                if (r[1] == 0xA1 && r[6] == 0x88 && r[9] == req[9] && r[10] == req[10] && r[11] == req[11] && r[12] == req[12]) return r;
                Thread.Sleep(1);
            }
        }
        throw new WlmException($"Reading the screen image failed at byte {offset}.");
    }

    /// <summary>Replaces the stored pictures: images[0] becomes the main one, the rest the random ones.</summary>
    public void UploadScreenImages(IReadOnlyList<ScreenImage> images, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (images.Count == 0) throw new ArgumentException("No main image");
        long total = 0;
        foreach (var img in images) total += img.Data.Length;
        if (total / 1024 >= ScreenSizeLimitKb) throw new WlmException($"The pictures are larger than {ScreenSizeLimitKb} KB together.");

        LcdSend(LcdPacket(1, 0x04, 2));                               // setLocateImageLCD(2)
        long done = 0;
        foreach (var img in images)
        {
            // header: offset 0, then total length, width, height, type (9 bytes)
            int len = img.Data.Length;
            LcdSend(LcdPacket(13, 0x00, 0, 0, 0, 0,
                (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len,
                (byte)(img.Width >> 8), (byte)img.Width, (byte)(img.Height >> 8), (byte)img.Height, (byte)img.Type));

            for (int pos = 0; pos < len; pos += ChunkOut)
            {
                ct.ThrowIfCancellationRequested();
                int n = Math.Min(ChunkOut, len - pos);
                int offset = 9 + pos;
                var p = LcdPacket((byte)(4 + n), 0x00, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset);
                Array.Copy(img.Data, pos, p, 11, n);
                LcdSend(p);
                done += n;
                if (pos % (ChunkOut * 64) == 0) progress?.Report((int)(done * 100 / total));
            }
            Thread.Sleep(50);
            var check = LcdPacket(5, 0x80);                           // getImageToFw
            check[11] = 9;
            LcdSend(check);
            Thread.Sleep(50);
        }
        LcdSend(LcdPacket(1, 0x05));                                  // setUpdateFinishState
        progress?.Report(100);
    }

    public void DeleteScreenImages() => LcdSend(LcdPacket(1, 0x09, 2));

    private static byte[] LcdPacket(byte length, byte cmd, params byte[] payload)
    {
        var b = new byte[WlmTransport.ReportSize];
        b[3] = Dongle;
        b[4] = length;
        b[5] = 5;
        b[6] = cmd;
        Array.Copy(payload, 0, b, 7, payload.Length);
        return b;
    }

    /// <summary>Hub retrySetGetWithDelay: done when the status is A1 or 02, resend on errors.</summary>
    private void LcdSend(byte[] packet)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            _io.SetRaw(packet);
            for (int poll = 0; poll < 40; poll++)
            {
                var r = _io.GetRaw();
                if (r[1] is 0xA1 or 0x02 && r[5] == packet[5] && r[6] == packet[6]) return;
                if (r[1] > 0xA1) break;
                Thread.Sleep(poll < 10 ? 1 : 10);
            }
        }
        throw new WlmException("The receiver did not accept the screen data.");
    }
}
