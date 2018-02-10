﻿using System.Threading.Tasks;

namespace MarginTrading.Backend.Core
{
	public interface IVolumeEquivalentService
	{
		void EnrichOpeningOrder(Order order);
		void EnrichClosingOrder(Order order);
	}
}