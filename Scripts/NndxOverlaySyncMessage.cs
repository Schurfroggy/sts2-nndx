using System.Buffers.Binary;

namespace Nndx.Scripts;

internal readonly struct NndxOverlaySyncMessage
{
    public ulong PlayerNetId { get; init; }

    public bool IsPlaying { get; init; }

    public static byte[] Serialize(NndxOverlaySyncMessage message)
    {
        byte[] buffer = new byte[9];
        buffer[0] = message.IsPlaying ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(1), message.PlayerNetId);
        return buffer;
    }

    public static NndxOverlaySyncMessage Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9)
        {
            throw new InvalidDataException($"NNDX overlay sync payload too short ({data.Length} bytes).");
        }

        return new NndxOverlaySyncMessage
        {
            IsPlaying = data[0] != 0,
            PlayerNetId = BinaryPrimitives.ReadUInt64LittleEndian(data[1..]),
        };
    }
}
