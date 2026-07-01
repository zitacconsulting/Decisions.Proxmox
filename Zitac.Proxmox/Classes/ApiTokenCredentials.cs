using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Service.Debugging.DebugData;
using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class ApiTokenCredentials : IDebuggerJsonProvider
{
    [DataMember]
    [WritableValue]
    public string? ApiTokenId { get; set; }

    [DataMember]
    [WritableValue]
    [PasswordText]
    public string? ApiTokenSecret { get; set; }

    public object GetJsonDebugView() => new
    {
        ApiTokenId = this.ApiTokenId,
        ApiTokenSecret = "********"
    };
}
