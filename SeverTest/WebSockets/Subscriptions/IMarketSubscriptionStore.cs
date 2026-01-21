using System.Collections.Generic;

namespace ServerTest.WebSockets.Subscriptions
{
    public interface IMarketSubscriptionStore
    {
        void Subscribe(string userId, IEnumerable<string> symbols);
        void Unsubscribe(string userId, IEnumerable<string> symbols);
        IReadOnlyCollection<string> GetSubscriptions(string userId);
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetAllSubscriptions();
    }
}
