using Editor;

namespace RopEditor;

internal static class RopModelMenu
{
	[Event( "folder.contextmenu", Priority = 60 )]
	public static void OnFolderContextMenu( FolderContextMenu e )
	{
		if ( e.ThisFolder || !RopModelBuilder.CanBuild( e.Target ) )
			return;

		e.Menu.AddOption( "Create RoP Model", "view_in_ar", () =>
		{
			var result = RopModelBuilder.Build( e.Target );

			EditorUtility.DisplayDialog( "Create RoP Model", result );
		} );
	}
}
