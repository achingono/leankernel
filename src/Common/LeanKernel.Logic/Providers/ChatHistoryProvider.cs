using LeanKernel.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides an implementation of the ChatHistoryProvider that uses Entity Framework for storing and retrieving chat history.
/// </summary>
public class DbChatHistoryProvider : ChatHistoryProvider
{

    public DbChatHistoryProvider(
        EntityContext entityContext,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? provideOutputMessageFilter = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputRequestMessageFilter = null,
        Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? storeInputResponseMessageFilter = null) : base(provideOutputMessageFilter, storeInputRequestMessageFilter, storeInputResponseMessageFilter)
    {
        // Use the entityContext for Entity Framework operations related to chat history.
    }
}