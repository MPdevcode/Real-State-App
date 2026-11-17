using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace RealEstateApp.WebApi.Errors;

internal static class ApiProblem
{
    public static async Task WriteAsync(HttpContext context, int status, string? detail = null)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        var writer = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await writer.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
        {
            // Error responses remain JSON even when Accept excludes the default problem writer.
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
