using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/app")]
public class AppUpdateController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public AppUpdateController(IWebHostEnvironment env)
    {
        _env = env;
    }

    
    [HttpGet("latest")]
    public ActionResult<AppLatestResponse> Latest()
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var apkDir = Path.Combine(webRoot, "apk");

        
        var apkPath = Path.Combine(apkDir, "app-latest.bin");
        if (!System.IO.File.Exists(apkPath))
        {
            return NotFound(new AppLatestResponse(0, null, "APK file not found: wwwroot/apk/app-latest.bin"));
        }

        var versionFile = Path.Combine(apkDir, "latest_version.txt");
        var versionCode = 0;
        if (System.IO.File.Exists(versionFile))
        {
            var s = System.IO.File.ReadAllText(versionFile).Trim();
            int.TryParse(s, out versionCode);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
        var apkUrl = $"{baseUrl}/api/app/download";

        return Ok(new AppLatestResponse(versionCode, apkUrl, "OK"));
    }

    
    [HttpGet("download")]
    public IActionResult Download()
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var apkPath = Path.Combine(webRoot, "apk", "app-latest.bin");
        if (!System.IO.File.Exists(apkPath))
        {
            return NotFound("APK file not found: wwwroot/apk/app-latest.bin");
        }

        var stream = new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/vnd.android.package-archive", "app-latest.apk");
    }
}

public record AppLatestResponse(int VersionCode, string? ApkUrl, string Message);

