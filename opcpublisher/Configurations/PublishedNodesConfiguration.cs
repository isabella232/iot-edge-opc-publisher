﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpcPublisher.Configurations
{
    public class PublishedNodesConfiguration
    {
        /// <summary>
        /// Keeps the version of the node configuration that has lastly been persisted
        /// </summary>
        private uint _lastNodeConfigVersion;

        /// <summary>
        /// Number of configured OPC UA sessions.
        /// </summary>
        public int NumberOfOpcSessionsConfigured
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    result = OpcSessions.Count();
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Number of connected OPC UA session.
        /// </summary>
        public int NumberOfOpcSessionsConnected
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    result = OpcSessions.Count(s => s.State == OpcUaSessionWrapper.SessionState.Connected);
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Number of configured OPC UA subscriptions.
        /// </summary>
        public int NumberOfOpcSubscriptionsConfigured
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    foreach (var opcSession in OpcSessions)
                    {

                        result += opcSession.GetNumberOfOpcSubscriptions();
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }
        /// <summary>
        /// Number of connected OPC UA subscriptions.
        /// </summary>
        public int NumberOfOpcSubscriptionsConnected
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    var opcSessions = OpcSessions.Where(s => s.State == OpcUaSessionWrapper.SessionState.Connected);
                    foreach (var opcSession in opcSessions)
                    {
                        result += opcSession.GetNumberOfOpcSubscriptions();
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Number of OPC UA nodes configured to monitor.
        /// </summary>
        public int NumberOfOpcMonitoredItemsConfigured
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    foreach (var opcSession in OpcSessions)
                    {
                        result += opcSession.GetNumberOfOpcMonitoredItemsConfigured();
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Number of monitored OPC UA nodes.
        /// </summary>
        public int NumberOfOpcMonitoredItemsMonitored
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    var opcSessions = OpcSessions.Where(s => s.State == OpcUaSessionWrapper.SessionState.Connected);
                    foreach (var opcSession in opcSessions)
                    {
                        result += opcSession.GetNumberOfOpcMonitoredItemsMonitored();
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Number of OPC UA nodes requested to stop monitoring.
        /// </summary>
        public int NumberOfOpcMonitoredItemsToRemove
        {
            get
            {
                int result = 0;
                try
                {
                    OpcSessionsListSemaphore.Wait();
                    foreach (var opcSession in OpcSessions)
                    {
                        result += opcSession.GetNumberOfOpcMonitoredItemsToRemove();
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
                return result;
            }
        }

        /// <summary>
        /// Semaphore to protect the node configuration data structures.
        /// </summary>
        public SemaphoreSlim PublisherNodeConfigurationSemaphore { get; set; }

        /// <summary>
        /// Semaphore to protect the node configuration file.
        /// </summary>
        public SemaphoreSlim PublisherNodeConfigurationFileSemaphore { get; set; }

        /// <summary>
        /// Semaphore to protect the OPC UA sessions list.
        /// </summary>
        public SemaphoreSlim OpcSessionsListSemaphore { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only
        /// <summary>
        /// List of configured OPC UA sessions.
        /// </summary>
        public List<OpcUaSessionWrapper> OpcSessions { get; set; } = new List<OpcUaSessionWrapper>();
#pragma warning restore CA2227 // Collection properties should be read only

        /// <summary>
        /// Ctor to initialize resources for the telemetry configuration.
        /// </summary>
        public PublishedNodesConfiguration()
        {
            OpcSessionsListSemaphore = new SemaphoreSlim(1);
            PublisherNodeConfigurationSemaphore = new SemaphoreSlim(1);
            PublisherNodeConfigurationFileSemaphore = new SemaphoreSlim(1);
            OpcSessions.Clear();
            _nodePublishingConfiguration = new List<NodePublishingConfigurationModel>();
            _configurationFileEntries = new List<ConfigurationFileEntryLegacyModel>();
        }

        /// <summary>
        /// Initialize the node configuration.
        /// </summary>
        /// <returns></returns>
        public void Init()
        {
            // read the configuration from the configuration file
            if (!ReadConfigAsync().Result)
            {
                string errorMessage = $"Error while reading the node configuration file '{SettingsConfiguration.PublisherNodeConfigurationFilename}'";
                Program.Instance.Logger.Error(errorMessage);
                throw new Exception(errorMessage);
            }

            // create the configuration data structures
            if (!CreateOpcPublishingDataAsync().Result)
            {
                string errorMessage = $"Error while creating node configuration data structures.";
                Program.Instance.Logger.Error(errorMessage);
                throw new Exception(errorMessage);
            }
        }

        /// <summary>
        /// Read and parse the publisher node configuration file.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> ReadConfigAsync()
        {
            // get information on the nodes to publish and validate the json by deserializing it.
            try
            {
                await PublisherNodeConfigurationSemaphore.WaitAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_GW_PNFP")))
                {
                    Program.Instance.Logger.Information("Publishing node configuration file path read from environment.");
                    SettingsConfiguration.PublisherNodeConfigurationFilename = Environment.GetEnvironmentVariable("_GW_PNFP");
                }
                Program.Instance.Logger.Information($"The name of the configuration file for published nodes is: {SettingsConfiguration.PublisherNodeConfigurationFilename}");

                // if the file exists, read it, if not just continue 
                if (File.Exists(SettingsConfiguration.PublisherNodeConfigurationFilename))
                {
                    Program.Instance.Logger.Information($"Attemtping to load node configuration from: {SettingsConfiguration.PublisherNodeConfigurationFilename}");
                    try
                    {
                        await PublisherNodeConfigurationFileSemaphore.WaitAsync().ConfigureAwait(false);
                        var json = File.ReadAllText(SettingsConfiguration.PublisherNodeConfigurationFilename);
                        _configurationFileEntries = JsonConvert.DeserializeObject<List<ConfigurationFileEntryLegacyModel>>(json);
                    }
                    finally
                    {
                        PublisherNodeConfigurationFileSemaphore.Release();
                    }

                    if (_configurationFileEntries != null)
                    {
                        Program.Instance.Logger.Information($"Loaded {_configurationFileEntries.Count} config file entry/entries.");
                        foreach (var publisherConfigFileEntryLegacy in _configurationFileEntries)
                        {
                            if (publisherConfigFileEntryLegacy.NodeId == null)
                            {
                                // new node configuration syntax.
                                foreach (var opcNode in publisherConfigFileEntryLegacy.OpcNodes)
                                {
                                    if (opcNode.ExpandedNodeId != null)
                                    {
                                        ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.ExpandedNodeId);
                                        _nodePublishingConfiguration.Add(new NodePublishingConfigurationModel(expandedNodeId, opcNode.ExpandedNodeId,
                                            publisherConfigFileEntryLegacy.EndpointUrl.OriginalString, publisherConfigFileEntryLegacy.UseSecurity,
                                            opcNode.OpcPublishingInterval, opcNode.OpcSamplingInterval, opcNode.DisplayName,
                                            opcNode.HeartbeatInterval, opcNode.SkipFirst, publisherConfigFileEntryLegacy.OpcAuthenticationMode, publisherConfigFileEntryLegacy.EncryptedAuthCredential));
                                    }
                                    else
                                    {
                                        // check Id string to check which format we have
                                        if (opcNode.Id.StartsWith("nsu=", StringComparison.InvariantCulture))
                                        {
                                            // ExpandedNodeId format
                                            ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.Id);
                                            _nodePublishingConfiguration.Add(new NodePublishingConfigurationModel(expandedNodeId, opcNode.Id,
                                                publisherConfigFileEntryLegacy.EndpointUrl.OriginalString, publisherConfigFileEntryLegacy.UseSecurity,
                                                opcNode.OpcPublishingInterval, opcNode.OpcSamplingInterval, opcNode.DisplayName,
                                                opcNode.HeartbeatInterval, opcNode.SkipFirst, publisherConfigFileEntryLegacy.OpcAuthenticationMode, publisherConfigFileEntryLegacy.EncryptedAuthCredential));
                                        }
                                        else
                                        {
                                            // NodeId format
                                            NodeId nodeId = NodeId.Parse(opcNode.Id);
                                            _nodePublishingConfiguration.Add(new NodePublishingConfigurationModel(nodeId, opcNode.Id,
                                                publisherConfigFileEntryLegacy.EndpointUrl.OriginalString, publisherConfigFileEntryLegacy.UseSecurity,
                                                opcNode.OpcPublishingInterval, opcNode.OpcSamplingInterval, opcNode.DisplayName,
                                                opcNode.HeartbeatInterval, opcNode.SkipFirst, publisherConfigFileEntryLegacy.OpcAuthenticationMode, publisherConfigFileEntryLegacy.EncryptedAuthCredential));
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // NodeId (ns=) format node configuration syntax using default sampling and publishing interval.
                                _nodePublishingConfiguration.Add(new NodePublishingConfigurationModel(publisherConfigFileEntryLegacy.NodeId,
                                    publisherConfigFileEntryLegacy.NodeId.ToString(), publisherConfigFileEntryLegacy.EndpointUrl.OriginalString, publisherConfigFileEntryLegacy.UseSecurity,
                                    null, null, null,
                                    null, null, publisherConfigFileEntryLegacy.OpcAuthenticationMode, publisherConfigFileEntryLegacy.EncryptedAuthCredential));
                            }
                        }
                    }
                }
                else
                {
                    Program.Instance.Logger.Information($"The node configuration file '{SettingsConfiguration.PublisherNodeConfigurationFilename}' does not exist. Continue and wait for remote configuration requests.");
                }
            }
            catch (Exception e)
            {
                Program.Instance.Logger.Fatal(e, "Loading of the node configuration file failed. Does the file exist and has correct syntax? Exiting...");
                return false;
            }
            finally
            {
                PublisherNodeConfigurationSemaphore.Release();
            }
            Program.Instance.Logger.Information($"There are {_nodePublishingConfiguration.Count.ToString(CultureInfo.InvariantCulture)} nodes to publish.");
            return true;
        }

        /// <summary>
        /// Create the publisher data structures to manage OPC sessions, subscriptions and monitored items.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CreateOpcPublishingDataAsync()
        {
            // create a list to manage sessions, subscriptions and monitored items.
            try
            {
                await PublisherNodeConfigurationSemaphore.WaitAsync().ConfigureAwait(false);
                await OpcSessionsListSemaphore.WaitAsync().ConfigureAwait(false);

                var uniqueEndpointUrls = _nodePublishingConfiguration.Select(n => n.EndpointUrl).Distinct();
                foreach (var endpointUrl in uniqueEndpointUrls)
                {
                    var currentNodePublishingConfiguration = _nodePublishingConfiguration.First(n => n.EndpointUrl == endpointUrl);

                    EncryptedNetworkCredential encryptedAuthCredential = null;

                    if (currentNodePublishingConfiguration.OpcAuthenticationMode == OpcUserSessionAuthenticationMode.UsernamePassword)
                    {
                        if (currentNodePublishingConfiguration.EncryptedAuthCredential == null)
                        {
                            throw new NullReferenceException($"Could not retrieve credentials to authenticate to the server. Please check if 'OpcAuthenticationUsername' and 'OpcAuthenticationPassword' are set in configuration.");
                        }

                        encryptedAuthCredential = currentNodePublishingConfiguration.EncryptedAuthCredential;
                    }

                    // create new session info.
                    OpcUaSessionWrapper opcSession = new OpcUaSessionWrapper(endpointUrl, currentNodePublishingConfiguration.UseSecurity, (uint)Program.Instance._application.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout, currentNodePublishingConfiguration.OpcAuthenticationMode, encryptedAuthCredential);

                    // create a subscription for each distinct publishing inverval
                    var nodesDistinctPublishingInterval = _nodePublishingConfiguration.Where(n => n.EndpointUrl.Equals(endpointUrl, StringComparison.OrdinalIgnoreCase)).Select(c => c.OpcPublishingInterval).Distinct();
                    foreach (var nodeDistinctPublishingInterval in nodesDistinctPublishingInterval)
                    {
                        // create a subscription for the publishing interval and add it to the session.
                        OpcUaSubscriptionWrapper opcSubscription = new OpcUaSubscriptionWrapper(nodeDistinctPublishingInterval);

                        // add all nodes with this OPC publishing interval to this subscription.
                        var nodesWithSamePublishingInterval = _nodePublishingConfiguration.Where(n => n.EndpointUrl.Equals(endpointUrl, StringComparison.OrdinalIgnoreCase)).Where(n => n.OpcPublishingInterval == nodeDistinctPublishingInterval);
                        foreach (var nodeInfo in nodesWithSamePublishingInterval)
                        {
                            // differentiate if NodeId or ExpandedNodeId format is used
                            if (nodeInfo.ExpandedNodeId != null)
                            {
                                // create a monitored item for the node, we do not have the namespace index without a connected session. 
                                // so request a namespace update.
                                OpcUaMonitoredItemWrapper opcMonitoredItem = new OpcUaMonitoredItemWrapper(nodeInfo.ExpandedNodeId, opcSession.EndpointUrl,
                                    nodeInfo.OpcSamplingInterval, nodeInfo.DisplayName, nodeInfo.HeartbeatInterval, nodeInfo.SkipFirst);
                                opcSubscription.OpcMonitoredItems.Add(opcMonitoredItem);
                                Interlocked.Increment(ref OpcUaSessionWrapper.NodeConfigVersion);
                            }
                            else if (nodeInfo.NodeId != null)
                            {
                                // create a monitored item for the node with the configured or default sampling interval
                                OpcUaMonitoredItemWrapper opcMonitoredItem = new OpcUaMonitoredItemWrapper(nodeInfo.NodeId, opcSession.EndpointUrl,
                                    nodeInfo.OpcSamplingInterval, nodeInfo.DisplayName, nodeInfo.HeartbeatInterval, nodeInfo.SkipFirst);
                                opcSubscription.OpcMonitoredItems.Add(opcMonitoredItem);
                                Interlocked.Increment(ref OpcUaSessionWrapper.NodeConfigVersion);
                            }
                            else
                            {
                                Program.Instance.Logger.Error($"Node {nodeInfo.OriginalId} has an invalid format. Skipping...");
                            }
                        }

                        // add subscription to session.
                        opcSession.OpcSubscriptionWrappers.Add(opcSubscription);
                    }

                    // add session.
                    OpcSessions.Add(opcSession);
                }
            }
            catch (Exception e)
            {
                Program.Instance.Logger.Fatal(e, "Creation of the internal OPC data managment structures failed. Exiting...");
                return false;
            }
            finally
            {
                OpcSessionsListSemaphore.Release();
                PublisherNodeConfigurationSemaphore.Release();
            }
            return true;
        }

        /// <summary>
        /// Returns a list of all published nodes for a specific endpoint in config file format.
        /// </summary>
        /// <returns></returns>
        public List<ConfigurationFileEntryModel> GetPublisherConfigurationFileEntries(string endpointUrl, bool getAll, out uint nodeConfigVersion)
        {
            List<ConfigurationFileEntryModel> publisherConfigurationFileEntries = new List<ConfigurationFileEntryModel>();
            nodeConfigVersion = (uint)OpcUaSessionWrapper.NodeConfigVersion;
            try
            {
                PublisherNodeConfigurationSemaphore.Wait();

                try
                {
                    OpcSessionsListSemaphore.Wait();

                    // itereate through all sessions, subscriptions and monitored items and create config file entries
                    foreach (var session in OpcSessions)
                    {
                        bool sessionLocked = false;
                        try
                        {
                            sessionLocked = session.LockSessionAsync().Result;
                            if (sessionLocked && (endpointUrl == null || session.EndpointUrl.Equals(endpointUrl, StringComparison.OrdinalIgnoreCase)))
                            {
                                ConfigurationFileEntryModel publisherConfigurationFileEntry = new ConfigurationFileEntryModel();

                                publisherConfigurationFileEntry.EndpointUrl = new Uri(session.EndpointUrl);
                                publisherConfigurationFileEntry.OpcAuthenticationMode = session.OpcAuthenticationMode;
                                publisherConfigurationFileEntry.EncryptedAuthCredential = session.EncryptedAuthCredential;
                                publisherConfigurationFileEntry.UseSecurity = session.UseSecurity;
                                publisherConfigurationFileEntry.OpcNodes = new List<OpcNodeOnEndpointModel>();

                                foreach (var subscription in session.OpcSubscriptionWrappers)
                                {
                                    foreach (var monitoredItem in subscription.OpcMonitoredItems)
                                    {
                                        // ignore items tagged to stop
                                        if (monitoredItem.State != OpcUaMonitoredItemWrapper.OpcMonitoredItemState.RemovalRequested || getAll == true)
                                        {
                                            OpcNodeOnEndpointModel opcNodeOnEndpoint = new OpcNodeOnEndpointModel(monitoredItem.OriginalId) {
                                                OpcPublishingInterval = subscription.PublishingInterval,
                                                OpcSamplingInterval = monitoredItem.RequestedSamplingIntervalFromConfiguration ? monitoredItem.RequestedSamplingInterval : (int?)null,
                                                DisplayName = monitoredItem.DisplayNameFromConfiguration ? monitoredItem.DisplayName : null,
                                                HeartbeatInterval = monitoredItem.HeartbeatIntervalFromConfiguration ? (int?)monitoredItem.HeartbeatInterval : null,
                                                SkipFirst = monitoredItem.SkipFirstFromConfiguration ? (bool?)monitoredItem.SkipFirst : null
                                            };
                                            publisherConfigurationFileEntry.OpcNodes.Add(opcNodeOnEndpoint);
                                        }
                                    }
                                }
                                publisherConfigurationFileEntries.Add(publisherConfigurationFileEntry);
                            }
                        }
                        finally
                        {
                            if (sessionLocked)
                            {
                                session.ReleaseSession();
                            }
                        }
                    }
                    nodeConfigVersion = (uint)OpcUaSessionWrapper.NodeConfigVersion;
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                Program.Instance.Logger.Error(e, "Reading configuration file entries failed.");
                publisherConfigurationFileEntries = null;
            }
            finally
            {
                PublisherNodeConfigurationSemaphore.Release();
            }
            return publisherConfigurationFileEntries;
        }

        /// <summary>
        /// Returns a list of all configured nodes in NodeId format.
        /// </summary>
        /// <returns></returns>
        public async Task<List<ConfigurationFileEntryLegacyModel>> GetPublisherConfigurationFileEntriesAsNodeIdsAsync(string endpointUrl)
        {
            List<ConfigurationFileEntryLegacyModel> publisherConfigurationFileEntriesLegacy = new List<ConfigurationFileEntryLegacyModel>();
            try
            {
                await PublisherNodeConfigurationSemaphore.WaitAsync().ConfigureAwait(false);

                try
                {
                    await OpcSessionsListSemaphore.WaitAsync().ConfigureAwait(false);

                    // itereate through all sessions, subscriptions and monitored items and create config file entries
                    foreach (var session in OpcSessions)
                    {
                        bool sessionLocked = false;
                        try
                        {
                            sessionLocked = await session.LockSessionAsync().ConfigureAwait(false);
                            if (sessionLocked && (endpointUrl == null || session.EndpointUrl.Equals(endpointUrl, StringComparison.OrdinalIgnoreCase)))
                            {

                                foreach (var subscription in session.OpcSubscriptionWrappers)
                                {
                                    foreach (var monitoredItem in subscription.OpcMonitoredItems)
                                    {
                                        // ignore items tagged to stop
                                        if (monitoredItem.State != OpcUaMonitoredItemWrapper.OpcMonitoredItemState.RemovalRequested)
                                        {
                                            ConfigurationFileEntryLegacyModel publisherConfigurationFileEntryLegacy = new ConfigurationFileEntryLegacyModel();
                                            publisherConfigurationFileEntryLegacy.EndpointUrl = new Uri(session.EndpointUrl);
                                            publisherConfigurationFileEntryLegacy.NodeId = null;
                                            publisherConfigurationFileEntryLegacy.OpcNodes = null;

                                            if (monitoredItem.ConfigType == OpcUaMonitoredItemWrapper.OpcMonitoredItemConfigurationType.ExpandedNodeId)
                                            {
                                                // for certain scenarios we support returning the NodeId format even so the
                                                // actual configuration of the node was in ExpandedNodeId format
                                                publisherConfigurationFileEntryLegacy.EndpointUrl = new Uri(session.EndpointUrl);
                                                publisherConfigurationFileEntryLegacy.NodeId = new NodeId(monitoredItem.ConfigExpandedNodeId.Identifier, (ushort)session.GetNamespaceIndexUnlocked(monitoredItem.ConfigExpandedNodeId?.NamespaceUri));
                                                publisherConfigurationFileEntriesLegacy.Add(publisherConfigurationFileEntryLegacy);
                                            }
                                            else
                                            {
                                                // we do not convert nodes with legacy configuration to the new format to keep backward
                                                // compatibility with external configurations.
                                                // the conversion would only be possible, if the session is connected, to have access to the
                                                // server namespace array.
                                                publisherConfigurationFileEntryLegacy.EndpointUrl = new Uri(session.EndpointUrl);
                                                publisherConfigurationFileEntryLegacy.NodeId = monitoredItem.ConfigNodeId;
                                                publisherConfigurationFileEntriesLegacy.Add(publisherConfigurationFileEntryLegacy);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (sessionLocked)
                            {
                                session.ReleaseSession();
                            }
                        }
                    }
                }
                finally
                {
                    OpcSessionsListSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                Program.Instance.Logger.Error(e, "Creation of configuration file entries failed.");
                publisherConfigurationFileEntriesLegacy = null;
            }
            finally
            {
                PublisherNodeConfigurationSemaphore.Release();
            }
            return publisherConfigurationFileEntriesLegacy;
        }

        /// <summary>
        /// Updates the configuration file to persist all currently published nodes
        /// </summary>
        public async Task UpdateNodeConfigurationFileAsync()
        {
            if (OpcUaSessionWrapper.NodeConfigVersion == _lastNodeConfigVersion)
                return;

            try
            {
                // iterate through all sessions, subscriptions and monitored items and create config file entries
                List<ConfigurationFileEntryModel> publisherNodeConfiguration = GetPublisherConfigurationFileEntries(null, true, out _lastNodeConfigVersion);

                Program.Instance.Logger.Debug($"Update node configuration file, version: {_lastNodeConfigVersion:X8}");

                // update the config file
                try
                {
                    await PublisherNodeConfigurationFileSemaphore.WaitAsync().ConfigureAwait(false);
                    await File.WriteAllTextAsync(SettingsConfiguration.PublisherNodeConfigurationFilename, JsonConvert.SerializeObject(publisherNodeConfiguration, Formatting.Indented)).ConfigureAwait(false);
                }
                finally
                {
                    PublisherNodeConfigurationFileSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                Program.Instance.Logger.Error(e, "Update of node configuration file failed.");
            }
        }

        private List<NodePublishingConfigurationModel> _nodePublishingConfiguration;
        private List<ConfigurationFileEntryLegacyModel> _configurationFileEntries;
    }
}