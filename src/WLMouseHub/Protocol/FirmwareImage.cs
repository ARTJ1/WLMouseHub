using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WLMouseHub.Protocol;

/// <summary>
/// An Intel HEX firmware file turned into the device's 32-byte program packets.
/// This is a literal port of the WL MOUSE HUB loader so the bytes sent are identical to the official tool.
/// </summary>
public sealed class FirmwareImage
{
    public const int PacketSize = WlmTransport.ReportSize;

    public byte[] Data { get; }
    public IReadOnlyList<byte[]> Packets { get; }
    public uint BaseAddress { get; }

    private FirmwareImage(byte[] data, List<byte[]> packets, uint baseAddress)
    {
        Data = data;
        Packets = packets;
        BaseAddress = baseAddress;
    }

    public static FirmwareImage Parse(string hexText)
    {
        var extended = new List<int>();
        var data = new StringBuilder();
        var addresses = new StringBuilder();
        string previous = "";
        int u, h = 0, m = 0;

        string[] lines = hexText.Contains('\r') ? hexText.Replace("\n", "").Split('\r') : hexText.Split('\n');
        foreach (string line in lines)
        {
            if (line.Length < 9) continue;
            string type = line.Substring(7, 2);
            if (type == "04")
            {
                extended.Add(int.Parse(line.Substring(9, 4), NumberStyles.HexNumber));
            }
            else
            {
                u = int.Parse(line.Substring(3, 4), NumberStyles.HexNumber);
                if (u == 0) { h = 0; previous = line; }
                if (h == 0)
                {
                    h = u;
                    previous = line;
                }
                else
                {
                    int p = u - h;
                    if (p != 16)
                    {
                        for (int k = 0; k < p - m; k++) data.Append("00");
                    }
                    else if (previous.Substring(1, 2) != "10")
                    {
                        for (int k = 0; k < 16 - int.Parse(previous.Substring(1, 2), NumberStyles.HexNumber); k++) data.Append("00");
                    }
                    h = u;
                    previous = line;
                }
            }

            if (line[0] == ':')
            {
                if (line.Substring(1, 8) == "00000001") break;
                if (line[8] == '0' || line[8] == '1')
                {
                    data.Append(line, 9, line.Length - 11);
                    m = (line.Length - 11) / 2;
                    addresses.Append(line, 3, 4);
                }
            }
        }

        string hex = data.ToString();
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber);

        int firstAddress = int.Parse(addresses.ToString(0, 4), NumberStyles.HexNumber);
        uint baseAddress = (uint)(firstAddress + ((extended.Count > 0 ? extended[0] : 0) << 16));

        int rows = (bytes.Length + 15) / 16;
        var rowSizes = new int[rows];
        for (int i = 0; i < rows - 1; i++) rowSizes[i] = 16;
        rowSizes[rows - 1] = bytes.Length % 16 == 0 ? 16 : bytes.Length % 16;
        var rowAddress = new uint[rows];
        for (int i = 0; i < rows; i++) rowAddress[i] = baseAddress + (uint)(16 * i);

        return new FirmwareImage(bytes, BuildPackets(rowSizes, rowAddress, bytes), baseAddress);
    }

    private static List<byte[]> BuildPackets(int[] rowSizes, uint[] rowAddress, byte[] data)
    {
        bool even = rowSizes.Length % 2 == 0;
        int count = even ? rowSizes.Length / 2 : rowSizes.Length / 2 + 1;
        var packets = new List<byte[]>(count);
        int row = 0, offset = 0;

        for (int f = 0; f < count; f++)
        {
            int size = (even || f < count - 1) ? rowSizes[f * 2] + rowSizes[f * 2 + 1] : rowSizes[^1];
            uint address = rowAddress[row];
            if (even || f < count - 1) row += size % 16 == 0 ? size / 16 : 2;

            var p = new byte[PacketSize];
            p[3] = 0;            // chip index, filled in when sent
            p[4] = (byte)(size + 5);
            p[5] = 0xB0;
            p[6] = 0x02;
            p[7] = (byte)size;
            p[8] = (byte)(address >> 24);
            p[9] = (byte)(address >> 16);
            p[10] = (byte)(address >> 8);
            p[11] = (byte)address;
            for (int i = 0; i < size && offset + i < data.Length; i++) p[12 + i] = data[offset + i];
            offset += size;
            for (int i = 12; i < PacketSize; i++) p[i] ^= 0x55;
            packets.Add(p);
        }
        return packets;
    }

    /// <summary>"…_v01.00.02.10_…" → "1.0.2.10".</summary>
    public static string VersionFromFileName(string fileName)
    {
        foreach (string part in fileName.Split('_'))
        {
            if (!part.Contains('.')) continue;
            var numbers = part.Replace("v", "").Split('.')
                .Select(s => int.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0);
            return string.Join(".", numbers);
        }
        return "";
    }

    /// <summary>True when <paramref name="current"/> is older than <paramref name="available"/> (hub rules).</summary>
    public static bool IsNewer(string current, string available)
    {
        if (string.IsNullOrEmpty(available)) return false;
        var a = current.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
        var b = available.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (i == 0 && x == 9) continue;
            if (x != y) return x < y;
        }
        return false;
    }

    /// <summary>SHA-256 of all packets, for comparing against the official loader.</summary>
    public string Fingerprint() =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Packets.SelectMany(p => p).ToArray()));
}
