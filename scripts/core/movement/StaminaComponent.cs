using Godot;
using System;

public partial class StaminaComponent : Node
{
	[Export] public int MaxCharges { get; set; } = 2;
	[Export] public float RechargeTimeSeconds { get; set; } = 3.0f;
	[Export] public bool AutoRecharge { get; set; } = true;
	[Export] public bool SprintRequiresCharge { get; set; } = false;

	// Compatibility events
	public event Action<float, float> StaminaChanged; // args: current, max
	public event Action StaminaDepleted;

	private int _currentCharges;
	private float _rechargeTimer;

	public override void _Ready()
	{
		_currentCharges = MaxCharges;
		_rechargeTimer = 0f;
		StaminaChanged?.Invoke(GetCurrentCharges(), GetMaxCharges());
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!AutoRecharge) return;
		if (_currentCharges >= MaxCharges)
		{
			_rechargeTimer = 0f;
			return;
		}
		var beforeProgress = GetNextChargeProgress01();
		_rechargeTimer += (float)delta;
		if (_rechargeTimer >= RechargeTimeSeconds)
		{
			_rechargeTimer -= RechargeTimeSeconds;
			_currentCharges = Mathf.Clamp(_currentCharges + 1, 0, MaxCharges);
			StaminaChanged?.Invoke(GetCurrentCharges(), GetMaxCharges());
		}
		else
		{
			var afterProgress = GetNextChargeProgress01();
			if (!Mathf.IsEqualApprox(beforeProgress, afterProgress))
			{
				StaminaChanged?.Invoke(GetCurrentCharges() + afterProgress - 1f + 1f, GetMaxCharges());
			}
		}
	}

	public bool TryUseCharge(int amount = 1)
	{
		if (amount <= 0) return true;
		if (_currentCharges >= amount)
		{
			_currentCharges -= amount;
			_rechargeTimer = 0f;
			StaminaChanged?.Invoke(GetCurrentCharges(), GetMaxCharges());
			if (_currentCharges == 0)
			{
				StaminaDepleted?.Invoke();
			}
			return true;
		}
		return false;
	}

	public int GetCurrentCharges()
	{
		return _currentCharges;
	}

	public int GetMaxCharges()
	{
		return MaxCharges;
	}

	public float GetNextChargeProgress01()
	{
		if (_currentCharges >= MaxCharges) return 1f;
		return Mathf.Clamp(_rechargeTimer / RechargeTimeSeconds, 0f, 1f);
	}

	// Compatibility helpers
	public bool CanSprint()
	{
		return !SprintRequiresCharge || _currentCharges > 0;
	}

	public bool TryConsumeSprint(float seconds)
	{
		return true;
	}

	public float GetStaminaPercentage()
	{
		// Represent charges as a normalized 0..1 value
		var denom = Mathf.Max(1, MaxCharges);
		var value = GetCurrentCharges() + (GetCurrentCharges() >= MaxCharges ? 0f : GetNextChargeProgress01());
		return Mathf.Clamp(value / denom, 0f, 1f);
	}
}
