using System;

namespace HappyWorld.Core.Api
{
    /// <summary>Error returned by the backend as { error: { code, message } }, or a network failure (Status 0).</summary>
    public sealed class ApiException : Exception
    {
        public int Status { get; }
        /// <summary>Stable code from packages/shared-contracts/src/errors.ts, e.g. INSUFFICIENT_FUNDS.</summary>
        public string Code { get; }
        public string RequestId { get; }

        public ApiException(int status, string code, string message, string requestId = null) : base(message)
        {
            Status = status;
            Code = code;
            RequestId = requestId;
        }

        public bool IsNetworkError => Status == 0;
    }
}
