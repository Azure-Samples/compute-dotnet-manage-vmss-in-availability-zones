// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;

namespace ManageZonalVirtualMachineScaleSet
{
    public class Program
    {
        /**
         * Azure Compute sample for managing virtual machine scale set -
         *  - Create a zone resilient public ip address
         *  - Create a zone resilient load balancer with
         *         - the existing zone resilient ip address
         *         - two load balancing rule which is applied to two different backend pools
         *  - Create two zone redundant virtual machine scale set each associated with one backend pool
         *  - Update the virtual machine scale set by appending new zone.
         */
        public static async Task RunSample(ArmClient client)
        {
            var region = AzureLocation.EastUS;
            var rgName = Utilities.CreateRandomName("rgCOMV");
            var loadBalancerName = Utilities.CreateRandomName("extlb");
            var publicIPName = "pip-" + loadBalancerName;
            var frontendName = loadBalancerName + "-FE1";
            var backendPoolName1 = loadBalancerName + "-BAP1";
            var backendPoolName2 = loadBalancerName + "-BAP2";
            var natPoolName1 = loadBalancerName + "-INP1";
            var natPoolName2 = loadBalancerName + "-INP2";
            var vmssName1 = Utilities.CreateRandomName("vmss1");
            var vmssName2 = Utilities.CreateRandomName("vmss2");
            var domainNameLabel = Utilities.CreateRandomName("domain");
            var vmssNetworkConfigurationName = Utilities.CreateRandomName("networkConfiguration");
            var ipConfigurationName = Utilities.CreateRandomName("ipconfigruation");
            var userName = Utilities.CreateUsername();
            var password = Utilities.CreatePassword();
            var lro = await client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdateAsync(Azure.WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;

            try
            {
                //=============================================================
                // Create a zone resilient PublicIP address

                Utilities.Log("Creating a zone resilient public ip address");

                var publicIpAddressCollection = resourceGroup.GetPublicIPAddresses();
                var publicIPAddressData = new PublicIPAddressData()
                {
                    Location = region,
                    Tags = { { "key", "value" } },
                    Sku = new PublicIPAddressSku()
                    {
                        Tier = "Standard"
                    }
                };
                var publicIpAddress = (await publicIpAddressCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, publicIPName, publicIPAddressData)).Value;
                Utilities.Log("Created a zone resilient public ip address: " + publicIpAddress.Id);

                //=============================================================
                // Create a zone resilient load balancer

                Utilities.Log("Creating a zone resilient load balancer");
                var loadBalancerCollection = resourceGroup.GetLoadBalancers();
                var loadBalancerData = new LoadBalancerData()
                {
                    Location = region,
                    Sku = new LoadBalancerSku()
                    {
                        Tier = "Standard"
                    },
                    LoadBalancingRules =
                    {
                        new LoadBalancingRuleData()
                        {
                            Name = "httpRule",
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            ProbeId = new ResourceIdentifier("httpProbe"),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName1)
                                }
                            }
                        },
                        new LoadBalancingRuleData()
                        {
                            Name= "httpRule",
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 443,
                            ProbeId = new ResourceIdentifier("httpProbe"),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName2)
                                }
                            }
                        }
                    },
                    // Add nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = natPoolName1,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 22,
                            FrontendPortRangeStart = 5000,
                            FrontendPortRangeEnd = 5099,
                        },
                        new InboundNatRuleData()
                        {
                            Name = natPoolName2,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 23,
                            FrontendPortRangeStart = 6000,
                            FrontendPortRangeEnd = 6099,
                        }
                    },
                    // Add two probes one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Name = "httpProbe"
                        },
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Name = "httpProbe"
                        }
                    },
                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            PublicIPAddress = new PublicIPAddressData()
                            {
                                Location = region,
                                Tags = { { "key", "value" } },
                                Sku = new PublicIPAddressSku()
                                {
                                    Tier = "Standard"
                                }
                            }
                        }
                    }
                };
                var loadBalancer = (await loadBalancerCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, loadBalancerName, loadBalancerData)).Value;

                Utilities.Log("Created a zone resilient load balancer: " + loadBalancer.Id);

                var backends = new List<string>();
                foreach (var backend in loadBalancer.Data.BackendAddressPools)
                {
                    backends.Add(backend.Id);
                }
                var natpools = new List<string>();
                foreach (var natPool in loadBalancer.Data.InboundNatPools)
                {
                    natpools.Add(natPool.Name);
                }

                Utilities.Log("Creating network for virtual machine scale sets");

                var networkCollection = resourceGroup.GetVirtualNetworks();
                var networkData = new VirtualNetworkData()
                {
                    Location = region,
                    AddressPrefixes =
                    {
                        "10.0.0.0/28"
                    }
                };
                var networkResource = (await networkCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, "vmssvnet", networkData)).Value;
                var subnetCollection = networkResource.GetSubnets();
                var subnetData = new SubnetData()
                {
                    AddressPrefix = "10.0.0.0/28"
                };
                var subnetResource = (await subnetCollection.CreateOrUpdateAsync(WaitUntil.Completed, "subnet1", subnetData)).Value;

                Utilities.Log("Created network for virtual machine scale sets");

                //=============================================================
                // Create a zone redundant virtual machine scale set

                Utilities.Log("Creating a zone redundant virtual machine scale set");

                // HTTP goes to this virtual machine scale set
                //
                var vmScaleSetVMCollection = resourceGroup.GetVirtualMachineScaleSets();
                var scaleSetData = new VirtualMachineScaleSetData(region)
                {
                    Sku = new ComputeSku()
                    {
                        Tier = "StandardD3v2"
                    },
                    VirtualMachineProfile = new VirtualMachineScaleSetVmProfile()
                    {
                        StorageProfile = new VirtualMachineScaleSetStorageProfile()
                        {
                            ImageReference = new ImageReference()
                            {
                                Publisher = "Canonical",
                                Offer = "UbuntuServer",
                                Sku = "16.04-LTS",
                                Version = "latest"
                            }
                        },
                        NetworkProfile = new VirtualMachineScaleSetNetworkProfile()
                        {
                            NetworkInterfaceConfigurations =
                           {
                               new VirtualMachineScaleSetNetworkConfiguration(vmssNetworkConfigurationName)
                               {
                                   IPConfigurations =
                                   {
                                       new VirtualMachineScaleSetIPConfiguration(ipConfigurationName)
                                       {
                                           LoadBalancerInboundNatPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(natpools[0])
                                               },
                                           },
                                           ApplicationGatewayBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(backends[0])
                                               },
                                           },
                                           LoadBalancerBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(loadBalancer.Data.Name)
                                               }
                                           },
                                           SubnetId = subnetResource.Id,
                                           Primary = true,
                                       }
                                   }
                               }
                           }
                        },
                    },
                    Zones =
                    {
                        "AvailabilityZoneId.Zone_1",
                        "AvailabilityZoneId.Zone_2"
                    }

                };
                var vmScaleSet1 = (await vmScaleSetVMCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmssName1, scaleSetData)).Value;

                Utilities.Log("Created first zone redundant virtual machine scale set");

                //=============================================================
                // Create a zone redundant virtual machine scale set

                Utilities.Log("Creating second zone redundant virtual machine scale set");

                // HTTPS goes to this virtual machine scale set
                //
                var scaleSet2Data = new VirtualMachineScaleSetData(region)
                {
                    Sku = new ComputeSku()
                    {
                        Tier = "StandardD3v2"
                    },
                    VirtualMachineProfile = new VirtualMachineScaleSetVmProfile()
                    {
                        StorageProfile = new VirtualMachineScaleSetStorageProfile()
                        {
                            ImageReference = new ImageReference()
                            {
                                Publisher = "Canonical",
                                Offer = "UbuntuServer",
                                Sku = "16.04-LTS",
                                Version = "latest"
                            }
                        },
                        NetworkProfile = new VirtualMachineScaleSetNetworkProfile()
                        {
                            NetworkInterfaceConfigurations =
                           {
                               new VirtualMachineScaleSetNetworkConfiguration(vmssNetworkConfigurationName)
                               {
                                   IPConfigurations =
                                   {
                                       new VirtualMachineScaleSetIPConfiguration(ipConfigurationName)
                                       {
                                           LoadBalancerInboundNatPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(natpools[1])
                                               },
                                           },
                                           ApplicationGatewayBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(backends[1])
                                               },
                                           },
                                           LoadBalancerBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(loadBalancer.Data.Name)
                                               }
                                           },
                                           SubnetId = subnetResource.Id,
                                           Primary = true,
                                       }
                                   }
                               }
                           }
                        },
                    },
                    Zones =
                    {
                        "AvailabilityZoneId.Zone_1",
                        "AvailabilityZoneId.Zone_2"
                    }

                };
                var vmScaleSet2 = (await vmScaleSetVMCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmssName2, scaleSet2Data)).Value;
                Utilities.Log("Created second zone redundant virtual machine scale set");
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + rgName);
                    await resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + rgName);
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=============================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}
