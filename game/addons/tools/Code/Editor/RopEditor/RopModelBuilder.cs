using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Editor;

namespace RopEditor;

// One-click model assembly for a folder that already holds a models/ mesh and a textures/ set:
// bakes a rop_psx material from the color texture, then writes a centimeter-scaled vmdl with hull collision.
public static class RopModelBuilder
{
	const string DocHeader = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->";

	const string Shader = "shaders/rop_psx.shader";

	const float CentimeterScale = 0.3937f;

	const string FlatColor = "[1.000000 1.000000 1.000000 0.000000]";
	const string FlatNormal = "[0.501961 0.501961 1.000000 0.000000]";
	const string FlatRoughness = "[0.700000 0.700000 0.700000 0.000000]";
	const string FlatAmbientOcclusion = "[1.000000 1.000000 1.000000 0.000000]";
	const string FlatMetalness = "[0.000000 0.000000 0.000000 0.000000]";

	static readonly string[] PbrChannels = { "color", "normal", "rough", "metal", "ao" };

	static readonly string[] MeshExtensions = { ".fbx", ".gltf", ".glb", ".obj", ".dmx", ".smd", ".ply" };

	public static bool CanBuild( DirectoryInfo folder )
	{
		return folder is not null && folder.Exists && FindMesh( folder ) is not null && TexturesDir( folder ) is not null;
	}

	public static string Build( DirectoryInfo folder )
	{
		var mesh = FindMesh( folder );

		if ( mesh is null )
			return "No mesh found under models/.";

		var textures = TexturesDir( folder );

		if ( textures is null )
			return "No textures/ folder found.";

		var name = folder.Name;

		var materialPath = BuildMaterial( textures, name );
		var modelPath = WriteModel( folder, name, mesh, materialPath );

		var asset = AssetSystem.RegisterFile( modelPath );
		asset?.Compile( false );

		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		if ( asset is not null )
			MainAssetBrowser.Instance?.Local.FocusOnAsset( asset );

		return $"Built {name}.vmdl";
	}

	static string BuildMaterial( DirectoryInfo textures, string name )
	{
		var channels = ScanChannels( textures );

		var slots = new Dictionary<string, string>
		{
			["TextureColor"] = channels.GetValueOrDefault( "color", FlatColor ),
			["TextureNormal"] = channels.GetValueOrDefault( "normal", FlatNormal ),
			["TextureRoughness"] = channels.GetValueOrDefault( "rough", FlatRoughness ),
			["TextureAmbientOcclusion"] = channels.GetValueOrDefault( "ao", FlatAmbientOcclusion ),
			["TextureMetalness"] = channels.GetValueOrDefault( "metal", FlatMetalness ),
		};

		var vmat = Join( Normalize( textures.FullName ), name + ".vmat" );

		WriteMaterial( vmat, slots );

		return ResourcePath( vmat );
	}

	static Dictionary<string, string> ScanChannels( DirectoryInfo textures )
	{
		var found = new Dictionary<string, string>();

		var images = textures
			.EnumerateFiles( "*.*", SearchOption.TopDirectoryOnly )
			.Where( file => IsImage( file.Extension ) )
			.ToList();

		foreach ( var image in images )
		{
			var channel = ChannelOf( image.FullName );

			if ( channel is null || found.ContainsKey( channel ) )
				continue;

			AssetSystem.RegisterFile( image.FullName );

			found[channel] = ResourcePath( image.FullName );
		}

		if ( !found.ContainsKey( "color" ) )
		{
			var fallback = images.FirstOrDefault( image => image.Name.Contains( "color" ) || image.Name.Contains( "diff" ) )
				?? images.FirstOrDefault();

			if ( fallback is not null )
			{
				AssetSystem.RegisterFile( fallback.FullName );

				found["color"] = ResourcePath( fallback.FullName );
			}
		}

		return found;
	}

	static void WriteMaterial( string vmatAbsolute, Dictionary<string, string> slots )
	{
		var text = new StringBuilder();

		text.Append( "// THIS FILE IS AUTO-GENERATED\n\n" );
		text.Append( "Layer0\n{\n" );
		text.Append( $"\tshader \"{Shader}\"\n\n" );
		text.Append( "\t//---- Fog ----\n\tg_bFogEnabled \"1\"\n\n" );
		text.Append( "\t//---- Material ----\n" );
		text.Append( "\tg_flTintColor \"[1.000000 1.000000 1.000000 0.000000]\"\n" );
		text.Append( $"\tTextureAmbientOcclusion \"{slots["TextureAmbientOcclusion"]}\"\n" );
		text.Append( $"\tTextureColor \"{slots["TextureColor"]}\"\n" );
		text.Append( $"\tTextureMetalness \"{slots["TextureMetalness"]}\"\n" );
		text.Append( $"\tTextureNormal \"{slots["TextureNormal"]}\"\n" );
		text.Append( $"\tTextureRoughness \"{slots["TextureRoughness"]}\"\n" );
		text.Append( "}\n" );

		File.WriteAllText( vmatAbsolute, text.ToString() );

		AssetSystem.RegisterFile( vmatAbsolute )?.Compile( false );
	}

	static string WriteModel( DirectoryInfo folder, string name, FileInfo mesh, string materialPath )
	{
		var meshRef = ResourcePath( Normalize( mesh.FullName ) );

		var text = new StringBuilder();

		text.AppendLine( DocHeader );
		text.AppendLine( "{" );
		text.AppendLine( "\trootNode =" );
		text.AppendLine( "\t{" );
		text.AppendLine( "\t\t_class = \"RootNode\"" );
		text.AppendLine( "\t\tchildren =" );
		text.AppendLine( "\t\t[" );

		AppendMaterialGroup( text, materialPath );
		AppendPhysicsHull( text );
		AppendRenderMesh( text, meshRef );
		AppendScaleModifier( text );

		text.AppendLine( "\t\t]" );
		text.AppendLine( "\t\tmodel_archetype = \"\"" );
		text.AppendLine( "\t\tprimary_associated_entity = \"\"" );
		text.AppendLine( "\t\tanim_graph_name = \"\"" );
		text.AppendLine( "\t\tbase_model_name = \"\"" );
		text.AppendLine( "\t}" );
		text.AppendLine( "}" );

		var vmdl = Join( Normalize( folder.FullName ), name + ".vmdl" );

		File.WriteAllText( vmdl, text.ToString() );

		return vmdl;
	}

	static void AppendMaterialGroup( StringBuilder text, string materialPath )
	{
		text.AppendLine( "\t\t\t{" );
		text.AppendLine( "\t\t\t\t_class = \"MaterialGroupList\"" );
		text.AppendLine( "\t\t\t\tchildren =" );
		text.AppendLine( "\t\t\t\t[" );
		text.AppendLine( "\t\t\t\t\t{" );
		text.AppendLine( "\t\t\t\t\t\t_class = \"DefaultMaterialGroup\"" );
		text.AppendLine( "\t\t\t\t\t\tremaps = [  ]" );
		text.AppendLine( "\t\t\t\t\t\tuse_global_default = true" );
		text.AppendLine( $"\t\t\t\t\t\tglobal_default_material = \"{materialPath}\"" );
		text.AppendLine( "\t\t\t\t\t}," );
		text.AppendLine( "\t\t\t\t]" );
		text.AppendLine( "\t\t\t}," );
	}

	static void AppendPhysicsHull( StringBuilder text )
	{
		text.AppendLine( "\t\t\t{" );
		text.AppendLine( "\t\t\t\t_class = \"PhysicsShapeList\"" );
		text.AppendLine( "\t\t\t\tchildren =" );
		text.AppendLine( "\t\t\t\t[" );
		text.AppendLine( "\t\t\t\t\t{" );
		text.AppendLine( "\t\t\t\t\t\t_class = \"PhysicsHullFromRender\"" );
		text.AppendLine( "\t\t\t\t\t\tparent_bone = \"\"" );
		text.AppendLine( "\t\t\t\t\t\tsurface_prop = \"default\"" );
		text.AppendLine( "\t\t\t\t\t\tcollision_tags = \"solid\"" );
		text.AppendLine( "\t\t\t\t\t}," );
		text.AppendLine( "\t\t\t\t]" );
		text.AppendLine( "\t\t\t}," );
	}

	static void AppendRenderMesh( StringBuilder text, string meshRef )
	{
		text.AppendLine( "\t\t\t{" );
		text.AppendLine( "\t\t\t\t_class = \"RenderMeshList\"" );
		text.AppendLine( "\t\t\t\tchildren =" );
		text.AppendLine( "\t\t\t\t[" );
		text.AppendLine( "\t\t\t\t\t{" );
		text.AppendLine( "\t\t\t\t\t\t_class = \"RenderMeshFile\"" );
		text.AppendLine( $"\t\t\t\t\t\tfilename = \"{meshRef}\"" );
		text.AppendLine( "\t\t\t\t\t\timport_translation = [ 0.0, 0.0, 0.0 ]" );
		text.AppendLine( "\t\t\t\t\t\timport_rotation = [ 0.0, 0.0, 0.0 ]" );
		text.AppendLine( "\t\t\t\t\t\timport_scale = 1.0" );
		text.AppendLine( "\t\t\t\t\t\talign_origin_x_type = \"None\"" );
		text.AppendLine( "\t\t\t\t\t\talign_origin_y_type = \"None\"" );
		text.AppendLine( "\t\t\t\t\t\talign_origin_z_type = \"None\"" );
		text.AppendLine( "\t\t\t\t\t\tparent_bone = \"\"" );
		text.AppendLine( "\t\t\t\t\t}," );
		text.AppendLine( "\t\t\t\t]" );
		text.AppendLine( "\t\t\t}," );
	}

	static void AppendScaleModifier( StringBuilder text )
	{
		text.AppendLine( "\t\t\t{" );
		text.AppendLine( "\t\t\t\t_class = \"ModelModifierList\"" );
		text.AppendLine( "\t\t\t\tchildren =" );
		text.AppendLine( "\t\t\t\t[" );
		text.AppendLine( "\t\t\t\t\t{" );
		text.AppendLine( "\t\t\t\t\t\t_class = \"ModelModifier_ScaleAndMirror\"" );
		text.AppendLine( $"\t\t\t\t\t\tscale = {CentimeterScale.ToString( CultureInfo.InvariantCulture )}" );
		text.AppendLine( "\t\t\t\t\t\tmirror_x = false" );
		text.AppendLine( "\t\t\t\t\t\tmirror_y = false" );
		text.AppendLine( "\t\t\t\t\t\tmirror_z = false" );
		text.AppendLine( "\t\t\t\t\t\tflip_bone_forward = false" );
		text.AppendLine( "\t\t\t\t\t\tswap_left_and_right_bones = false" );
		text.AppendLine( "\t\t\t\t\t}," );
		text.AppendLine( "\t\t\t\t]" );
		text.AppendLine( "\t\t\t}," );
	}

	static FileInfo FindMesh( DirectoryInfo folder )
	{
		var models = folder.EnumerateDirectories( "models", SearchOption.TopDirectoryOnly ).FirstOrDefault();

		if ( models is null )
			return null;

		return models
			.EnumerateFiles( "*.*", SearchOption.TopDirectoryOnly )
			.Where( file => MeshExtensions.Contains( file.Extension.ToLowerInvariant() ) )
			.OrderBy( file => MeshRank( file.Extension ) )
			.FirstOrDefault();
	}

	static DirectoryInfo TexturesDir( DirectoryInfo folder )
	{
		return folder.EnumerateDirectories( "textures", SearchOption.TopDirectoryOnly ).FirstOrDefault();
	}

	static string ChannelOf( string file )
	{
		var name = Path.GetFileNameWithoutExtension( file );
		var cut = name.LastIndexOf( '_' );

		if ( cut < 0 )
			return null;

		var token = name[(cut + 1)..].ToLowerInvariant();

		return Array.IndexOf( PbrChannels, token ) >= 0 ? token : null;
	}

	static int MeshRank( string extension )
	{
		var index = Array.IndexOf( MeshExtensions, extension.ToLowerInvariant() );

		return index < 0 ? int.MaxValue : index;
	}

	static bool IsImage( string extension )
	{
		return extension.ToLowerInvariant() is ".png" or ".tga" or ".jpg" or ".jpeg" or ".tiff" or ".psd";
	}

	static string AssetsRoot() => Normalize( Project.Current.GetAssetsPath() );

	static string Normalize( string path ) => path.Replace( '\\', '/' ).TrimEnd( '/' );

	static string Join( string folder, string fileName ) => Normalize( folder ) + "/" + fileName;

	static string ResourcePath( string absolute )
	{
		var root = AssetsRoot() + "/";
		var normalized = Normalize( absolute );

		if ( normalized.StartsWith( root, StringComparison.OrdinalIgnoreCase ) )
			return normalized[root.Length..];

		return normalized;
	}
}
