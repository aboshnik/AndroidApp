using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace EmployeeApi.Services;

/// <summary>
/// После конвейера: фиксирует HTTP 5xx; при исключении, дошедшем до этого уровня — отдельная запись.
/// </summary>
public sealed class ServerDiagnosticsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
            if (context.Response.StatusCode >= 500)
            {
                string? detail = null;
                var feat = context.Features.Get<IExceptionHandlerFeature>();
                if (feat?.Error != null)
                    detail = $"{feat.Error.GetType().Name}: {feat.Error.Message}";
                ServerDiagnosticsBuffer.RecordHttp500(context, context.Response.StatusCode, detail);
            }
        }
        catch (Exception ex)
        {
            ServerDiagnosticsBuffer.RecordUnhandled("Необработанное исключение", ex, context);
            throw;
        }
    }
}
