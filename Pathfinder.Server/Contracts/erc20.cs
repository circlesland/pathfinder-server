using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Pathfinder.Server.contracts
{
    public partial class ERC20Deployment : ERC20DeploymentBase
    {
        public ERC20Deployment() : base(BYTECODE)
        {
        }

        public ERC20Deployment(string byteCode) : base(byteCode)
        {
        }
    }

    public class ERC20DeploymentBase : ContractDeploymentMessage
    {
        public static string BYTECODE =
            "0x608060405234801561001057600080fd5b5061090b806100206000396000f3fe608060405234801561001057600080fd5b506004361061008e5760003560e01c8063095ea7b31461009357806318160ddd146100d357806323b872dd146100ed578063313ce56714610123578063395093511461014157806370a082311461016d57806395d89b4114610193578063a457c2d714610210578063a9059cbb1461023c578063dd62ed3e14610268575b600080fd5b6100bf600480360360408110156100a957600080fd5b506001600160a01b038135169060200135610296565b604080519115158252519081900360200190f35b6100db6102ac565b60408051918252519081900360200190f35b6100bf6004803603606081101561010357600080fd5b506001600160a01b038135811691602081013590911690604001356102b2565b61012b61031b565b6040805160ff9092168252519081900360200190f35b6100bf6004803603604081101561015757600080fd5b506001600160a01b038135169060200135610324565b6100db6004803603602081101561018357600080fd5b50356001600160a01b031661035a565b61019b610375565b6040805160208082528351818301528351919283929083019185019080838360005b838110156101d55781810151838201526020016101bd565b50505050905090810190601f1680156102025780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6100bf6004803603604081101561022657600080fd5b506001600160a01b03813516906020013561040b565b6100bf6004803603604081101561025257600080fd5b506001600160a01b03813516906020013561045a565b6100db6004803603604081101561027e57600080fd5b506001600160a01b0381358116916020013516610467565b60006102a3338484610492565b50600192915050565b60025490565b60006102bf84848461057e565b610311843361030c85604051806060016040528060288152602001610840602891396001600160a01b038a16600090815260016020908152604080832033845290915290205491906106d9565b610492565b5060019392505050565b60045460ff1690565b3360008181526001602090815260408083206001600160a01b038716845290915281205490916102a391859061030c9086610770565b6001600160a01b031660009081526020819052604090205490565b60038054604080516020601f60026000196101006001881615020190951694909404938401819004810282018101909252828152606093909290918301828280156104015780601f106103d657610100808354040283529160200191610401565b820191906000526020600020905b8154815290600101906020018083116103e457829003601f168201915b5050505050905090565b60006102a3338461030c856040518060600160405280602581526020016108b1602591393360009081526001602090815260408083206001600160a01b038d16845290915290205491906106d9565b60006102a333848461057e565b6001600160a01b03918216600090815260016020908152604080832093909416825291909152205490565b6001600160a01b0383166104d75760405162461bcd60e51b815260040180806020018281038252602481526020018061088d6024913960400191505060405180910390fd5b6001600160a01b03821661051c5760405162461bcd60e51b81526004018080602001828103825260228152602001806107f86022913960400191505060405180910390fd5b6001600160a01b03808416600081815260016020908152604080832094871680845294825291829020859055815185815291517f8c5be1e5ebec7d5bd14f71427d1e84f3dd0314c0f7b2291e5b200ac8c7c3b9259281900390910190a3505050565b6001600160a01b0383166105c35760405162461bcd60e51b81526004018080602001828103825260258152602001806108686025913960400191505060405180910390fd5b6001600160a01b0382166106085760405162461bcd60e51b81526004018080602001828103825260238152602001806107d56023913960400191505060405180910390fd5b6106138383836107cf565b6106508160405180606001604052806026815260200161081a602691396001600160a01b03861660009081526020819052604090205491906106d9565b6001600160a01b03808516600090815260208190526040808220939093559084168152205461067f9082610770565b6001600160a01b038084166000818152602081815260409182902094909455805185815290519193928716927fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef92918290030190a3505050565b600081848411156107685760405162461bcd60e51b81526004018080602001828103825283818151815260200191508051906020019080838360005b8381101561072d578181015183820152602001610715565b50505050905090810190601f16801561075a5780820380516001836020036101000a031916815260200191505b509250505060405180910390fd5b505050900390565b6000828201838110156107c8576040805162461bcd60e51b815260206004820152601b60248201527a536166654d6174683a206164646974696f6e206f766572666c6f7760281b604482015290519081900360640190fd5b9392505050565b50505056fe45524332303a207472616e7366657220746f20746865207a65726f206164647265737345524332303a20617070726f766520746f20746865207a65726f206164647265737345524332303a207472616e7366657220616d6f756e7420657863656564732062616c616e636545524332303a207472616e7366657220616d6f756e74206578636565647320616c6c6f77616e636545524332303a207472616e736665722066726f6d20746865207a65726f206164647265737345524332303a20617070726f76652066726f6d20746865207a65726f206164647265737345524332303a2064656372656173656420616c6c6f77616e63652062656c6f77207a65726fa264697066735822122029606cd418e556f4bf720164075fc2d074c28bc6182ac01a38a7aacd862fe57664736f6c63430007010033";

        public ERC20DeploymentBase() : base(BYTECODE)
        {
        }

        public ERC20DeploymentBase(string byteCode) : base(byteCode)
        {
        }
    }

    public partial class DecimalsFunction : DecimalsFunctionBase
    {
    }

    [Function("decimals", "uint8")]
    public class DecimalsFunctionBase : FunctionMessage
    {
    }

    public partial class TotalSupplyFunction : TotalSupplyFunctionBase
    {
    }

    [Function("totalSupply", "uint256")]
    public class TotalSupplyFunctionBase : FunctionMessage
    {
    }

    public partial class BalanceOfFunction : BalanceOfFunctionBase
    {
    }

    [Function("balanceOf", "uint256")]
    public class BalanceOfFunctionBase : FunctionMessage
    {
        [Parameter("address", "account", 1)] public virtual string Account { get; set; }
    }

    public partial class TransferFunction : TransferFunctionBase
    {
    }

    [Function("transfer", "bool")]
    public class TransferFunctionBase : FunctionMessage
    {
        [Parameter("address", "recipient", 1)] public virtual string Recipient { get; set; }
        [Parameter("uint256", "amount", 2)] public virtual BigInteger Amount { get; set; }
    }

    public partial class AllowanceFunction : AllowanceFunctionBase
    {
    }

    [Function("allowance", "uint256")]
    public class AllowanceFunctionBase : FunctionMessage
    {
        [Parameter("address", "owner", 1)] public virtual string Owner { get; set; }
        [Parameter("address", "spender", 2)] public virtual string Spender { get; set; }
    }

    public partial class ApproveFunction : ApproveFunctionBase
    {
    }

    [Function("approve", "bool")]
    public class ApproveFunctionBase : FunctionMessage
    {
        [Parameter("address", "spender", 1)] public virtual string Spender { get; set; }
        [Parameter("uint256", "amount", 2)] public virtual BigInteger Amount { get; set; }
    }

    public partial class TransferFromFunction : TransferFromFunctionBase
    {
    }

    [Function("transferFrom", "bool")]
    public class TransferFromFunctionBase : FunctionMessage
    {
        [Parameter("address", "sender", 1)] public virtual string Sender { get; set; }
        [Parameter("address", "recipient", 2)] public virtual string Recipient { get; set; }
        [Parameter("uint256", "amount", 3)] public virtual BigInteger Amount { get; set; }
    }

    public partial class IncreaseAllowanceFunction : IncreaseAllowanceFunctionBase
    {
    }

    [Function("increaseAllowance", "bool")]
    public class IncreaseAllowanceFunctionBase : FunctionMessage
    {
        [Parameter("address", "spender", 1)] public virtual string Spender { get; set; }

        [Parameter("uint256", "addedValue", 2)]
        public virtual BigInteger AddedValue { get; set; }
    }

    public partial class DecreaseAllowanceFunction : DecreaseAllowanceFunctionBase
    {
    }

    [Function("decreaseAllowance", "bool")]
    public class DecreaseAllowanceFunctionBase : FunctionMessage
    {
        [Parameter("address", "spender", 1)] public virtual string Spender { get; set; }

        [Parameter("uint256", "subtractedValue", 2)]
        public virtual BigInteger SubtractedValue { get; set; }
    }

    public partial class ApprovalEventDTO : ApprovalEventDTOBase
    {
    }

    [Event("Approval")]
    public class ApprovalEventDTOBase : IEventDTO
    {
        [Parameter("address", "owner", 1, true)]
        public virtual string Owner { get; set; }

        [Parameter("address", "spender", 2, true)]
        public virtual string Spender { get; set; }

        [Parameter("uint256", "value", 3, false)]
        public virtual BigInteger Value { get; set; }
    }

    public partial class TransferEventDTO : TransferEventDTOBase
    {
    }

    [Event("Transfer")]
    public class TransferEventDTOBase : IEventDTO
    {
        [Parameter("address", "from", 1, true)]
        public virtual string From { get; set; }

        [Parameter("address", "to", 2, true)] public virtual string To { get; set; }

        [Parameter("uint256", "value", 3, false)]
        public virtual BigInteger Value { get; set; }
    }

    public partial class DecimalsOutputDTO : DecimalsOutputDTOBase
    {
    }

    [FunctionOutput]
    public class DecimalsOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint8", "", 1)] public virtual byte ReturnValue1 { get; set; }
    }

    public partial class TotalSupplyOutputDTO : TotalSupplyOutputDTOBase
    {
    }

    [FunctionOutput]
    public class TotalSupplyOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint256", "", 1)] public virtual BigInteger ReturnValue1 { get; set; }
    }

    public partial class BalanceOfOutputDTO : BalanceOfOutputDTOBase
    {
    }

    [FunctionOutput]
    public class BalanceOfOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint256", "", 1)] public virtual BigInteger ReturnValue1 { get; set; }
    }


    public partial class AllowanceOutputDTO : AllowanceOutputDTOBase
    {
    }

    [FunctionOutput]
    public class AllowanceOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint256", "", 1)] public virtual BigInteger ReturnValue1 { get; set; }
    }
}