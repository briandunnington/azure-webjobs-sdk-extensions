﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.CosmosDB.Trigger
{
    internal class CosmosDBTargetScaler : ITargetScaler
    {
        public const int DefaultMaxItemsPerInvocation = 100;
        private readonly string _functionId;
        private readonly TargetScalerDescriptor _targetScalerDescriptor;
        private readonly CosmosDBMetricsProvider _cosmosDBMetricsProvider;
        private readonly ILogger _logger;
        private readonly CosmosDBTriggerAttribute _cosmosDBTriggerAttribute;
        private readonly Container _monitoredContainer;

        public CosmosDBTargetScaler(string functionId, CosmosDBTriggerAttribute cosmosDBTriggerAttribute, Container monitoredContainer, Container leaseContainer, string processorName, ILogger logger)
        {
            _functionId = functionId;
            _targetScalerDescriptor = new TargetScalerDescriptor(functionId);
            _monitoredContainer = monitoredContainer;
            _cosmosDBMetricsProvider = new CosmosDBMetricsProvider(logger, _monitoredContainer, leaseContainer, processorName);
            _logger = logger;
            _cosmosDBTriggerAttribute = cosmosDBTriggerAttribute;
        }

        public TargetScalerDescriptor TargetScalerDescriptor => _targetScalerDescriptor;

        public async Task<TargetScalerResult> GetScaleResultAsync(TargetScalerContext context)
        {
            CosmosDBTriggerMetrics metrics = await _cosmosDBMetricsProvider.GetMetricsAsync();

            return GetScaleResultInternal(context, metrics.RemainingWork, metrics.PartitionCount);
        }

        internal TargetScalerResult GetScaleResultInternal(TargetScalerContext context, long remainingWork, int partitionCount)
        {
            int concurrency;

            if (!context.InstanceConcurrency.HasValue)
            {
                concurrency = _cosmosDBTriggerAttribute.MaxItemsPerInvocation > 0 ? _cosmosDBTriggerAttribute.MaxItemsPerInvocation : DefaultMaxItemsPerInvocation;
            }
            else
            {
                concurrency = context.InstanceConcurrency.Value;
            }

            if (concurrency <= 0)
            {
                _logger.LogWarning($"Concurrency value for target based scale must be greater than 0. Using default value of {DefaultMaxItemsPerInvocation} as concurrency value.");
                concurrency = DefaultMaxItemsPerInvocation;
            }

            int targetWorkerCount = (int)Math.Ceiling(remainingWork / (decimal)concurrency);

            string targetScaleMessage = $"Target worker count for function '{_functionId}' is '{targetWorkerCount}' (MonitoredContainerId='{_monitoredContainer.Id}', MonitoredContainerDatabaseId='{_monitoredContainer.Database.Id}', RemainingWork ='{remainingWork}', Concurrency='{concurrency}').";

            if (partitionCount > 0 && targetWorkerCount > partitionCount)
            {
                targetScaleMessage += $" However, partition count is {partitionCount}. Adding more workers than partitions would not be helpful, so capping target worker count at {partitionCount}";
                targetWorkerCount = partitionCount;
            }

            _logger.LogInformation(targetScaleMessage);

            return new TargetScalerResult
            {
                TargetWorkerCount = targetWorkerCount
            };
        }
    }
}
