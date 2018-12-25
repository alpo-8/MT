﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MarginTrading.Backend.Contracts.Common;
using MarginTrading.Backend.Contracts.Orders;
using Refit;

namespace MarginTrading.Backend.Contracts
{
    /// <summary>
    /// API for performing operations with orders and reading current state
    /// </summary>
    [PublicAPI]
    public interface IOrdersApi
    {
        /// <summary>
        /// Place new order
        /// </summary>
        /// <param name="request">Order model</param>
        /// <returns>Order Id</returns>
        [Post("/api/orders")]
        Task<string> PlaceAsync([Body] [NotNull] OrderPlaceRequest request);
        
        /// <summary>
        /// Change existing order
        /// </summary>
        [Put("/api/orders/{orderId}")]
        Task ChangeAsync([NotNull] string orderId, [Body][NotNull] OrderChangeRequest request);

        /// <summary>
        /// Close existing order 
        /// </summary>
        [Delete("/api/orders/{orderId}")]
        Task CancelAsync([NotNull] string orderId, [Body] OrderCancelRequest request = null);

        /// <summary>
        /// Close group of orders by accountId, assetPairId and direction.
        /// </summary>
        /// <param name="accountId">Mandatory</param>
        /// <param name="assetPairId">Optional</param>
        /// <param name="direction">Optional</param>
        /// <param name="request">Optional</param>
        /// <returns>Dictionary of failed to close orderIds with exception message</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        [Delete("/api/orders/close-group")]
        Task<Dictionary<string, string>> CloseGroupAsync([Query] [NotNull] string accountId,
            [Query] [CanBeNull] string assetPairId = null,
            [Query] [CanBeNull] OrderDirectionContract? direction = null,
            [Body] [CanBeNull] OrderCancelRequest request = null);

        /// <summary>
        /// Get order by id 
        /// </summary>
        [Get("/api/orders/{orderId}"), ItemCanBeNull]
        Task<OrderContract> GetAsync([NotNull] string orderId);

        /// <summary>
        /// Get open orders with optional filtering
        /// </summary>
        [Get("/api/orders")]
        Task<List<OrderContract>> ListAsync([Query, CanBeNull] string accountId = null,
            [Query, CanBeNull] string assetPairId = null, [Query, CanBeNull] string parentPositionId = null,
            [Query, CanBeNull] string parentOrderId = null);

        /// <summary>
        /// Get open orders with optional filtering and pagination
        /// </summary>
        [Get("/api/orders/by-pages")]
        Task<PaginatedResponseContract<OrderContract>> ListAsyncByPages([Query] [CanBeNull] string accountId = null,
            [Query] [CanBeNull] string assetPairId = null, [Query] [CanBeNull] string parentPositionId = null,
            [Query] [CanBeNull] string parentOrderId = null,
            [Query] [CanBeNull] int? skip = null, [Query] [CanBeNull] int? take = null);
    }
}