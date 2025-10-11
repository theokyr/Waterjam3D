using Godot;
using System;
using Waterjam.Core.Systems.Console;

public partial class StaminaComponent : Node
{
	[Export] public MovementConfig Config { get; set; }
	[Export] public bool AutoRecharge { get; set; } = true;
	[Export] public bool SprintRequiresCharge { get; set; } = false;

	// Compatibility events
	public event Action<float, float> StaminaChanged; // args: current, max
	public event Action StaminaDepleted;

	private int _currentCharges;
	private float _rechargeTimer;

	public override void _Ready()
	{
		if (!EnsureConfig())
		{
			ConsoleSystem.LogWarn($"[StaminaComponent] Config not set; attempting lazy lookup failed", ConsoleChannel.Player);
		}
		else
		{
			ConsoleSystem.Log($"[StaminaComponent] Config bound (MaxCharges={Config.MaxCharges})", ConsoleChannel.Player);
		}
		// Initialize charges once when ready if config is available
		if (Config != null)
		{
			_currentCharges = Config.MaxCharges;
			_rechargeTimer = 0f;
			StaminaChanged?.Invoke(GetCurrentCharges(), GetMaxCharges());
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!EnsureConfig()) return;
		var useAutoRecharge = Config != null ? Config.AutoRecharge : AutoRecharge;
		if (!useAutoRecharge) return;

		if (_currentCharges >= Config.MaxCharges)
		{
			_rechargeTimer = 0f;
			return;
		}
		_rechargeTimer += (float)delta;
		if (_rechargeTimer >= Config.RechargeTimeSeconds)
		{
			_rechargeTimer -= Config.RechargeTimeSeconds;
			_currentCharges = Mathf.Clamp(_currentCharges + 1, 0, Config.MaxCharges);
			StaminaChanged?.Invoke(GetCurrentCharges(), GetMaxCharges());
		}
	}

	public bool TryUseCharge(int amount = 1)
	{
		if (amount <= 0) return true;
		if (!EnsureConfig()) return false;
		if (_currentCharges >= amount)
		{
			_currentCharges -= amount;
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
		return Config != null ? Config.MaxCharges : 0;
	}

	public float GetNextChargeProgress01()
	{
		if (Config == null) return 0f;
		if (_currentCharges >= Config.MaxCharges) return 1f;
		return Mathf.Clamp(_rechargeTimer / Config.RechargeTimeSeconds, 0f, 1f);
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
		if (Config == null) return 0f;
		// Represent charges as a normalized 0..1 value
		var denom = Mathf.Max(1, Config.MaxCharges);
		var value = GetCurrentCharges() + (GetCurrentCharges() >= Config.MaxCharges ? 0f : GetNextChargeProgress01());
		return Mathf.Clamp(value / denom, 0f, 1f);
	}

	private bool EnsureConfig()
	{
		if (Config != null) return true;
		// Try to resolve from player controller
		var player = GetParent() as SimpleThirdPersonController
			?? GetParent()?.GetParent() as SimpleThirdPersonController;
		if (player != null && player.Config != null)
		{
			Config = player.Config;
			return true;
		}
		return false;
	}
}
