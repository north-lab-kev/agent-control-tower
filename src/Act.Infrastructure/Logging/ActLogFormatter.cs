using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace Act.Infrastructure.Logging;

public sealed class ActLogFormatter : ITextFormatter
{
    private static readonly string[] RenderedElsewhere = ["SourceContext", "EventId"];

    private readonly MessageTemplateTextFormatter prefix =
        new("{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} {SourceContext} ");

    private readonly MessageTemplateTextFormatter body = new("{Message:l}{NewLine}{Exception}");

    public void Format(LogEvent logEvent, TextWriter output)
    {
        prefix.Format(logEvent, output);

        WriteContext(logEvent, output);

        body.Format(logEvent, output);
    }

    private static void WriteContext(LogEvent logEvent, TextWriter output)
    {
        var spoken = logEvent.MessageTemplate.Tokens
            .OfType<PropertyToken>()
            .Select(token => token.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

        var context = logEvent.Properties
            .Where(property => !RenderedElsewhere.Contains(property.Key))
            .Where(property => !spoken.Contains(property.Key))
            .ToList();

        if (context.Count == 0)
            return;

        output.Write('[');

        for (var index = 0; index < context.Count; index++)
        {
            if (index > 0)
                output.Write(' ');

            output.Write(context[index].Key);
            output.Write('=');

            WriteValue(context[index].Value, output);
        }

        output.Write("] ");
    }

    private static void WriteValue(LogEventPropertyValue value, TextWriter output)
    {
        if (value is ScalarValue { Value: string text })
            output.Write(text);
        else
            output.Write(value.ToString());
    }
}
