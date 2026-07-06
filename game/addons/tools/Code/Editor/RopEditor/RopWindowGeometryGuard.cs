using Editor;
using Sandbox;
using Sandbox.Internal;

namespace RopEditor;

// The editor persists its window geometry via Qt saveGeometry on exit. A maximized-state blob can restore
// broken (off-screen / oversized normal rect) after a resolution, DPI, or monitor change, leaving the main
// window unable to maximize. Dropping the geometry cookie on a maximized exit sidesteps that: the next launch
// has no geometry to restore, so EditorMainWindow centers fresh and maximizes cleanly. Windowed sessions keep
// their saved position. This runs after the engine's own app.exit save (which writes the cookie), and before
// ToolsDll flushes the cookie cache to disk.
internal static class RopWindowGeometryGuard
{
	[Event( "app.exit", Priority = 100 )]
	public static void DropMaximizedGeometry()
	{
		var window = GlobalToolsNamespace.EditorWindow;

		if ( !window.IsValid() || !window.IsMaximized )
			return;

		GlobalToolsNamespace.EditorCookie.Remove( $"Window.{window.StateCookie}.Geometry" );
	}
}
