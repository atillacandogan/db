using System.Buffers.Binary;

namespace DbScanner.Infrastructure.Discovery;

internal static class TdsPreLoginProtocol
{
    private const int HeaderLength = 8;
    private const int OptionDirectoryEntryLength = 5;
    private const byte PreLoginPacketType = 0x12;
    private const byte PreLoginResponsePacketType = 0x04;
    private const byte EndOfMessageStatus = 0x01;
    private const byte TerminatorToken = 0xFF;
    private const byte VersionToken = 0x00;
    private const byte EncryptionToken = 0x01;
    private const byte InstanceToken = 0x02;
    private const byte ThreadIdToken = 0x03;
    private const byte MarsToken = 0x04;
    private const byte TraceIdToken = 0x05;
    private const byte FedAuthRequiredToken = 0x06;
    private const byte NonceToken = 0x07;

    public static byte[] CreateRequest()
    {
        TdsPreLoginOption[] options =
        [
            new(VersionToken, [0x0F, 0x00, 0x00, 0x00, 0x00, 0x00]),
            new(EncryptionToken, [0x00]),
            new(InstanceToken, [0x00]),
            new(ThreadIdToken, [0x00, 0x00, 0x00, 0x00]),
            new(MarsToken, [0x00])
        ];

        var optionDirectoryLength = checked(options.Length * OptionDirectoryEntryLength + 1);
        var payloadLength = checked(optionDirectoryLength + options.Sum(option => option.Data.Length));
        var packet = new byte[checked(HeaderLength + payloadLength)];

        packet[0] = PreLoginPacketType;
        packet[1] = EndOfMessageStatus;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), checked((ushort)packet.Length));
        packet[6] = 0x01;

        var directoryOffset = HeaderLength;
        var dataOffset = optionDirectoryLength;
        foreach (var option in options)
        {
            packet[directoryOffset] = option.Token;
            BinaryPrimitives.WriteUInt16BigEndian(
                packet.AsSpan(directoryOffset + 1, 2),
                checked((ushort)dataOffset));
            BinaryPrimitives.WriteUInt16BigEndian(
                packet.AsSpan(directoryOffset + 3, 2),
                checked((ushort)option.Data.Length));
            option.Data.CopyTo(packet, HeaderLength + dataOffset);

            directoryOffset += OptionDirectoryEntryLength;
            dataOffset += option.Data.Length;
        }

        packet[directoryOffset] = TerminatorToken;
        return packet;
    }

    public static bool IsValidResponse(ReadOnlySpan<byte> packet)
    {
        if (!TryGetDeclaredPacketLength(packet, out var declaredLength)
            || packet[0] != PreLoginResponsePacketType
            || (packet[1] & EndOfMessageStatus) == 0
            || declaredLength != packet.Length
            || packet.Length <= HeaderLength
            || packet[HeaderLength] != VersionToken)
        {
            return false;
        }

        var options = new Dictionary<byte, TdsPreLoginOptionLocation>();
        var directoryOffset = HeaderLength;
        var dataSectionOffset = -1;
        while (directoryOffset < packet.Length)
        {
            var token = packet[directoryOffset];
            if (token == TerminatorToken)
            {
                dataSectionOffset = directoryOffset + 1;
                break;
            }

            if (directoryOffset + OptionDirectoryEntryLength > packet.Length
                || options.ContainsKey(token))
            {
                return false;
            }

            var relativeOffset = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(directoryOffset + 1, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(directoryOffset + 3, 2));
            options[token] = new TdsPreLoginOptionLocation(HeaderLength + relativeOffset, length);
            directoryOffset += OptionDirectoryEntryLength;
        }

        if (dataSectionOffset < 0
            || !options.TryGetValue(VersionToken, out var version)
            || version.Length != 6
            || !options.TryGetValue(EncryptionToken, out var encryption)
            || encryption.Length != 1)
        {
            return false;
        }

        var occupiedRanges = new List<TdsPreLoginOptionLocation>();
        foreach (var (token, location) in options)
        {
            if (location.Offset < dataSectionOffset
                || location.Offset > packet.Length
                || location.Length > packet.Length - location.Offset
                || !HasValidKnownOptionShape(token, location, packet))
            {
                return false;
            }

            if (location.Length == 0)
            {
                continue;
            }

            if (occupiedRanges.Any(existing => RangesOverlap(existing, location)))
            {
                return false;
            }

            occupiedRanges.Add(location);
        }

        return true;
    }

    public static bool TryGetDeclaredPacketLength(ReadOnlySpan<byte> packet, out int length)
    {
        if (packet.Length < HeaderLength)
        {
            length = 0;
            return false;
        }

        length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        return length >= HeaderLength;
    }

    private static bool HasValidKnownOptionShape(
        byte token,
        TdsPreLoginOptionLocation location,
        ReadOnlySpan<byte> packet) => token switch
        {
            VersionToken => location.Length == 6,
            EncryptionToken => location.Length == 1
                && packet[location.Offset] is >= 0x00 and <= 0x03,
            InstanceToken => location.Length == 1
                && packet[location.Offset] is 0x00 or 0x01,
            ThreadIdToken => location.Length is 0 or 4,
            MarsToken => location.Length == 1
                && packet[location.Offset] is 0x00 or 0x01,
            TraceIdToken => location.Length == 36,
            FedAuthRequiredToken => location.Length == 1
                && packet[location.Offset] is 0x00 or 0x01,
            NonceToken => location.Length == 32,
            _ => true
        };

    private static bool RangesOverlap(
        TdsPreLoginOptionLocation left,
        TdsPreLoginOptionLocation right) =>
        left.Offset < right.Offset + right.Length
        && right.Offset < left.Offset + left.Length;

    private sealed record TdsPreLoginOption(byte Token, byte[] Data);

    private readonly record struct TdsPreLoginOptionLocation(int Offset, int Length);
}
