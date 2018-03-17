// program.cs  
//----------------------------------------------------------------  
// Copyright (c) Microsoft Corporation.  All rights reserved.  
//----------------------------------------------------------------  

using System;
using System.Security.Principal;
using System.ServiceModel;
using System.ServiceModel.Discovery;

namespace Microsoft.Samples.Discovery
{
    class Program
    {
        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                .IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void Main()
        {
            Uri probeEndpointAddress = new Uri("net.tcp://localhost:8001/Probe");
            Uri announcementEndpointAddress = new Uri("net.tcp://localhost:9021/Announcement");
            
            var probePortSharingBinding = new NetTcpBinding(SecurityMode.None);
            var announcementPortSharingBinding = new NetTcpBinding(SecurityMode.None);

            if (IsAdministrator())
            {
                probePortSharingBinding.PortSharingEnabled = true;
                announcementPortSharingBinding.PortSharingEnabled = true;
            }

            // Host the DiscoveryProxy service  
            ServiceHost proxyServiceHost = new ServiceHost(new DiscoveryProxyService());

            try
            {
                // Add DiscoveryEndpoint to receive Probe and Resolve messages  
                DiscoveryEndpoint discoveryEndpoint = 
                    new DiscoveryEndpoint(probePortSharingBinding, new EndpointAddress(probeEndpointAddress));
                discoveryEndpoint.IsSystemEndpoint = false;

                // Add AnnouncementEndpoint to receive Hello and Bye announcement messages  
                AnnouncementEndpoint announcementEndpoint = 
                    new AnnouncementEndpoint(announcementPortSharingBinding, new EndpointAddress(announcementEndpointAddress));
                
                proxyServiceHost.AddServiceEndpoint(discoveryEndpoint);
                proxyServiceHost.AddServiceEndpoint(announcementEndpoint);

                proxyServiceHost.Open();

                Console.WriteLine("Proxy Service started.");
                Console.WriteLine();
                Console.WriteLine("Press <ENTER> to terminate the service.");
                Console.WriteLine();
                Console.ReadLine();

                proxyServiceHost.Close();
            }
            catch (CommunicationException e)
            {
                Console.WriteLine(e.Message + "\n\r");

                if (e.Message.Contains("There is already a listener"))
                {
                    Console.WriteLine("Try to run the application as Administrator:");
                    Console.WriteLine("Right click it and then select \"Run as administrator\".\n\r");
                }
            }
            catch (TimeoutException e)
            {
                Console.WriteLine(e.Message);
            }

            if (proxyServiceHost.State != CommunicationState.Closed)
            {
                Console.WriteLine("Aborting the service...");
                Console.ReadLine();
                proxyServiceHost.Abort();
            }
        }
    }
}
