namespace ServerTest.Models
{
    public class ErrorResponse
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string? TraceId { get; set; }

        public static ErrorResponse Create(string code, string message, string? traceId)
        {
            return new ErrorResponse
            {
                Code = code,
                Message = message,
                TraceId = traceId
            };
        }
    }
}
