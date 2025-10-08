using Godot;
using System;
using System.Linq;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Domain.Chat;

namespace Waterjam.UI.Components;

/// <summary>
/// UI component for party chat, visible when the player is in a party.
/// </summary>
public partial class PartyChatPanel : PanelContainer,
    IGameEventHandler<PartyCreatedEvent>,
    IGameEventHandler<PartyJoinedEvent>,
    IGameEventHandler<PartyLeftEvent>,
    IGameEventHandler<PartyDisbandedEvent>,
    IGameEventHandler<PartyChatMessageEvent>,
    IGameEventHandler<PartyMemberJoinedEvent>,
    IGameEventHandler<PartyMemberLeftEvent>
{
    private VBoxContainer _container;
    private RichTextLabel _chatLog;
    private LineEdit _messageInput;
    private Button _sendButton;
    private Button _toggleButton;
    private bool _isExpanded = true;
    
    private string _currentPartyId;
    
    public override void _Ready()
    {
        Name = "PartyChatPanel";
        CustomMinimumSize = new Vector2(300, 200);
        
        // Position in bottom-left corner
        AnchorLeft = 0f;
        AnchorRight = 0f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = 8f;
        OffsetRight = 308f;
        OffsetTop = -208f;
        OffsetBottom = -8f;
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.Begin;
        
        BuildUI();
        UpdateVisibility();
    }
    
    private void BuildUI()
    {
        _container = new VBoxContainer();
        _container.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_container);
        
        // Header with title and toggle
        var header = new HBoxContainer();
        var title = new Label { Text = "Party Chat", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _toggleButton = new Button { Text = "−", CustomMinimumSize = new Vector2(32, 0) };
        _toggleButton.Pressed += OnTogglePressed;
        header.AddChild(title);
        header.AddChild(_toggleButton);
        _container.AddChild(header);
        
        // Chat log
        _chatLog = new RichTextLabel();
        _chatLog.BbcodeEnabled = true;
        _chatLog.ScrollFollowing = true;
        _chatLog.FitContent = true;
        _chatLog.CustomMinimumSize = new Vector2(0, 120);
        _chatLog.SizeFlagsVertical = SizeFlags.ExpandFill;
        _container.AddChild(_chatLog);
        
        // Input row
        var inputRow = new HBoxContainer();
        _messageInput = new LineEdit();
        _messageInput.PlaceholderText = "Type a message...";
        _messageInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _messageInput.TextSubmitted += OnMessageSubmitted;
        
        _sendButton = new Button { Text = "Send" };
        _sendButton.Pressed += OnSendPressed;
        
        inputRow.AddChild(_messageInput);
        inputRow.AddChild(_sendButton);
        _container.AddChild(inputRow);
    }
    
    private void OnTogglePressed()
    {
        _isExpanded = !_isExpanded;
        _toggleButton.Text = _isExpanded ? "−" : "+";
        _chatLog.Visible = _isExpanded;
        _messageInput.Visible = _isExpanded;
        _sendButton.GetParent<HBoxContainer>().Visible = _isExpanded;
        
        CustomMinimumSize = _isExpanded ? new Vector2(300, 200) : new Vector2(300, 40);
    }
    
    private void OnSendPressed()
    {
        SendMessage();
    }
    
    private void OnMessageSubmitted(string text)
    {
        SendMessage();
    }
    
    private void SendMessage()
    {
        var message = _messageInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(_currentPartyId))
        {
            return;
        }
        
        try
        {
            var partyService = GetNodeOrNull("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
            if (partyService != null)
            {
                var localPlayerId = partyService.GetLocalPlayerId();
                var displayName = "You";
                
                if (Waterjam.Core.Services.PlatformService.IsSteamInitialized)
                {
                    try
                    {
                        displayName = GodotSteam.Steam.GetPersonaName();
                    }
                    catch { }
                }
                
                partyService.SendPartyChatMessage(_currentPartyId, localPlayerId, displayName, message);
                _messageInput.Text = "";
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[PartyChatPanel] Failed to send message: {ex.Message}", ConsoleChannel.UI);
        }
    }
    
    private void UpdateVisibility()
    {
        try
        {
            var partyService = GetNodeOrNull("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
            if (partyService == null)
            {
                Visible = false;
                return;
            }
            
            var currentParty = partyService.GetCurrentPlayerParty();
            
            // Show chat if we're in a party with more than one member
            Visible = currentParty != null && currentParty.Members.Count > 1;
            
            if (Visible && currentParty != null)
            {
                _currentPartyId = currentParty.PartyId;
                RefreshChatLog();
            }
            else
            {
                _currentPartyId = null;
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[PartyChatPanel] Failed to update visibility: {ex.Message}", ConsoleChannel.UI);
            Visible = false;
        }
    }
    
    private void RefreshChatLog()
    {
        if (string.IsNullOrEmpty(_currentPartyId))
        {
            return;
        }
        
        try
        {
            var partyService = GetNodeOrNull("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
            if (partyService == null)
            {
                return;
            }
            
            var chatChannel = partyService.GetPartyChatChannel(_currentPartyId);
            if (chatChannel == null)
            {
                return;
            }
            
            _chatLog.Clear();
            
            // MessageHistory is newest first, so reverse for display
            var recentMessages = chatChannel.GetRecentMessages(50);
            foreach (var msg in recentMessages.Reverse())
            {
                var timestamp = msg.SentAt.ToLocalTime().ToString("HH:mm");
                var color = msg.SenderPlayerId == partyService.GetLocalPlayerId() ? "#88FF88" : "#FFFFFF";
                _chatLog.AppendText($"[color=#888888]{timestamp}[/color] [color={color}]{msg.SenderDisplayName}[/color]: {msg.Content}\n");
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[PartyChatPanel] Failed to refresh chat log: {ex.Message}", ConsoleChannel.UI);
        }
    }
    
    // Event handlers
    public void OnGameEvent(PartyCreatedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
    
    public void OnGameEvent(PartyJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
    
    public void OnGameEvent(PartyLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
    
    public void OnGameEvent(PartyDisbandedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
    
    public void OnGameEvent(PartyChatMessageEvent eventArgs)
    {
        if (eventArgs.PartyId == _currentPartyId)
        {
            CallDeferred(nameof(RefreshChatLog));
        }
    }
    
    public void OnGameEvent(PartyMemberJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
    
    public void OnGameEvent(PartyMemberLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdateVisibility));
    }
}

