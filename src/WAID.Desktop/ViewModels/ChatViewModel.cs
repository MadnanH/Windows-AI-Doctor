using System.Collections.ObjectModel;
using System.Windows.Input;
using WAID.Application.Abstractions;

namespace WAID.Desktop.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    private readonly IChatAssistant _chat;
    private readonly AsyncCommand _send;
    private Guid? _conversationId;
    private string _question = "";
    private string _status = "Offline provider ready.";

    public ChatViewModel(IChatAssistant chat) { _chat = chat; _send = new(SendAsync); }
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public string Question { get => _question; set => Set(ref _question, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand SendCommand => _send;

    private async Task SendAsync()
    {
        try
        {
            Status = "Retrieving WAID evidence...";
            var conversation = await _chat.AskAsync(_conversationId, Question, CancellationToken.None);
            _conversationId = conversation.Id;
            Messages.Clear();
            foreach (var message in conversation.Messages) Messages.Add(message);
            Question = "";
            Notify(nameof(Question));
            Status = "Answered using saved WAID evidence. Repairs require the normal approval workflow.";
        }
        catch (Exception exception) { Status = $"Chat could not answer: {exception.Message}"; }
    }
}
