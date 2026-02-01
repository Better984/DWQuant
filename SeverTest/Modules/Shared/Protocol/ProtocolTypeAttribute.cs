using System;

namespace ServerTest.Protocol
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ProtocolTypeAttribute : Attribute
    {
        public ProtocolTypeAttribute(string type)
        {
            Type = type ?? string.Empty;
        }

        public string Type { get; }
    }
}
