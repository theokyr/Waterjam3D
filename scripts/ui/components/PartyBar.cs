using System.Linq;
using Godot;
using Waterjam.Events;
using GodotSteam;
using Waterjam.Core.Services;

namespace Waterjam.UI.Components;

public partial class PartyBar : Control,
	IGameEventHandler<PartyCreatedEvent>,
	IGameEventHandler<PartyJoinedEvent>,
	IGameEventHandler<PartyLeftEvent>,
	IGameEventHandler<PartyMemberJoinedEvent>,
	IGameEventHandler<PartyMemberLeftEvent>,
	IGameEventHandler<PartyLeaderChangedEvent>,
	IGameEventHandler<PartyDisbandedEvent>
{
	private HBoxContainer _avatarRow;
	private Button _inviteButton;

	public override void _Ready()
	{
		// Layout: [avatars ...] [+]
		Name = "PartyBar";
		ZIndex = 100;
		AnchorRight = 1.0f;
		AnchorTop = 0f;
		AnchorBottom = 0f;
		OffsetRight = 0f;
		OffsetTop = 8f;
		OffsetBottom = 56f;
		GrowHorizontal = GrowDirection.Both;
		MouseFilter = MouseFilterEnum.Stop;

		var root = new HBoxContainer();
		root.Name = "Root";
		root.Alignment = BoxContainer.AlignmentMode.End;
		root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		root.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		AddChild(root);

		_avatarRow = new HBoxContainer();
		_avatarRow.Name = "Avatars";
		_avatarRow.Alignment = BoxContainer.AlignmentMode.End;
		_avatarRow.AddThemeConstantOverride("separation", 6);
		root.AddChild(_avatarRow);

		_inviteButton = new Button();
		_inviteButton.Name = "InviteButton";
		_inviteButton.Text = "+";
		_inviteButton.TooltipText = "Invite to Party";
		_inviteButton.FocusMode = FocusModeEnum.None;
		_inviteButton.CustomMinimumSize = new Vector2(32, 32);
		_inviteButton.Pressed += OnInvitePressed;
		root.AddChild(_inviteButton);

		Refresh();
	}

    private void OnInvitePressed()
    {
        // Navigate to Party screen where full invite UI is available
        GameEvent.DispatchGlobal(new UiShowPartyScreenEvent());
    }

	private void Refresh()
	{
		_avatarRow?.QueueFreeChildren();

        var partyService = GetNodeOrNull("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
		if (partyService == null)
		{
			_inviteButton.Disabled = true;
			return;
		}

		_inviteButton.Disabled = false;
        var party = partyService.GetCurrentPlayerParty();
        var localId = partyService.GetLocalPlayerId();

        // Always show current player first (even if not in a formal party yet)
        if (!string.IsNullOrEmpty(localId))
        {
            var displaySelf = GetLocalDisplayName();
            _avatarRow.AddChild(CreateAvatar(localId, isLeader: party?.LeaderPlayerId == localId, display: displaySelf));
        }

        if (party != null)
		{
			foreach (var member in party.Members.Where(m => m.PlayerId != localId))
			{
				_avatarRow.AddChild(CreateAvatar(member.PlayerId, member.IsLeader, member.DisplayName));
			}
		}
        else
        {
            // Not in a party: show hint avatar slot to indicate invite ability
            var hint = new Label { Text = "Invite+", ThemeTypeVariation = "CaptionLabel" };
            _avatarRow.AddChild(hint);
        }
	}

    private Control CreateAvatar(string playerId, bool isLeader, string display)
	{
        var vb = new VBoxContainer();
		vb.CustomMinimumSize = new Vector2(48, 48);
		vb.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		vb.Alignment = BoxContainer.AlignmentMode.Center;

        var avatar = BuildAvatarImage(playerId);
        vb.AddChild(avatar);

		var label = new Label();
		label.Text = display;
		label.ThemeTypeVariation = "CaptionLabel";
		label.HorizontalAlignment = HorizontalAlignment.Center;
		vb.AddChild(label);

		vb.TooltipText = isLeader ? $"{display} (Leader)" : display;
		return vb;
	}

    private Control BuildAvatarImage(string playerId)
    {
        // If Steam is initialized, try to fetch Steam avatar; otherwise show fallback rect
        if (PlatformService.IsSteamInitialized)
        {
            try
            {
                // Try to get a small avatar synchronously (Steam returns an index). If 0 or failure, fallback.
                var steamId = ulong.TryParse(playerId, out var id) ? id : Steam.GetSteamID();
                var avatarIndex = Steam.GetSmallFriendAvatar(steamId);

                var texRect = new TextureRect();
                texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                texRect.CustomMinimumSize = new Vector2(36, 36);

                // Listen for async image load; request player avatar to trigger signal
                // One-shot handlers to avoid accumulation
                Steam.AvatarImageLoadedEventHandler onImageLoaded = null;
                onImageLoaded = (avatarId, index, w, h) =>
                {
                    if (avatarId != steamId) return;
                    var img = Steam.GetImageRGBA((int)index);
                    if (img != null && img.Success && img.Buffer != null)
                    {
                        var image = Image.CreateFromData((int)w, (int)h, false, Image.Format.Rgba8, img.Buffer);
                        var tex = ImageTexture.CreateFromImage(image);
                        texRect.Texture = tex;
                        try { Steam.AvatarImageLoaded -= onImageLoaded; } catch { }
                    }
                };
                Steam.AvatarImageLoaded += onImageLoaded;

                Steam.AvatarLoadedEventHandler onAvatarLoaded = null;
                onAvatarLoaded = (avatarId, width, data) =>
                {
                    if (avatarId != steamId || data == null || data.Length == 0) return;
                    var image = Image.CreateFromData(width, width, false, Image.Format.Rgba8, data);
                    var tex = ImageTexture.CreateFromImage(image);
                    texRect.Texture = tex;
                    try { Steam.AvatarLoaded -= onAvatarLoaded; } catch { }
                };
                Steam.AvatarLoaded += onAvatarLoaded;

                // Trigger load
                Steam.GetPlayerAvatar(AvatarSize.Small, steamId);

                return texRect;
            }
            catch
            {
                // fall back to rect below
            }
        }

        var rect = new ColorRect();
        rect.Color = new Color(0.2f, 0.24f, 0.28f);
        rect.CustomMinimumSize = new Vector2(36, 36);
        return rect;
    }

    private string GetLocalDisplayName()
    {
        if (PlatformService.IsSteamInitialized)
        {
            try
            {
                var name = Steam.GetPersonaName();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }
        }
        return "You";
    }

    public void OnGameEvent(PartyCreatedEvent e) { CallDeferred(nameof(Refresh)); Waterjam.Events.GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent()); }
    public void OnGameEvent(PartyJoinedEvent e) { CallDeferred(nameof(Refresh)); Waterjam.Events.GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent()); }
	public void OnGameEvent(PartyLeftEvent e) => CallDeferred(nameof(Refresh));
	public void OnGameEvent(PartyMemberJoinedEvent e) => CallDeferred(nameof(Refresh));
	public void OnGameEvent(PartyMemberLeftEvent e) => CallDeferred(nameof(Refresh));
	public void OnGameEvent(PartyLeaderChangedEvent e) => CallDeferred(nameof(Refresh));
	public void OnGameEvent(PartyDisbandedEvent e) => CallDeferred(nameof(Refresh));
}

public static class NodeExtensions
{
	public static void QueueFreeChildren(this Node node)
	{
		foreach (var child in node.GetChildren())
		{
			(child as Node)?.QueueFree();
		}
	}
}
