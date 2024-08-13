using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Bfs.TestTask.Driver;

public class CardDriverMock : ICardDriverMock
{
    private readonly Channel<CardDriverContext> _contextChannel;

    public CardDriverMock()
    {
        _contextChannel = Channel.CreateUnbounded<CardDriverContext>();
    }

    public async Task<CardData?> ReadCard(CancellationToken cancellationToken)
    {
        try
        {
            var ctx = await _contextChannel.Reader.ReadAsync(cancellationToken);
            return ctx.Card;

        } catch (OperationCanceledException)
        {
            return default;
        }
    }

    public async IAsyncEnumerable<EjectResult> EjectCard(CancellationToken cancellationToken)
    {
        await _contextChannel.Writer.WriteAsync(CardDriverContext.Ejected());
        do
        {
            var item = await _contextChannel.Reader.ReadAsync();
            yield return item.Result;
        } while (!cancellationToken.IsCancellationRequested);
        
        _contextChannel.Writer.Complete();
        yield return EjectResult.Retracted;
    }

    public void SetCardData(CardData cardData)
    {
        _contextChannel.Writer.WriteAsync(CardDriverContext.ReadCard(cardData));
    }

    public void CantReadCard()
    {
        _contextChannel.Writer.WriteAsync(CardDriverContext.CantReadCard());
    }

    public void TakeCard()
    {
        _contextChannel.Writer.WriteAsync(CardDriverContext.CardTaken());
    }

    private class CardDriverContext
    {
        public CardData? Card { get; private set; }

        public EjectResult Result { get; private set; }

        private CardDriverContext(CardData? card, EjectResult result)
        {
            Card = card;
            Result = result;
        }

        public static CardDriverContext Ejected() => new CardDriverContext(default, EjectResult.Ejected);
        public static CardDriverContext ReadCard(CardData cardData) => new CardDriverContext(cardData, EjectResult.Ejected);
        public static CardDriverContext CantReadCard() => new CardDriverContext(default, EjectResult.Ejected);
        public static CardDriverContext CardTaken() => new CardDriverContext(default, EjectResult.CardTaken);
    }
}