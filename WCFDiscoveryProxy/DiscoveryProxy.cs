// DiscoveryProxy.cs  
//----------------------------------------------------------------  
// Copyright (c) Microsoft Corporation.  All rights reserved.  
//----------------------------------------------------------------  

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Discovery;
using System.Xml;
using System.Xml.Linq;

namespace WCFDiscoveryProxy
{
    // Implement DiscoveryProxy by extending the DiscoveryProxy class and overriding the abstract methods  
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class DiscoveryProxyService : DiscoveryProxy
    {
        // Repository to store EndpointDiscoveryMetadata. A database or a flat file could also be used instead.  
        private readonly Dictionary<string, EndpointDiscoveryMetadata> _onlineServices;

        public DiscoveryProxyService()
        {
            this._onlineServices = new Dictionary<string, EndpointDiscoveryMetadata>();
        }

        // OnBeginOnlineAnnouncement method is called when a Hello message is received by the Proxy  
        protected override IAsyncResult OnBeginOnlineAnnouncement(DiscoveryMessageSequence messageSequence, EndpointDiscoveryMetadata endpointDiscoveryMetadata, AsyncCallback callback, object state)
        {
            this.AddOnlineService(endpointDiscoveryMetadata);
            return new OnOnlineAnnouncementAsyncResult(callback, state);
        }

        protected override void OnEndOnlineAnnouncement(IAsyncResult result)
        {
            OnOnlineAnnouncementAsyncResult.End(result);
        }

        // OnBeginOfflineAnnouncement method is called when a Bye message is received by the Proxy  
        protected override IAsyncResult OnBeginOfflineAnnouncement(DiscoveryMessageSequence messageSequence, EndpointDiscoveryMetadata endpointDiscoveryMetadata, AsyncCallback callback, object state)
        {
            this.RemoveOnlineService(endpointDiscoveryMetadata);
            return new OnOfflineAnnouncementAsyncResult(callback, state);
        }

        protected override void OnEndOfflineAnnouncement(IAsyncResult result)
        {
            OnOfflineAnnouncementAsyncResult.End(result);
        }

        // OnBeginFind method is called when a Probe request message is received by the Proxy  
        protected override IAsyncResult OnBeginFind(FindRequestContext findRequestContext, AsyncCallback callback, object state)
        {
            this.MatchFromOnlineService(findRequestContext);
            return new OnFindAsyncResult(callback, state);
        }

        protected override void OnEndFind(IAsyncResult result)
        {
            OnFindAsyncResult.End(result);
        }

        // OnBeginFind method is called when a Resolve request message is received by the Proxy  
        protected override IAsyncResult OnBeginResolve(ResolveCriteria resolveCriteria, AsyncCallback callback, object state)
        {
            return new OnResolveAsyncResult(this.MatchFromOnlineService(resolveCriteria), callback, state);
        }

        protected override EndpointDiscoveryMetadata OnEndResolve(IAsyncResult result)
        {
            return OnResolveAsyncResult.End(result);
        }

        // The following are helper methods required by the Proxy implementation  
        void AddOnlineService(EndpointDiscoveryMetadata endpointDiscoveryMetadata)
        {
            if (endpointDiscoveryMetadata == null)
                throw new Exception("Metadata is invalid.");

            var mexCriteria = new FindCriteria(typeof(IMetadataExchange));
            if (mexCriteria.IsMatch(endpointDiscoveryMetadata)) return;

            if (endpointDiscoveryMetadata.Extensions.Count == 0)
                throw new Exception("Endpoint is invalid.");

            string serviceContract = string.Empty;
            string serviceId = string.Empty;
            string serviceRequirements = string.Empty;

            foreach (XElement customMetadata in endpointDiscoveryMetadata.Extensions)
            {
                switch (customMetadata.Name.LocalName)
                {
                    case "Contract":
                        serviceContract = customMetadata.Value;
                        break;
                    case "Id":
                        serviceId = customMetadata.Value;
                        break;
                    case "Requirements":
                        serviceRequirements = customMetadata.Value;
                        break;
                }
            }

            if (serviceContract == string.Empty)
                throw new Exception("Service Contract is missing.");

            lock (this._onlineServices)
            {
                if (serviceRequirements != string.Empty)
                {
                    if (!serviceRequirements.Split().All(req =>
                        this._onlineServices.Values.Any(x =>
                            x.Extensions.Any(y =>
                                y.Name.LocalName == "Contract" && y.Value == req))))
                    {
                        throw new Exception("Service requirement couldn't be matched.");
                    }
                }

                if (serviceId == string.Empty)
                {
                    serviceId = serviceContract;
                }
                else if (this._onlineServices.ContainsKey(serviceId))
                    throw new Exception("Service Id is duplicated.");

                this._onlineServices[serviceId] = endpointDiscoveryMetadata;
            }

            PrintDiscoveryMetadata(endpointDiscoveryMetadata, "Adding");
        }

        void RemoveOnlineService(EndpointDiscoveryMetadata endpointDiscoveryMetadata)
        {
            if (endpointDiscoveryMetadata == null)
                throw new Exception("Metadata is invalid.");

            var mexCriteria = new FindCriteria(typeof(IMetadataExchange));
            if (mexCriteria.IsMatch(endpointDiscoveryMetadata)) return;

            lock (this._onlineServices)
            {
                XElement delService = endpointDiscoveryMetadata.Extensions.FirstOrDefault(x => 
                                          x.Name.LocalName == "Id" && x.Value != "") ??
                                      endpointDiscoveryMetadata.Extensions.FirstOrDefault(x =>
                                          x.Name.LocalName == "Contract" && x.Value != "");
                if (delService == null) return;

                this._onlineServices.Remove(delService.Value);
            }

            PrintDiscoveryMetadata(endpointDiscoveryMetadata, "Removing");
        }

        void MatchFromOnlineService(FindRequestContext findRequestContext)
        {
            lock (this._onlineServices)
            {
                string foundId = findRequestContext.Criteria.Extensions.FirstOrDefault(x =>
                    x.Name.LocalName == "Id")?.Value;
                if (foundId != null)
                {
                    EndpointDiscoveryMetadata endpointMetadata = this._onlineServices[foundId];

                    if (!findRequestContext.Criteria.IsMatch(endpointMetadata)) return;

                    findRequestContext.AddMatchingEndpoint(endpointMetadata);
                }
                else
                {
                    foreach (EndpointDiscoveryMetadata endpointMetadata in this._onlineServices.Values)
                    {
                        if (!findRequestContext.Criteria.IsMatch(endpointMetadata)) continue;

                        int countdown = findRequestContext.Criteria.Extensions.Count;
                        foreach (XElement elem in findRequestContext.Criteria.Extensions)
                        {
                            if (endpointMetadata.Extensions.All(x => 
                                x.Name.LocalName == elem.Name.LocalName && x.Value != elem.Value))
                            {
                                break;
                            }

                            countdown--;
                        }

                        if (countdown == 0)
                        {
                            findRequestContext.AddMatchingEndpoint(endpointMetadata);
                        }
                    }
                }
            }
        }

        EndpointDiscoveryMetadata MatchFromOnlineService(ResolveCriteria criteria)
        {
            EndpointDiscoveryMetadata matchingEndpoint = null;
            lock (this._onlineServices)
            {
                foreach (EndpointDiscoveryMetadata endpointMetadata in this._onlineServices.Values)
                {
                    EndpointAddress address;
                    // Check to see if the endpoint has a listenUri and if it differs from the Address URI
                    if (endpointMetadata.ListenUris.Count == 0 ||
                        endpointMetadata.Address.Uri == endpointMetadata.ListenUris[0])
                    {
                        address = endpointMetadata.Address;
                    }
                    else
                    {
                        address = new EndpointAddress(endpointMetadata.ListenUris[0]);
                    }

                    if (criteria.Address != address) continue;
                    matchingEndpoint = endpointMetadata;
                    break;
                }
            }
            return matchingEndpoint;
        }

        void PrintDiscoveryMetadata(EndpointDiscoveryMetadata endpointDiscoveryMetadata, string verb)
        {
            Console.WriteLine("\n**** " + verb + " service of the following type from cache. ");
            foreach (XmlQualifiedName contractName in endpointDiscoveryMetadata.ContractTypeNames)
            {
                Console.WriteLine("** " + contractName);
                break;
            }
            Console.WriteLine("**** Operation Completed");
        }

        sealed class OnOnlineAnnouncementAsyncResult : AsyncResult
        {
            public OnOnlineAnnouncementAsyncResult(AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.Complete(true);
            }

            public static void End(IAsyncResult result)
            {
                AsyncResult.End<OnOnlineAnnouncementAsyncResult>(result);
            }
        }

        sealed class OnOfflineAnnouncementAsyncResult : AsyncResult
        {
            public OnOfflineAnnouncementAsyncResult(AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.Complete(true);
            }

            public static void End(IAsyncResult result)
            {
                AsyncResult.End<OnOfflineAnnouncementAsyncResult>(result);
            }
        }

        sealed class OnFindAsyncResult : AsyncResult
        {
            public OnFindAsyncResult(AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.Complete(true);
            }

            public static void End(IAsyncResult result)
            {
                AsyncResult.End<OnFindAsyncResult>(result);
            }
        }

        sealed class OnResolveAsyncResult : AsyncResult
        {
            private readonly EndpointDiscoveryMetadata _matchingEndpoint;

            public OnResolveAsyncResult(EndpointDiscoveryMetadata matchingEndpoint, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this._matchingEndpoint = matchingEndpoint;
                this.Complete(true);
            }

            public static EndpointDiscoveryMetadata End(IAsyncResult result)
            {
                OnResolveAsyncResult thisPtr = AsyncResult.End<OnResolveAsyncResult>(result);
                return thisPtr._matchingEndpoint;
            }
        }
    }
}
