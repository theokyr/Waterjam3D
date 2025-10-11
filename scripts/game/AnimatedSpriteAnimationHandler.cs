using Godot;
using Waterjam.Events;

namespace Waterjam.Game;

public partial class AnimatedSpriteAnimationHandler : Node, IGameEventHandler<AnimationChangedEvent>
{
    [Export]
    public string ActorId { get; set; } = "player";

    [Export]
    public NodePath AnimatedSpritePath { get; set; }
    
    [Export]
    public string DefaultAnimation { get; set; } = "idle";

    private AnimatedSprite3D _sprite;

    public override void _Ready()
    {
        _sprite = GetNodeOrNull<AnimatedSprite3D>(AnimatedSpritePath);
        if (_sprite == null)
        {
            _sprite = GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
        }
        if (_sprite != null)
        {
            _sprite.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
            if (!string.IsNullOrEmpty(DefaultAnimation) && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(DefaultAnimation))
            {
                _sprite.Animation = DefaultAnimation;
                _sprite.Play();
            }
        }
    }

    public void OnGameEvent(AnimationChangedEvent eventArgs)
    {
        if (_sprite == null) return;
        if (eventArgs.ActorId != ActorId) return;

        if (!string.IsNullOrEmpty(eventArgs.Animation) && (_sprite.SpriteFrames?.HasAnimation(eventArgs.Animation) ?? false))
        {
            if (_sprite.Animation != eventArgs.Animation)
            {
                _sprite.Animation = eventArgs.Animation;
                _sprite.Play();
            }
        }

        if (eventArgs.Speed.HasValue)
        {
            _sprite.SpeedScale = Mathf.Max(0.01f, eventArgs.Speed.Value);
        }

        if (eventArgs.FlipX.HasValue)
        {
            var scale = _sprite.Scale;
            scale[0] = Mathf.Abs(scale[0]) * (eventArgs.FlipX.Value ? -1f : 1f);
            _sprite.Scale = scale;
        }
    }
}


