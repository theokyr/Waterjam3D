using Godot;
using Waterjam.UI.Components;

public partial class StaminaWheel : Control
{
	[Export] public float Radius { get; set; } = 42f;
	[Export] public float Thickness { get; set; } = 8f;
	[Export] public float SegmentGapDegrees { get; set; } = 6f;
	[Export] public Color EmptyColor { get; set; } = new Color(0.1f, 0.1f, 0.1f, 0.85f);
	[Export] public Color FillColor { get; set; } = new Color(0.95f, 0.95f, 0.95f, 1f);
	[Export] public Color PartialColor { get; set; } = new Color(1f, 0.85f, 0.2f, 1f);
	// Semi-circle settings: start at 180deg (left) and cover 180deg (bottom arc)
	[Export] public float ArcStartDegrees { get; set; } = 0f;
	[Export] public float ArcSpanDegrees { get; set; } = 180f;

	private IStaminaSource _source;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public void SetSource(object stamina)
	{
		// Accept either StaminaComponent or custom sources implementing IStaminaSource
		if (stamina is IStaminaSource s)
		{
			_source = s;
		}
		else if (stamina is StaminaComponent c)
		{
			_source = new StaminaAdapter(c);
		}
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_source != null)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		var center = new Vector2(Size.X * 0.5f, Size.Y * 0.5f);
		// Compute arc parameters
		float totalSpan = Mathf.DegToRad(Mathf.Clamp(ArcSpanDegrees, 1f, 360f));
		float start = Mathf.DegToRad(ArcStartDegrees);
		float gapRad = Mathf.DegToRad(SegmentGapDegrees);

		if (_source == null)
		{
			// Draw placeholder segmented empty arc
			int placeholderSegments = 3;
			float arcSpan = (totalSpan / placeholderSegments) - gapRad;
			for (int i = 0; i < placeholderSegments; i++)
			{
				float segStart = start + i * (arcSpan + gapRad);
				DrawRing(center, Radius, Thickness, segStart, segStart + arcSpan, EmptyColor);
			}
			return;
		}

		int max = Mathf.Max(1, _source.GetMaxCharges());
		int cur = Mathf.Clamp(_source.GetCurrentCharges(), 0, max);
		float progress = _source.GetNextChargeProgress01();

		// Debug logging
		// GD.Print($"[StaminaWheel] max={max} cur={cur} progress={progress:F2}");

		float perSegSpan = (totalSpan / max) - gapRad;
		if (perSegSpan <= 0f) return;

		// Empty segments
		for (int i = 0; i < max; i++)
		{
			float segStart = start + i * (perSegSpan + gapRad);
			DrawRing(center, Radius, Thickness, segStart, segStart + perSegSpan, EmptyColor);
		}

		// Filled whole segments
		for (int i = 0; i < cur; i++)
		{
			float segStart = start + i * (perSegSpan + gapRad);
			DrawRing(center, Radius, Thickness, segStart, segStart + perSegSpan, FillColor);
		}

		// Partial on the next segment (HP bar style)
		if (cur < max)
		{
			float segStart = start + cur * (perSegSpan + gapRad);
			float segEnd = segStart + perSegSpan * Mathf.Clamp(progress, 0f, 1f);
			DrawRing(center, Radius, Thickness, segStart, segEnd, PartialColor);
		}
	}

	private void DrawRing(Vector2 center, float radius, float thickness, float fromAngle, float toAngle, Color color)
	{
		// Draw as a set of thin arcs (approximated with many small lines)
		int steps = 64;
		float span = Mathf.Max(0f, toAngle - fromAngle);
		if (span <= 0f) return;
		int count = Mathf.Max(3, Mathf.CeilToInt(steps * span / Mathf.Tau));
		var inner = radius - thickness * 0.5f;
		var outer = radius + thickness * 0.5f;

		for (int i = 0; i < count; i++)
		{
			float a0 = fromAngle + span * (i / (float)count);
			float a1 = fromAngle + span * ((i + 1) / (float)count);
			var p0i = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * inner;
			var p0o = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outer;
			var p1i = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * inner;
			var p1o = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outer;
			DrawPolygon(new Vector2[] { p0i, p0o, p1o, p1i }, new Color[] { color, color, color, color });
		}
	}

	private sealed class StaminaAdapter : IStaminaSource
	{
		private readonly StaminaComponent _c;
		public StaminaAdapter(StaminaComponent c) { _c = c; }
		public int GetMaxCharges() => _c.GetMaxCharges();
		public int GetCurrentCharges() => _c.GetCurrentCharges();
		public float GetNextChargeProgress01() => _c.GetNextChargeProgress01();
		public bool TryUseCharge(int amount = 1) => _c.TryUseCharge(amount);
	}
}


