namespace ServerTest.Protocol
{
    public static class ProtocolErrorCodes
    {
        public const int Ok = 0;

        // 1xxx 参数/格式
        public const int InvalidRequest = 1000;
        public const int MissingField = 1001;
        public const int InvalidFormat = 1002;
        public const int OutOfRange = 1003;
        public const int Unsupported = 1004;

        // 2xxx 鉴权/权限
        public const int Unauthorized = 2000;
        public const int TokenInvalid = 2001;
        public const int Forbidden = 2002;

        // 3xxx 业务
        public const int NotFound = 3000;
        public const int Conflict = 3001;
        public const int StateInvalid = 3002;
        public const int LimitExceeded = 3003;
        public const int RateLimited = 3004;

        // 5xxx 系统/依赖
        public const int InternalError = 5000;
        public const int DependencyError = 5001;
        public const int Timeout = 5002;
        public const int ServiceUnavailable = 5003;
    }
}
