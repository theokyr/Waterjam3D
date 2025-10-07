using Godot;
using System;
using System.Linq;
using Waterjam.Domain.Party;
using Waterjam.Events;

namespace Waterjam.UI;

/// <summary>
/// UI for managing parties and invitations.
/// </summary>
public partial class PartyUI : Control,
    IGameEventHandler<PartyCreatedEvent>,
    IGameEventHandler<PartyJoinedEvent>,
    IGameEventHandler<PartyLeftEvent>,
    IGameEventHandler<PartyMemberJoinedEvent>,
    IGameEventHandler<PartyMemberLeftEvent>,
    IGameEventHandler<PartyLeaderChangedEvent>,
    IGameEventHandler<PartyDisbandedEvent>,
    IGameEventHandler<PartyInviteReceivedEvent>,
    IGameEventHandler<PartyInviteAcceptedEvent>,
    IGameEventHandler<PartyInviteDeclinedEvent>
{
    private Label _partyStatusLabel;
    private Button _createPartyButton;
    private Button _joinPartyButton;
    private Button _leavePartyButton;
    private Label _partyCodeLabel;
    private Label _membersLabel;
    private ItemList _membersList;
    private ItemList _invitesList;
    private Button _inviteButton;

    public override void _Ready()
    {
        base._Ready();

        // Get UI elements
        _partyStatusLabel = GetNode<Label>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/PartyStatus");
        _createPartyButton = GetNode<Button>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/CreatePartyButton");
        _joinPartyButton = GetNode<Button>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/JoinPartyButton");
        _leavePartyButton = GetNode<Button>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/LeavePartyButton");
        _partyCodeLabel = GetNode<Label>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/PartyCodeLabel");
        _membersLabel = GetNode<Label>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/MembersLabel");
        _membersList = GetNode<ItemList>("Panel/VBoxContainer/TabContainer/PartyTab/PartyInfo/MembersList");
        _invitesList = GetNode<ItemList>("Panel/VBoxContainer/TabContainer/InvitesTab/InvitesList");
        _inviteButton = GetNode<Button>("Panel/VBoxContainer/TabContainer/InvitesTab/InviteButton");

        UpdatePartyDisplay();
        UpdateInvitesDisplay();
    }

    private void UpdatePartyDisplay()
    {
        var partyService = GetNode("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
        if (partyService == null)
        {
            GD.PushWarning("PartyService not found");
            return;
        }

        var currentParty = partyService.GetCurrentPlayerParty();

        if (currentParty == null)
        {
            _partyStatusLabel.Text = "Not in a party";
            _createPartyButton.Visible = true;
            _joinPartyButton.Visible = true;
            _leavePartyButton.Visible = false;
            _partyCodeLabel.Visible = false;
            _membersLabel.Visible = false;
            _membersList.Visible = false;
            _inviteButton.Disabled = true;
        }
        else
        {
            _partyStatusLabel.Text = $"In party: {currentParty.DisplayName}";
            _createPartyButton.Visible = false;
            _joinPartyButton.Visible = false;
            _leavePartyButton.Visible = true;
            _partyCodeLabel.Visible = true;
            _partyCodeLabel.Text = $"Party Code: {currentParty.PartyCode}";
            _membersLabel.Visible = true;
            _membersList.Visible = true;
            _inviteButton.Disabled = !currentParty.LeaderPlayerId.Equals(partyService.GetLocalPlayerId());

            // Update members list
            _membersList.Clear();
            foreach (var member in currentParty.Members)
            {
                var leaderIndicator = member.IsLeader ? " (Leader)" : "";
                _membersList.AddItem($"{member.DisplayName}{leaderIndicator}");
            }
        }
    }

    private void UpdateInvitesDisplay()
    {
        var partyService = GetNode("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
        if (partyService == null)
        {
            GD.PushWarning("PartyService not found");
            return;
        }

        var invites = partyService.GetPlayerInvites(partyService.GetLocalPlayerId());

        _invitesList.Clear();
        foreach (var invite in invites)
        {
            _invitesList.AddItem($"From {invite.FromPlayerDisplayName}: {invite.PartyDisplayName}");
        }
    }

    private void _on_create_party_pressed()
    {
        var dialog = new AcceptDialog();
        dialog.Title = "Create Party";
        dialog.DialogText = "Enter party name:";
        dialog.AddCancelButton("Cancel");

        var lineEdit = new LineEdit();
        lineEdit.PlaceholderText = "My Party";
        dialog.AddChild(lineEdit);

        dialog.Confirmed += () =>
        {
            var partyName = lineEdit.Text;
            if (string.IsNullOrWhiteSpace(partyName))
                partyName = "My Party";

            GameEvent.DispatchGlobal(new CreatePartyRequestEvent(partyName));
        };

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void _on_join_party_pressed()
    {
        var dialog = new AcceptDialog();
        dialog.Title = "Join Party";
        dialog.DialogText = "Enter party code:";
        dialog.AddCancelButton("Cancel");

        var lineEdit = new LineEdit();
        lineEdit.PlaceholderText = "ABC123";
        lineEdit.MaxLength = 6;
        dialog.AddChild(lineEdit);

        dialog.Confirmed += () =>
        {
            var partyCode = lineEdit.Text.ToUpper();
            if (string.IsNullOrWhiteSpace(partyCode))
                return;

            GameEvent.DispatchGlobal(new JoinPartyRequestEvent(partyCode));
        };

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void _on_leave_party_pressed()
    {
        GameEvent.DispatchGlobal(new LeavePartyRequestEvent());
    }

    private void _on_invite_player_pressed()
    {
        var dialog = new AcceptDialog();
        dialog.Title = "Invite Player";
        dialog.DialogText = "Enter player ID to invite:";
        dialog.AddCancelButton("Cancel");

        var lineEdit = new LineEdit();
        lineEdit.PlaceholderText = "player123";
        dialog.AddChild(lineEdit);

        var messageEdit = new TextEdit();
        messageEdit.Size = new Vector2(300, 100);
        messageEdit.PlaceholderText = "Optional message...";
        dialog.AddChild(messageEdit);

        dialog.Confirmed += () =>
        {
            var playerId = lineEdit.Text;
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            var message = messageEdit.Text;
            GameEvent.DispatchGlobal(new InviteToPartyRequestEvent(playerId, message));
        };

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void _on_close_pressed()
    {
        QueueFree();
    }

    // Event handlers
    public void OnGameEvent(PartyCreatedEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
        GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent());
    }

    public void OnGameEvent(PartyJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
        GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent());
    }

    public void OnGameEvent(PartyLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
    }

    public void OnGameEvent(PartyMemberJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
    }

    public void OnGameEvent(PartyMemberLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
    }

    public void OnGameEvent(PartyLeaderChangedEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
    }

    public void OnGameEvent(PartyDisbandedEvent eventArgs)
    {
        CallDeferred(nameof(UpdatePartyDisplay));
    }

    public void OnGameEvent(PartyInviteReceivedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateInvitesDisplay));

        // Show notification
        var acceptDialog = new AcceptDialog();
        acceptDialog.Title = "Party Invitation";
        acceptDialog.DialogText = $"You have been invited to join {eventArgs.Invite.FromPlayerDisplayName}'s party: {eventArgs.Invite.PartyDisplayName}";
        acceptDialog.AddButton("Accept");
        acceptDialog.AddCancelButton("Decline");

        acceptDialog.CustomAction += (action) =>
        {
            if (action == "Accept")
            {
                GameEvent.DispatchGlobal(new RespondToPartyInviteRequestEvent(eventArgs.Invite.InviteId, true));
            }
            else
            {
                GameEvent.DispatchGlobal(new RespondToPartyInviteRequestEvent(eventArgs.Invite.InviteId, false));
            }
        };

        AddChild(acceptDialog);
        acceptDialog.PopupCentered();
    }

    public void OnGameEvent(PartyInviteAcceptedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateInvitesDisplay));
        GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent());
    }

    public void OnGameEvent(PartyInviteDeclinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateInvitesDisplay));
    }

    // Navigation is handled by UiService consumers of UiShow* events
}
