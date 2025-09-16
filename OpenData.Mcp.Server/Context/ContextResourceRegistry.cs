using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenData.Mcp.Server.Context;

internal static class ContextResourceRegistry
{
    private const string MimeType = "application/json";

    public static IReadOnlyList<McpServerResource> LoadResources(string contextDirectory)
    {
        if (!Directory.Exists(contextDirectory))
        {
            return Array.Empty<McpServerResource>();
        }

        var files = Directory.GetFiles(contextDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            return Array.Empty<McpServerResource>();
        }

        var resources = new List<McpServerResource>(files.Length);
        foreach (var file in files)
        {
            resources.Add(CreateResource(file));
        }

        return resources;
    }

    private static McpServerResource CreateResource(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var uri = $"context/{fileName}";
        var options = new McpServerResourceCreateOptions
        {
            Name = $"context:{fileName}",
            Title = ToDisplayName(fileName),
            Description = $"Official UK Parliament context for {fileName} API.",
            UriTemplate = uri,
            MimeType = MimeType
        };

        return McpServerResource.Create(
            async (RequestContext<ReadResourceRequestParams> _, CancellationToken cancellationToken) =>
            {
                var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                return new ReadResourceResult
                {
                    Contents =
                    [
                        new TextResourceContents
                        {
                            MimeType = MimeType,
                            Text = text,
                            Uri = uri
                        }
                    ]
                };
            },
            options);
    }

    private static string ToDisplayName(string fileName)
    {
        var text = fileName.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }
}
