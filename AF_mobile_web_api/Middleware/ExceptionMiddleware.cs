using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace AF_mobile_web_api.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away (closed the tab, navigated on, a proxy gave up) and the
                // work was cancelled with it. Not a server fault, so not an error in the logs;
                // and nobody is left to read a JSON body - writing one to the aborted
                // connection would only fail again.
                _logger.LogDebug("Request {Method} {Path} aborted by the client", context.Request.Method, context.Request.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            // validation errors (e.g. unknown city name) are the caller's fault -> 400 with the real message
            var isValidationError = exception is ArgumentException;
            context.Response.StatusCode = isValidationError
                ? (int)HttpStatusCode.BadRequest
                : (int)HttpStatusCode.InternalServerError;

            var message = isValidationError || _env.IsDevelopment() ? exception.Message : "Internal Server Error";
            var stackTrace = _env.IsDevelopment() ? exception.StackTrace : "";

            var response = new { StatusCode = context.Response.StatusCode, Message = message, StackTrace = stackTrace };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(response, options);

            await context.Response.WriteAsync(json);
        }
    }
}
