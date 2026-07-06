using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Editor;

namespace RopEditor;

// Surfaces the game's RoP GameResources under a single "New RoP Resource" submenu with proper nesting,
// and prunes the flat "_rop/*" category entries the engine's stock "New" menu would otherwise show.
internal static class RopResourceMenu
{
	const string Marker = "_rop/";

	static readonly FieldInfo MenusField = typeof( Menu ).GetField( "Menus", BindingFlags.Instance | BindingFlags.NonPublic );
	static readonly MethodInfo RemoveFromParent = typeof( Menu ).GetMethod( "RemoveFromParent", BindingFlags.Instance | BindingFlags.NonPublic );

	[Event( "folder.contextmenu", Priority = -50 )]
	public static void AddRopResourceMenu( FolderContextMenu e )
	{
		if ( !IsAssetsFolder( e.Target ) )
			return;

		var resources = RopResources();

		if ( resources.Count == 0 )
			return;

		BuildRopMenu( e.Menu, e.Target, resources );
	}

	[Event( "folder.contextmenu", Priority = 20 )]
	public static void PruneStockNew( FolderContextMenu e )
	{
		if ( !IsAssetsFolder( e.Target ) )
			return;

		try
		{
			var stockNew = Submenus( e.Menu ).FirstOrDefault( m => m.IsValid && string.Equals( m.Title, "New", StringComparison.OrdinalIgnoreCase ) );

			if ( stockNew is null || MenusField?.GetValue( stockNew ) is not IList<Menu> branches )
				return;

			var rop = branches
				.Where( b => b.IsValid && b.Title is not null && b.Title.StartsWith( Marker, StringComparison.OrdinalIgnoreCase ) )
				.ToList();

			foreach ( var branch in rop )
			{
				RemoveFromParent?.Invoke( branch, null );

				branches.Remove( branch );
			}
		}
		catch ( Exception )
		{
		}
	}

	static void BuildRopMenu( Menu parent, DirectoryInfo folder, List<AssetTypeAttribute> resources )
	{
		var root = parent.AddMenu( "New RoP Resource", "note_add" );

		foreach ( var resource in resources )
		{
			var branch = resource.Category[Marker.Length..].Split( '/', StringSplitOptions.RemoveEmptyEntries );

			var leaf = branch.Aggregate( root, ( menu, segment ) => menu.FindOrCreateMenu( segment ) );

			var icon = AssetType.FromType( resource.TargetType )?.Icon64;

			if ( icon is not null )
				leaf.AddOptionWithImage( resource.Name, icon, () => CreateAsset.CreateGameResource( resource, folder ) );
			else
				leaf.AddOption( resource.Name, action: () => CreateAsset.CreateGameResource( resource, folder ) );
		}
	}

	static List<AssetTypeAttribute> RopResources()
	{
		return EditorTypeLibrary.GetAttributes<AssetTypeAttribute>()
			.Where( x => x.Category is not null && x.Category.StartsWith( Marker, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Category )
			.ThenBy( x => x.Name )
			.ToList();
	}

	static bool IsAssetsFolder( DirectoryInfo folder )
	{
		return folder is not null && new DiskLocation( folder ).Type is LocalAssetBrowser.LocationType.Assets;
	}

	static List<Menu> Submenus( Menu menu )
	{
		if ( MenusField?.GetValue( menu ) is IEnumerable<Menu> list )
			return list.ToList();

		return new List<Menu>();
	}
}
