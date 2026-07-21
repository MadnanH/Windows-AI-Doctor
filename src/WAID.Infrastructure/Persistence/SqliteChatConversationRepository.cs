using System.Text.Json;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Persistence;

public sealed class SqliteChatConversationRepository(WaidDatabase database) : IChatConversationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatConversation?> GetAsync(Guid id, CancellationToken token)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT conversation_json FROM chat_conversations WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        var json = await command.ExecuteScalarAsync(token) as string;
        return json is null ? null : JsonSerializer.Deserialize<ChatConversation>(json, JsonOptions);
    }

    public async Task SaveAsync(ChatConversation conversation, CancellationToken token)
    {
        if (conversation.IsDeleted) throw new InvalidOperationException("Deleted conversations cannot be saved as active.");
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO chat_conversations(id,updated_utc,is_deleted,exported,conversation_json) VALUES($id,$time,0,$exported,$json) ON CONFLICT(id) DO UPDATE SET updated_utc=$time,is_deleted=0,exported=$exported,conversation_json=$json";
        command.Parameters.AddWithValue("$id", conversation.Id.ToString());
        command.Parameters.AddWithValue("$time", conversation.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$exported", conversation.Exported ? 1 : 0);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(conversation, JsonOptions));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task DeleteAsync(Guid id, DateTimeOffset deletedAtUtc, CancellationToken token)
    {
        var conversation = await GetAsync(id, token) ?? throw new KeyNotFoundException("Conversation not found.");
        var deleted = conversation with { Messages = [], IsDeleted = true, DeletedAtUtc = deletedAtUtc, UpdatedAtUtc = deletedAtUtc };
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE chat_conversations SET updated_utc=$time,is_deleted=1,conversation_json=$json WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$time", deletedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(deleted, JsonOptions));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task MarkExportedAsync(Guid id, CancellationToken token)
    {
        var conversation = await GetAsync(id, token) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.IsDeleted) throw new InvalidOperationException("Deleted conversations cannot be exported.");
        await SaveAsync(conversation with { Exported = true }, token);
    }
}
