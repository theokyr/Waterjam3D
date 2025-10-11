// scripts/tools/SpriteFramesBuilder.cs
// [Tool] editor script: select the node, run from Editor/Tools > BuildSpriteFrames
#if TOOLS
using Godot;
using System.Linq;

[Tool]
public partial class SpriteFramesBuilder : EditorScript
{
    public override void _Run()
    {
        var root = "res://resources/sprites/jamoulis";
        var map = new System.Collections.Generic.Dictionary<string, string>
        {
            { "Idle", "idle" }, { "Run", "run" }, { "Dash", "dash" }, { "Slide", "slide" },
            { "Jump", "jump" } // ascend/in_air/descent will be sliced from Jump below if desired
        };

        var frames = new SpriteFrames();

        foreach (var kv in map)
        {
            var dir = $"{root}/{kv.Key}";
            if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(dir))) continue;

            var da = DirAccess.Open(dir);
            var files = da.GetFiles().Where(f => f.EndsWith(".png")).OrderBy(f => f).ToArray();

            if (!frames.HasAnimation(kv.Value))
                frames.AddAnimation(kv.Value);

            foreach (var file in files)
            {
                var tex = ResourceLoader.Load<Texture2D>($"{dir}/{file}");
                if (tex != null)
                    frames.AddFrame(kv.Value, tex);
            }

            if (frames.GetFrameCount(kv.Value) > 0)
            {
                frames.SetAnimationLoop(kv.Value, kv.Value is "idle" or "run" or "dash" or "slide");
                frames.SetAnimationSpeed(kv.Value, kv.Value == "run" || kv.Value == "dash" ? 12 : 8);
            }
            else
            {
                // Remove empty animations to avoid "Animation '<name>' doesn't exist" warnings
                if (frames.HasAnimation(kv.Value))
                    frames.RemoveAnimation(kv.Value);
            }
        }

        // Optional: split Jump frames into ascend/in_air/descent thirds
        if (frames.HasAnimation("jump") && frames.GetFrameCount("jump") >= 6)
        {
            var total = frames.GetFrameCount("jump");
            int aEnd = total / 3, dStart = total * 2 / 3;

            if (!frames.HasAnimation("ascend")) frames.AddAnimation("ascend");
            for (int i = 0; i < aEnd; i++) frames.AddFrame("ascend", frames.GetFrameTexture("jump", i));
            frames.SetAnimationLoop("ascend", true);

            if (!frames.HasAnimation("in_air")) frames.AddAnimation("in_air");
            for (int i = aEnd; i < dStart; i++) frames.AddFrame("in_air", frames.GetFrameTexture("jump", i));
            frames.SetAnimationLoop("in_air", true);

            if (!frames.HasAnimation("descent")) frames.AddAnimation("descent");
            for (int i = dStart; i < total; i++) frames.AddFrame("descent", frames.GetFrameTexture("jump", i));
            frames.SetAnimationLoop("descent", true);

            frames.SetAnimationLoop("jump", false); // keep jump as 1-shot intro
        }

        ResourceSaver.Save(frames, "res://resources/sprites/player/Player.tres");
        GD.Print("SpriteFrames saved to res://resources/sprites/player/Player.tres");
    }
}
#endif