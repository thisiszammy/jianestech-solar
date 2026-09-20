using Microsoft.AspNetCore.Http;
using JianeTech.Services.Interfaces;

namespace JianeTech.Services
{
    public class HttpClientContextAccessor : IClientContextAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpClientContextAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? IpAddress
            => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        public string? UserAgent
            => _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString();
    }
}
