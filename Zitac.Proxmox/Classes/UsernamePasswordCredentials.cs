using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Service.Debugging.DebugData;
using System.Runtime.Serialization;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Zitac.Proxmox.Steps;

[AutoRegisterNativeType]
[DataContract]
public class UsernamePasswordCredentials : IDebuggerJsonProvider
{
    [DataMember]
    [WritableValue]
    public string? Username { get; set; }

    [DataMember]
    [WritableValue]
    [PasswordText]
    public string? Password { get; set; }

    public object GetJsonDebugView() => new
    {
        Username = this.Username,
        Password = "********"
    };
}
