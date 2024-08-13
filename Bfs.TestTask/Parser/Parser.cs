using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace Bfs.TestTask.Parser;

public class Parser : IParser
{
    private const byte Separator = 28;
    private int _length;
    public async IAsyncEnumerable<IMessage> Parse(ChannelReader<ReadOnlyMemory<byte>> source)
    {
        while (source.TryRead(out var memoryBuffer))
        {
            int? messageType = null;
            var messageLengthBuffer = memoryBuffer.Slice(0, 2).ToArray();
            _length = messageLengthBuffer[0] * 256 + messageLengthBuffer[1];
            var bodyBuffer = new byte[_length];
            var messageLength = _length;
            if (memoryBuffer.Length >= 3)
            {
                messageType = GetMessageType(memoryBuffer);
                ParsePartBody(bodyBuffer, memoryBuffer.Slice(2), ref messageLength);
            }

            while (messageLength > 0)
            {
                memoryBuffer = await source.ReadAsync();
                if (messageType is null)
                {
                    messageType = GetMessageType(memoryBuffer);
                }
                ParsePartBody(bodyBuffer, memoryBuffer, ref messageLength);
            }

            yield return messageType switch
            {
                0 => ParseCardReaderState(bodyBuffer.AsMemory().Slice(3)),
                1 => ParseOtherMessages(bodyBuffer.AsMemory().Slice(3)),
                _ => throw new InvalidOperationException()
            };

            await source.WaitToReadAsync();
        }
    }

    private void ParsePartBody(byte[] dest, ReadOnlyMemory<byte> source, ref int length)
    {
        source.CopyTo(dest.AsMemory(_length - length));
        length -= source.Length;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int? GetMessageType(ReadOnlyMemory<byte> buffer)
    {
        int? messageType;
        var firstBitBuffer = buffer.Slice(2, 1);
        var firstBit = Encoding.ASCII.GetString(firstBitBuffer.Span);
        if (firstBit == "1")
            messageType = 0;
        else
            messageType = 1;
        return messageType;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IMessage ParseCardReaderState(ReadOnlyMemory<byte> buffer)
    {
        //00100355D1001
        GetLuno(buffer, out var luno, out var separatorIndex);
        var state = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 2).Span);
        return new CardReaderState(luno, state[0], state[1].ToInt(), state[2].ToInt(), state[3].ToInt(), state[4].ToInt());

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetLuno(ReadOnlyMemory<byte> buffer, out string luno, out int separatorIndex)
    {
        separatorIndex = buffer.Span.IndexOf(Separator);
        luno = Encoding.ASCII.GetString(buffer.Slice(0, separatorIndex).Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IMessage ParseOtherMessages(ReadOnlyMemory<byte> buffer)
    {
        var separatorCount = buffer.Span.Count(Separator);
        if (separatorCount == 4)
        {
            return ParseSendStatus(buffer);
        }

        return ParseGetFitnessData(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IMessage ParseSendStatus(ReadOnlyMemory<byte> buffer)
    {
        GetLuno(buffer, out var luno, out var separatorIndex);
        var statusDescriptor = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 2, 1).Span)[0];
        var tranNumber = int.Parse(Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 5).Span));
        return new SendStatus(luno, statusDescriptor, tranNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IMessage ParseGetFitnessData(ReadOnlyMemory<byte> buffer)
    {
        GetLuno(buffer, out var luno, out var separatorIndex);
        var statusDescriptor = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 2, 1).Span)[0];
        var messageIdentifier = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 4, 1).Span)[0];
        var hardwareIdentifier = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 5, 1).Span)[0];
        var statusInformation = Encoding.ASCII.GetString(buffer.Slice(separatorIndex + 6).Span);
        var fitness = statusInformation.Split('\u001d').Select(x => new FitnessState(x[0], x[1..]));

        return new GetFitnessData(luno, statusDescriptor,messageIdentifier, hardwareIdentifier, fitness.ToArray());
    }
}

public static class Extensions
{
    public static int ToInt(this char c)
    {
        return (int)(c - '0');
    }
}

 /*
       Message with type Get Fitness Data builded:
       2200100355FJAD01y1A0E00000G0L0w00040003000200010H0
       Description:
       (b) 2 = Message class
       (c) 2 = Message sub-class
       (d) 00100355 = LUNO
       MagneticCardReader RoutineErrorsHaveOccurred,SecondaryCardReader RoutineErrorsHaveOccurred,TimeOfDayClock NoError,CashHandler NoError,ReceiptPrinter NoError,Encryptor NoError,BunchNoteAcceptor NoError,JournalPrinter NoError
       (f) F = Status Descriptor (TerminalState)
       Status Information
       (g1) J = Message Identifier (FitnessData)
       (g2) A = Hardware Fitness Identifier
       (g2) D = Device Identifier Graphic MagneticCardReader
       (g2) 01 = Fitness - RoutineErrorsHaveOccurred
       (g2) y = Device Identifier Graphic SecondaryCardReader
       (g2) 1 = Fitness - RoutineErrorsHaveOccurred
       (g2) A = Device Identifier Graphic TimeOfDayClock
       (g2) 0 = Fitness - NoError
       (g2) E = Device Identifier Graphic CashHandler
       (g2) 00000 = Fitness - NoError
       (g2) G = Device Identifier Graphic ReceiptPrinter
       (g2) 0 = Fitness - NoError
       (g2) L = Device Identifier Graphic Encryptor
       (g2) 0 = Fitness - NoError
       (g2) w = Device Identifier Graphic BunchNoteAcceptor
       (g2) 00040003000200010 = Fitness - NoError
       (g2) H = Device Identifier Graphic JournalPrinter
       (g2) 0 = Fitness - NoError

     */