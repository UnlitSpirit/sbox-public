namespace Editor;

/// <summary>
/// A class that handles the fullscreen behavior for the main editor window.
/// </summary>
internal partial class FullScreenManager
{
	public FullScreenManager()
	{
		EditorEvent.Register( this );
	}

	public bool IsActive => Widget.IsValid();

	/// <summary>
	/// The current fullscreen widget
	/// </summary>
	public Widget Widget { get; private set; }

	/// <summary>
	/// A reference to the previous parent, when killing fullscreen we want to restore the widget 
	/// </summary>
	private Widget PreviousParent { get; set; }

	/// <summary>
	/// Removes the current widget as the full screen widget
	/// </summary>
	public void Clear()
	{
		if ( Widget is null )
			return;

		EditorWindow.DockManager.Visible = true;

		if ( EditorWindow.Console.IsValid() )
		{
			EditorWindow.Console.Input.FocusMode = FocusMode.TabOrClick;
		}

		if ( Widget.IsValid() )
		{
			Widget.Parent = PreviousParent;

			if ( PreviousParent.IsValid() )
			{
				PreviousParent.Layout?.Add( Widget );
			}
		}

		Widget = null;
		PreviousParent = null;
	}

	[EditorEvent.Frame]
	public void OnFrame()
	{
		if ( Widget is null )
			return;

		// If the widget died while fullscreen, restore the docks
		if ( !Widget.IsValid() )
		{
			Clear();
			return;
		}

		if ( Widget.Size != GetTargetSize() || Widget.Position != GetTargetPosition() )
		{
			SetWidgetLayout();
		}
	}

	private Vector2 GetTargetPosition()
	{
		var position = new Vector2( 0, EditorWindow.MenuWidget.Size.y );
		if ( EditorWindow.IsMaximized )
			position += 8;
		return position;
	}

	private Vector2 GetTargetSize()
	{
		var size = EditorWindow.Size - Widget.Position - new Vector2( 0, EditorWindow.StatusBar.Size.y + 4 );
		if ( EditorWindow.IsMaximized )
			size -= 8;
		return size;
	}

	private void SetWidgetLayout()
	{
		if ( !Widget.IsValid() )
			return;

		Widget.Position = GetTargetPosition();
		Widget.Size = GetTargetSize();
	}

	/// <summary>
	/// Sets a widget as the fullscreen widget
	/// </summary>
	/// <param name="widget"></param>
	public void SetWidget( Widget widget )
	{
		Clear();

		if ( !widget.IsValid() )
			return;

		// Store the widget, and the widget's parent so we can restore it later
		Widget = widget;
		PreviousParent = widget.Parent;

		// Set our target widget's parent to the editor's main window, so we can size it properly
		widget.Parent = EditorWindow;

		// The docks are native windows, which always draw above sibling widget content,
		// so the fullscreen widget can't simply cover them - hide them while it's active
		EditorWindow.DockManager.Visible = false;

		// Make sure we kill focus from the console
		if ( EditorWindow.Console.IsValid() )
		{
			EditorWindow.Console.Input.FocusMode = FocusMode.None;
			EditorWindow.Console.Input.Blur();
		}

		// Make sure it's visible now
		widget.Visible = true;

		// Lay the widget out
		SetWidgetLayout();
	}
}
