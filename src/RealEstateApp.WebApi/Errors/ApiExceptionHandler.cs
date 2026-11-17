using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RealEstateApp.Domain;

namespace RealEstateApp.WebApi.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            AuthenticationException => StatusCodes.Status401Unauthorized,
            ForbiddenException => StatusCodes.Status403Forbidden,
            NotFoundException => StatusCodes.Status404NotFound,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            BusinessException or ValidationException => StatusCodes.Status400BadRequest,
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled API error. TraceId: {TraceId}", httpContext.TraceIdentifier);

        var detail = status switch
        {
            >= StatusCodes.Status500InternalServerError => "Ocurrió un error inesperado. Use el traceId para reportarlo.",
            StatusCodes.Status409Conflict => "Los datos cambiaron durante la operación. Actualice e intente nuevamente.",
            _ => exception.Message
        };
        if (status == StatusCodes.Status401Unauthorized)
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        await ApiProblem.WriteAsync(httpContext, status, detail);
        return true;
    }
}
