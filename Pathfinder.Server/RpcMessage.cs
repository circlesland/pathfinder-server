using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pathfinder.Server
{
    public class RpcMessage
    {
        public readonly uint Id;
        private static uint _id;

        public readonly string Cmd;
        public readonly string Data;

        public RpcMessage(string command, JObject data)
        {
            Id = Interlocked.Increment(ref _id);
            Cmd = command;
            Data = data.ToString(Formatting.None);
        }

        public override string ToString()
        {
            dynamic jsonObject = JObject.Parse(Data);
            jsonObject.id = Id;
            jsonObject.cmd = Cmd;

            return jsonObject.ToString(Formatting.None);
        }

        public static RpcMessage LoadDb(string filename)
        {
            dynamic args = new JObject();
            args.file = filename;
            return new RpcMessage("loaddb", args);
        }

        public static RpcMessage LoadDbFromHexString(string dataString)
        {
            dynamic args = new JObject();
            args.data = dataString;
            return new RpcMessage("loaddbStream", args);
        }

        private static void ValidateAddress(string address, string param)
        {
            if (!Nethereum.Util.AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
                throw new ArgumentException("Not a valid ethereum address.", param);
        }

        public static RpcMessage Signup(string user, string token)
        {
            ValidateAddress(user, nameof(user));
            ValidateAddress(token, nameof(token));
            
            dynamic args = new JObject();
            args.user = user;
            args.token = token;
            return new RpcMessage("signup", args);
        }

        public static RpcMessage OrganizationSignup(string organization)
        {
            ValidateAddress(organization, nameof(organization));
            
            dynamic args = new JObject();
            args.organization = organization;
            return new RpcMessage("organizationSignup", args);
        }

        public static RpcMessage Trust(string canSendTo, string user, int limitPercentage)
        {
            ValidateAddress(canSendTo, nameof(canSendTo));
            ValidateAddress(user, nameof(user));
            
            dynamic args = new JObject();
            args.canSendTo = canSendTo;
            args.user = user;
            args.limitPercentage = limitPercentage;
            return new RpcMessage("trust", args);
        }

        public static RpcMessage Transfer(string token, string from, string to, string value)
        {
            ValidateAddress(token, nameof(token));
            ValidateAddress(from, nameof(from));
            ValidateAddress(to, nameof(to));
            
            dynamic args = new JObject();
            args.token = token;
            args.from = from;
            args.to = to;
            args.value = value;
            
            return new RpcMessage("transfer", args);
        }

        public static RpcMessage EdgeCount()
        {
            return new RpcMessage("edgeCount", new JObject());
        }

        public static RpcMessage DelayEdgeUpdates()
        {
            return new RpcMessage("delayEdgeUpdates", new JObject());
        }

        public static RpcMessage PerformEdgeUpdates()
        {
            return new RpcMessage("performEdgeUpdates", new JObject());
        }

        public static RpcMessage Adjacencies(string user)
        {
            ValidateAddress(user, nameof(user));
            
            dynamic args = new JObject();
            args.user = user;
            
            return new RpcMessage("adjacencies", args);
        }

        public static RpcMessage Flow(string from, string to, string value)
        {
            ValidateAddress(from, nameof(from));
            ValidateAddress(to, nameof(to));
            
            dynamic args = new JObject();
            args.from = from;
            args.to = to;
            args.value = value;
            
            return new RpcMessage("flow", args);
        }
    }
}