---
page_type: sample
languages:
- csharp
products:
- azure
extensions:
  services: Compute
  platforms: dotnet
---

# Manage virtual machine scale sets in availability zones #

 Azure Compute sample for managing virtual machine scale set -
  - Create a zone resilient public ip address
  - Create a zone resilient load balancer with
         - the existing zone resilient ip address
         - two load balancing rule which is applied to two different backend pools
  - Create two zone redundant virtual machine scale set each associated with one backend pool
  - Update the virtual machine scale set by appending new zone.


## Running this Sample ##

To run this sample:

Set the environment variable `CLIENT_ID`,`CLIENT_SECRET`,`TENANT_ID`,`SUBSCRIPTION_ID` with the full path for an auth file. See [how to create an auth file](https://github.com/Azure/azure-libraries-for-net/blob/master/AUTH.md).

    git clone https://github.com/Azure-Samples/compute-dotnet-manage-vmss-in-availability-zones.git

    cd compute-dotnet-manage-vmss-in-availability-zones

    dotnet build

    bin\Debug\net452\ManageZonalVirtualMachineScaleSet.exe

## More information ##

[Azure Management Libraries for C#](https://github.com/Azure/azure-sdk-for-net/)
[Azure .Net Developer Center](https://azure.microsoft.com/en-us/develop/net/)
If you don't have a Microsoft Azure subscription you can get a FREE trial account [here](http://go.microsoft.com/fwlink/?LinkId=330212)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.