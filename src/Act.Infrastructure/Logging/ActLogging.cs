using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Act.Infrastructure.Logging;

public static class ActLogging
{
    private const string FileNameTemplate = "act-.log";

    private const int RetainedFiles = 14;

    private const long FileSizeLimitBytes = 32L * 1024 * 1024;

    // The BOM is the point. A localised message reaches this file — the queue logs the sentence it
    // showed the user — and so do paths under an accented profile name, and without a BOM every
    // Windows reader that defaults to the ANSI codepage (PowerShell 5.1's `Get-Content`, older
    // editors) turns those into mojibake.
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static ILoggingBuilder AddActFileLog(this ILoggingBuilder logging, string dataDirectory)
    {
        var directory = ActLogDirectory.Resolve(dataDirectory);

        Directory.CreateDirectory(directory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                new ActLogFormatter(),
                Path.Combine(directory, FileNameTemplate),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: true,
                encoding: Utf8WithBom)
            .CreateLogger();

        logging.Services.AddSingleton(new ActLogLocation(directory));
        logging.Services.AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(logger, dispose: true));

        return logging;
    }
}
