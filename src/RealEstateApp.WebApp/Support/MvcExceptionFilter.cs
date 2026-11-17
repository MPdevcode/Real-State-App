using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Support;

public sealed class MvcExceptionFilter(IModelMetadataProvider metadata, ILogger<MvcExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.HttpContext.Response.HasStarted || context.Exception is OperationCanceledException) return;
        var code = context.Exception switch
        {
            NotFoundException => 404, ForbiddenException => 403, AuthenticationException => 401,
            BusinessException => 400, _ => 500
        };
        if (code == 500) logger.LogError(context.Exception, "MVC request {RequestId} failed", context.HttpContext.TraceIdentifier);
        var model = new ErrorViewModel
        {
            StatusCode = code, RequestId = context.HttpContext.TraceIdentifier,
            Message = context.Exception is BusinessException ? context.Exception.Message : "No fue posible procesar la solicitud. Intente nuevamente más tarde."
        };
        context.Result = new ViewResult
        {
            ViewName = "Error", StatusCode = code,
            ViewData = new ViewDataDictionary<ErrorViewModel>(metadata, context.ModelState) { Model = model }
        };
        context.ExceptionHandled = true;
    }
}
