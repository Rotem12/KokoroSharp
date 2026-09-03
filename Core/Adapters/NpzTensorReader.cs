namespace KokoroSharp.Adapters;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record NpzFloatTensor(float[] Values, int[] Shape);

/// <summary>
/// Small, dependency-free reader for the float32 NPY entries used by the
/// Wayu voice and source-parameter packs.
/// </summary>
internal static partial class NpzTensorReader
{
    public static IReadOnlyList<string> ListKeys(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries
            .Where(entry => entry.FullName.EndsWith(".npy", StringComparison.OrdinalIgnoreCase))
            .Select(entry => Path.GetFileNameWithoutExtension(entry.FullName))
            .ToArray();
    }

    public static NpzFloatTensor ReadFloat32(string path, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry($"{key}.npy")
            ?? throw new FileNotFoundException($"The NPY entry '{key}' was not found.", path);
        using var stream = entry.Open();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        var magic = reader.ReadBytes(6);
        if (!magic.SequenceEqual(new byte[] { 0x93, (byte) 'N', (byte) 'U', (byte) 'M', (byte) 'P', (byte) 'Y' }))
            throw new InvalidDataException($"The NPY entry '{key}' has an invalid magic header.");

        var major = reader.ReadByte();
        _ = reader.ReadByte();
        var headerLength = major switch
        {
            1 => reader.ReadUInt16(),
            2 or 3 => reader.ReadUInt32(),
            _ => throw new InvalidDataException($"The NPY entry '{key}' uses unsupported version {major}.")
        };
        var header = Encoding.ASCII.GetString(reader.ReadBytes(checked((int) headerLength)));

        var descriptor = DescriptorRegex().Match(header).Groups["value"].Value;
        if (descriptor is not "<f4" and not "|f4" and not "=f4")
            throw new InvalidDataException($"The NPY entry '{key}' is '{descriptor}', expected little-endian float32.");

        var shapeText = ShapeRegex().Match(header).Groups["value"].Value;
        var shape = shapeText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();
        if (shape.Length == 0 || shape.Any(dimension => dimension < 0))
            throw new InvalidDataException($"The NPY entry '{key}' has an invalid shape.");

        using var payload = new MemoryStream();
        stream.CopyTo(payload);
        var bytes = payload.ToArray();
        if (bytes.Length % sizeof(float) != 0)
            throw new InvalidDataException($"The NPY entry '{key}' payload is not float32-sized.");

        var expectedValues = shape.Aggregate(1L, (total, dimension) => checked(total * dimension));
        if (expectedValues != bytes.Length / sizeof(float))
            throw new InvalidDataException($"The NPY entry '{key}' shape does not match its payload.");

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return new NpzFloatTensor(values, shape);
    }

    [GeneratedRegex(@"['""]descr['""]\s*:\s*['""](?<value>[^'""]+)['""]")]
    private static partial Regex DescriptorRegex();

    [GeneratedRegex(@"['""]shape['""]\s*:\s*\((?<value>[^)]*)\)")]
    private static partial Regex ShapeRegex();
}
