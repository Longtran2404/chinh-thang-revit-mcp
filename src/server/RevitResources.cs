// Modified for Chinh Thang Revit MCP, 2026-09-18. See FORK_CHANGES.md.
using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;
using RvtMcp.Server.Memory;

namespace RvtMcp.Server
{
    [McpServerResourceType]
    public class RevitResources
    {
        internal static SessionContext Session { get; set; }

        [McpServerResource(UriTemplate = "revit://guidance/native-modeling", Name = "Chinh Thang Native BIM Policy", MimeType = "text/markdown")]
        [Description("Required modeling guidance for editable Revit walls, doors, furniture families, native details, materials and image textures. Read before modeling interiors.")]
        public static string GetNativeModelingPolicy()
        {
            using var stream = typeof(RevitResources).Assembly.GetManifestResourceStream("ChinhThang.NativeModelingPolicy.md");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [McpServerResource(UriTemplate = "revit://session/context", Name = "Session Context", MimeType = "application/json")]
        [Description("Current MCP session context: tool call history, success rates, usage patterns, and flags. Use this to understand what has been done in this session.")]
        public static string GetSessionContext()
        {
            return Session?.GetSummary() ?? "{\"status\": \"no session\"}";
        }
    }
}
