using Sandbox;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using VNBase.Assets;
using VNBase.UI;
using SandLang;

namespace VNBase;

/// <summary>
/// Responsible for handling visual novel base scripts.
/// </summary>
[Title( "VN Script Player" )]
[Category( "VNBase" )]
public sealed partial class ScriptPlayer : Component
{
	/// <summary>
	/// If not empty, will load the script at this path on initial component start.
	/// </summary>
	[Property] public string? InitialScript { get; set; }

	/// <summary>
	/// The currently active script.
	/// </summary>
	public Script? ActiveScript { get; private set; }

	/// <summary>
	/// The currently active script label text.
	/// </summary>
	[Property, Group( "Dialogue" )] public string? DialogueText { get; set; }

	/// <summary>
	/// Path to the currently active background.
	/// </summary>
	[Property, Group( "Dialogue" )] public string? Background { get; set; }

	/// <summary>
	/// If the dialogue has finished writing text.
	/// </summary>
	[Property, Group( "Dialogue" )] public bool DialogueFinished { get; set; }

	/// <summary>
	/// The currently active speaking character.
	/// </summary>
	public Character? SpeakingCharacter { get; set; }

	/// <summary>
	/// Characters to display for this label.
	/// </summary>
	public List<Character> Characters { get; set; } = new();

	[Property] public Settings Settings { get; } = new();

	private Dialogue? _dialogue;
	private Dialogue.Label? _currentLabel;

	private CancellationTokenSource? _cts;

	protected override void OnStart()
	{
		if ( !string.IsNullOrEmpty( InitialScript ) )
		{
			LoadScript( InitialScript );
		}

		if ( Scene.GetAllComponents<VNHud>().IsNullOrEmpty() )
		{
			Log.Warning( "No VNHud Component found, ScriptPlayer will not be immediately visible!" );
		}
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( Settings.SkipAction ) )
		{
			if ( ActiveScript is null || _currentLabel is null )
			{
				return;
			}

			if ( !DialogueFinished && Settings.SkipActionEnabled )
			{
				SkipDialogue();
			}
			else if ( DialogueChoices.IsNullOrEmpty() )
			{
				ExecuteAfterLabel();
			}
		}
	}

	/// <summary>
	/// Read and Load the script at the provided path.
	/// </summary>
	/// <param name="path">Path to the script to load.</param>
	public void LoadScript( string path )
	{
		var dialogue = FileSystem.Mounted.ReadAllText( path );

		if ( dialogue is null )
		{
			Log.Error( $"Unable to load script! Script file couldn't be found by path: {path}" );
			return;
		}

		if ( !string.IsNullOrEmpty( dialogue ) )
		{
			Script script = new( dialogue, path );
			LoadScript( script );
		}
		else
		{
			Log.Error( "Unable to load script! The script file is empty." );
		}
	}

	/// <summary>
	/// Load the provided Script object.
	/// </summary>
	/// <param name="script">Script to load.</param>
	public void LoadScript( Script script )
	{
		if ( script is null )
		{
			Log.Error( "Unable to load script! Script is null!" );
			return;
		}

		string? scriptName = string.Empty;
		if ( script.FromFile )
		{
			scriptName = Path.GetFileNameWithoutExtension( script.Path );
		}

		Log.Info( $"Loading script: {scriptName}" );

		ActiveScript = script;
		script.OnLoad();

		var dialogue = Dialogue.ParseDialogue( SParen.ParseText( ActiveScript.Dialogue ).ToList() );
		SetEnvironment( dialogue );
		SetCurrentLabel( dialogue.InitialLabel );
	}

	/// <summary>
	/// Unloads the currently active script.
	/// </summary>
	public void UnloadScript()
	{
		if ( ActiveScript is null )
		{
			return;
		}

		_dialogue = null;
		DialogueText = null;
		SpeakingCharacter = null;
		Background = null;
		DialogueChoices.Clear();

		ActiveScript.OnUnload();
		if ( ActiveScript.NextScript is not null )
		{
			LoadScript( ActiveScript.NextScript );
		}
		else
		{
			ActiveScript = null;
		}

		Log.Info( $"Unloaded active script." );
	}

	private async void SetCurrentLabel( Dialogue.Label label )
	{
		_currentLabel = label;
		DialogueFinished = false;

		if ( Game.IsEditor )
		{
			Log.Info( $"Loading Label {label.Name}" );
		}

		Characters.Clear();
		label.Characters.ForEach( Characters.Add );
		SpeakingCharacter = label.SpeakingCharacter;

		foreach ( SoundAsset sound in label.Assets.OfType<SoundAsset>() )
		{
			sound.Play();
			Log.Info( $"Played SoundAsset {sound} from label {label.Name}" );
		}

		try
		{
			Background = label.Assets.OfType<BackgroundAsset>().SingleOrDefault()?.Path;
		}
		catch ( InvalidOperationException )
		{
			Log.Error( $"There can only be one {nameof( BackgroundAsset )} in label {label.Name}!" );
			Background = null;
		}

		_cts = new();

		if ( Settings.TextEffect is not null )
		{
			try
			{
				await Settings.TextEffect.Play( label.Text, Settings.TextEffectDelay, ( text ) => DialogueText = text, _cts.Token );
			}
			catch ( OperationCanceledException )
			{
				DialogueText = label.Text;
			}
		}
		else
		{
			DialogueText = label.Text;
		}

		AddHistory( label );
		DialogueFinished = true;

		if ( ActiveScript is not null )
		{
			DialogueChoices = label.Choices.Where( x => x.IsAvailable( ActiveScript.GetEnvironment() ) ).Select( p => p.ChoiceText ).ToList();
		}
	}

	private void ExecuteAfterLabel()
	{
		if ( ActiveScript is null || _currentLabel is null )
		{
			Log.Error( $"Unable to execute the AfterLabel, there is either no active script or label!" );
			return;
		}

		var afterLabel = _currentLabel.AfterLabel;

		if ( afterLabel is not null )
		{
			foreach ( var codeBlock in afterLabel.CodeBlocks )
			{
				codeBlock.Execute( ActiveScript.GetEnvironment() );
			}

			if ( afterLabel.IsLastLabel )
			{
				UnloadScript();
				return;
			}

			if ( afterLabel.TargetLabel is not null )
			{
				if ( _dialogue is null )
				{
					return;
				}

				SetCurrentLabel( _dialogue.Labels[afterLabel.TargetLabel] );
			}
		}
	}

	/// <summary>
	/// Skip the currently active text effect.
	/// </summary>
	public void SkipDialogue()
	{
		if ( !DialogueFinished && _cts is not null )
		{
			_cts.Cancel();
		}
	}
}
