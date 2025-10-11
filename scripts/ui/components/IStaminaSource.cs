namespace Waterjam.UI.Components
{
	public interface IStaminaSource
	{
		int GetMaxCharges();
		int GetCurrentCharges();
		float GetNextChargeProgress01();
		bool TryUseCharge(int amount = 1);
	}
}

